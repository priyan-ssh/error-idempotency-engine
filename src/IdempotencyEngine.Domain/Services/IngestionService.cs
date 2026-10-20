using System.Text.Json;
using IdempotencyEngine.Domain.Dtos;
using IdempotencyEngine.Domain.Entities;
using IdempotencyEngine.Domain.Exceptions;
using IdempotencyEngine.Domain.Interfaces;
using IdempotencyEngine.Domain.Models;
using IdempotencyEngine.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IdempotencyEngine.Domain.Services;

/// <summary>
/// Implements the core ingestion domain service orchestration covering Cases A, B, and C,
/// peek-before-embed, idempotency gating, deterministic sample ordering, advisory locking,
/// and KNN variant thresholding per distinct (Kind, Domain, ShapeHash, ShapeVersion) group.
/// </summary>
public class IngestionService : IIngestionService
{
    private readonly IIncidentReadRepository _incidentReadRepo;
    private readonly IVariantReadRepository _variantReadRepo;
    private readonly ITransactionHelper _transactionHelper;
    private readonly ITextNormalizer _textNormalizer;
    private readonly IFingerprintGenerator _fingerprintGenerator;
    private readonly IHasher _hasher;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<IngestionService> _logger;
    private readonly IngestionOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionService"/> class with required repositories, helpers, and providers.
    /// </summary>
    /// <param name="incidentReadRepo">The incident read repository.</param>
    /// <param name="variantReadRepo">The variant read repository.</param>
    /// <param name="transactionHelper">The transaction and locking helper.</param>
    /// <param name="textNormalizer">The text normalization utility for shape generation.</param>
    /// <param name="fingerprintGenerator">The context fingerprint generator.</param>
    /// <param name="hasher">The hashing utility for shape hashes.</param>
    /// <param name="embeddingProvider">The embedding generation provider.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The ingestion options configuration.</param>
    public IngestionService(
        IIncidentReadRepository incidentReadRepo,
        IVariantReadRepository variantReadRepo,
        ITransactionHelper transactionHelper,
        ITextNormalizer textNormalizer,
        IFingerprintGenerator fingerprintGenerator,
        IHasher hasher,
        IEmbeddingProvider embeddingProvider,
        ILogger<IngestionService>? logger = null,
        IOptions<IngestionOptions>? options = null)
    {
        _incidentReadRepo = incidentReadRepo;
        _variantReadRepo = variantReadRepo;
        _transactionHelper = transactionHelper;
        _textNormalizer = textNormalizer;
        _fingerprintGenerator = fingerprintGenerator;
        _hasher = hasher;
        _embeddingProvider = embeddingProvider;
        _logger = logger ?? NullLogger<IngestionService>.Instance;
        _options = options?.Value ?? new IngestionOptions();
    }

    /// <summary>
    /// Ingests a batch of error samples, grouping by composite keys, performing peek-before-embed,
    /// acquiring advisory locks when necessary, and processing transactionally.
    /// </summary>
    /// <param name="request">The batch ingestion request containing samples.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The ingestion result containing status, resolved anchor ID, and processed count.</returns>
    public async Task<IngestResult> IngestBatchAsync(IngestBatchRequest request, CancellationToken ct = default)
    {
        if (request.Samples == null || request.Samples.Count == 0)
        {
            return new IngestResult(true, Guid.Empty, 0);
        }

        var batchId = !string.IsNullOrWhiteSpace(request.BatchId) ? request.BatchId : Guid.NewGuid().ToString("N");
        var defaultKind = request.Kind ?? "error";
        var defaultDomain = request.Domain ?? "default";
        var origin = request.Origin;
        var defaultShapeVersion = request.ShapeVersion ?? "v1";

        _logger.LogInformation("Processing Ingestion Batch [BatchId: {BatchId}] with {SampleCount} samples.", batchId, request.Samples.Count);

        var compositeGroups = new Dictionary<string, (string Kind, string Domain, string ShapeVersion, string ShapeTemplate, string ShapeHash, List<IngestSample> Samples)>();

        foreach (var sample in request.Samples)
        {
            var kind = sample.Kind ?? defaultKind;
            var domain = sample.Domain ?? defaultDomain;
            var shapeVersion = defaultShapeVersion;

            var shapeTemplate = _textNormalizer.Normalize(sample.RawText);
            var shapeHash = _hasher.ComputeSha256(shapeTemplate);

            var groupKey = $"{kind}:{domain}:{shapeHash}:{shapeVersion}";

            if (!compositeGroups.ContainsKey(groupKey))
            {
                compositeGroups[groupKey] = (kind, domain, shapeVersion, shapeTemplate, shapeHash, new List<IngestSample>());
            }
            compositeGroups[groupKey].Samples.Add(sample);
        }

        Guid lastResolvedAnchorId = Guid.Empty;
        bool lastIsNewAnchor = false;
        int totalProcessedCount = 0;

        // Process each distinct composite group independently, sorted deterministically by groupKey (Rule 02)
        foreach (var (groupKey, group) in compositeGroups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var kind = group.Kind;
            var domain = group.Domain;
            var shapeVersion = group.ShapeVersion;
            var batchShapeTemplate = group.ShapeTemplate;
            var shapeHash = group.ShapeHash;
            var batchSamples = group.Samples;

            var anchor = await _incidentReadRepo.GetIncidentByShapeAsync(kind, domain, shapeHash, shapeVersion, ct).ConfigureAwait(false);
            var anchorExists = anchor != null;

            var samplePeekResults = new List<(IngestSample Sample, string ContextFingerprint, string ContextJson, Guid? ExistingVariantId)>();

            foreach (var sample in batchSamples)
            {
                var ctxDict = sample.Context != null ? new Dictionary<string, object>(sample.Context) : new Dictionary<string, object>();
                var contextFingerprint = _fingerprintGenerator.ComputeContextFingerprint(ctxDict);
                var contextJson = JsonSerializer.Serialize(ctxDict);

                Guid? existingVariantId = null;
                if (anchorExists)
                {
                    var variant = await _variantReadRepo.GetVariantByFingerprintAsync(anchor!.Id, new[] { contextFingerprint }, ct).ConfigureAwait(false);
                    existingVariantId = variant?.Id;
                }

                samplePeekResults.Add((sample, contextFingerprint, contextJson, existingVariantId));
            }

            var preparedSamples = samplePeekResults
                .OrderBy(s => s.ContextFingerprint, StringComparer.Ordinal)
                .ToList();

            bool allVariantsHit = anchorExists && preparedSamples.All(s => s.ExistingVariantId.HasValue);
            float[]? errorEmbedding = null;
            var missingContextEmbeddings = new Dictionary<string, float[]>();

            if (!allVariantsHit)
            {
                if (!anchorExists)
                {
                    _logger.LogInformation("Case C: Anchor missing for Kind={Kind}, Domain={Domain}, ShapeHash={ShapeHash}. Computing embeddings.", kind, domain, shapeHash);
                    errorEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(batchShapeTemplate, ct).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInformation("Case B: Anchor exists for Kind={Kind}, Domain={Domain}, ShapeHash={ShapeHash}, but some variants missed.", kind, domain, shapeHash);
                }

                foreach (var s in preparedSamples.Where(s => !s.ExistingVariantId.HasValue))
                {
                    if (!missingContextEmbeddings.ContainsKey(s.ContextFingerprint))
                    {
                        var sampleText = $"{s.Sample.RawText} {s.ContextJson}";
                        var ctxEmb = await _embeddingProvider.GenerateEmbeddingAsync(sampleText, ct).ConfigureAwait(false);
                        missingContextEmbeddings[s.ContextFingerprint] = ctxEmb;
                    }
                }
            }
            else
            {
                _logger.LogDebug("Case A: All fingerprints hit for Group={GroupKey}. Zero embedding calls required.", groupKey);
            }

            var lockKey = AdvisoryLockKey.Format(kind, domain, shapeHash, shapeVersion);
            var budgetMs = _options.LockBudgetMs > 0 ? _options.LockBudgetMs : 2000;
            var deadline = DateTime.UtcNow.AddMilliseconds(budgetMs);
            bool acquired = false;
            Guid resolvedAnchorId = Guid.Empty;
            bool isNewAnchor = false;
            int processedCount = 0;

            while (DateTime.UtcNow < deadline && !acquired)
            {
                try
                {
                    if (allVariantsHit)
                    {
                        var caseAResult = await _transactionHelper.ExecuteInTransactionAsync(
                            ctx => ProcessCaseAAsync(ctx, anchor!.Id, preparedSamples, ct),
                            ct
                        ).ConfigureAwait(false);

                        resolvedAnchorId = caseAResult.AnchorId;
                        processedCount = caseAResult.ProcessedCount;
                        break;
                    }
                    else
                    {
                        acquired = await _transactionHelper.ExecuteInTransactionAsync(
                            async ctx =>
                            {
                                var lockAcquired = await ctx.TryAcquireAdvisoryXactLockAsync(lockKey, ct).ConfigureAwait(false);
                                if (!lockAcquired) return false;

                                var caseBResult = await ProcessCaseBCAsync(
                                    ctx, kind, domain, shapeHash, shapeVersion, batchShapeTemplate,
                                    origin, errorEmbedding, preparedSamples, missingContextEmbeddings, ct
                                ).ConfigureAwait(false);

                                resolvedAnchorId = caseBResult.AnchorId;
                                isNewAnchor = caseBResult.IsNewAnchor;
                                processedCount = caseBResult.ProcessedCount;
                                return true;
                            },
                            ct
                        ).ConfigureAwait(false);

                        if (acquired)
                        {
                            break;
                        }
                    }
                }
                catch (Exception) when (!allVariantsHit)
                {
                    // Lock contention or retryable failure; wait with jitter and retry
                    var jitter = Random.Shared.Next(0, _options.LockRetryJitterMs > 0 ? _options.LockRetryJitterMs : 25);
                    var delay = (_options.LockRetryIntervalMs > 0 ? _options.LockRetryIntervalMs : 50) + jitter;
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }

            if (!allVariantsHit && !acquired)
            {
                _logger.LogWarning("Advisory lock acquisition timed out after {BudgetMs}ms for key {LockKey}", budgetMs, lockKey);
                throw new AdvisoryLockTimeoutException(lockKey, 1);
            }

            if (isNewAnchor && resolvedAnchorId != Guid.Empty)
            {
                _logger.LogInformation("New anchor created with ID {AnchorId}. Firing post-commit notification.", resolvedAnchorId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _transactionHelper.NotifyAnchorCreatedAsync(resolvedAnchorId, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send background post-commit notification for AnchorId={AnchorId}", resolvedAnchorId);
                    }
                }, CancellationToken.None);
            }

            lastResolvedAnchorId = resolvedAnchorId;
            lastIsNewAnchor = isNewAnchor;
            totalProcessedCount += processedCount;
        }

        return new IngestResult(true, lastResolvedAnchorId, totalProcessedCount);
    }

    /// <summary>
    /// Processes Case A (all variants hit) within a transaction, claiming ingest events and incrementing occurrence counts.
    /// </summary>
    /// <param name="ctx">The ingestion transaction context.</param>
    /// <param name="anchorId">The resolved incident anchor ID.</param>
    /// <param name="preparedSamples">The prepared sample list with existing variant IDs.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A tuple containing the anchor ID and total processed count.</returns>
    private async Task<(Guid AnchorId, int ProcessedCount)> ProcessCaseAAsync(
        IIngestionTransactionContext ctx,
        Guid anchorId,
        List<(IngestSample Sample, string ContextFingerprint, string ContextJson, Guid? ExistingVariantId)> preparedSamples,
        CancellationToken ct)
    {
        int processedCount = 0;
        foreach (var s in preparedSamples)
        {
            var claimedEventId = await ctx.TryClaimIngestEventAsync(s.Sample.EventId, ct).ConfigureAwait(false);
            if (claimedEventId == null) continue;

            await ctx.IncrementVariantOccurrenceCountAsync(
                s.ExistingVariantId!.Value, s.Sample.CollapsedCount, false, ct
            ).ConfigureAwait(false);
            processedCount++;
        }

        return (anchorId, processedCount);
    }

    /// <summary>
    /// Processes Case B or C (missing anchor or variants) within an advisory locked transaction, creating anchors, variants, or matching via KNN.
    /// </summary>
    /// <param name="ctx">The ingestion transaction context.</param>
    /// <param name="kind">The error kind.</param>
    /// <param name="domain">The error domain.</param>
    /// <param name="batchShapeHash">The shape hash.</param>
    /// <param name="shapeVersion">The shape version.</param>
    /// <param name="batchShapeTemplate">The normalized shape template text.</param>
    /// <param name="origin">The source origin.</param>
    /// <param name="errorEmbedding">The pre-computed error embedding vector, if applicable.</param>
    /// <param name="preparedSamples">The prepared sample list.</param>
    /// <param name="missingContextEmbeddings">Dictionary of pre-computed context embeddings for missing variants.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A tuple containing the resolved anchor ID, whether a new anchor was created, and the processed count.</returns>
    private async Task<(Guid AnchorId, bool IsNewAnchor, int ProcessedCount)> ProcessCaseBCAsync(
        IIngestionTransactionContext ctx,
        string kind,
        string domain,
        string batchShapeHash,
        string shapeVersion,
        string batchShapeTemplate,
        string? origin,
        float[]? errorEmbedding,
        List<(IngestSample Sample, string ContextFingerprint, string ContextJson, Guid? ExistingVariantId)> preparedSamples,
        Dictionary<string, float[]> missingContextEmbeddings,
        CancellationToken ct)
    {
        Guid innerAnchorId;
        bool newAnchorCreated = false;

        // Re-check anchor under lock
        var currentAnchor = await _incidentReadRepo.GetIncidentByShapeAsync(kind, domain, batchShapeHash, shapeVersion, ct).ConfigureAwait(false);
        if (currentAnchor == null)
        {
            innerAnchorId = Guid.NewGuid();
            var newIncident = new ProcessedIncident(
                Id: innerAnchorId,
                Kind: kind,
                Domain: domain,
                ShapeHash: batchShapeHash,
                ShapeVersion: shapeVersion,
                ShapeTemplate: batchShapeTemplate,
                Origin: origin,
                ErrorEmbedding: errorEmbedding!,
                EmbeddingModel: _embeddingProvider.ModelName,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow
            );

            var anchorRes = await ctx.InsertAnchorIfNotExistsAsync(newIncident, ct).ConfigureAwait(false);
            innerAnchorId = anchorRes.Id;
            newAnchorCreated = anchorRes.IsNew;
        }
        else
        {
            innerAnchorId = currentAnchor.Id;
        }

        int processedCount = 0;
        foreach (var s in preparedSamples)
        {
            var claimedEventId = await ctx.TryClaimIngestEventAsync(s.Sample.EventId, ct).ConfigureAwait(false);
            if (claimedEventId == null) continue;

            var variantId = await ctx.RecheckVariantByFingerprintAsync(innerAnchorId, s.ContextFingerprint, ct).ConfigureAwait(false);
            if (variantId.HasValue)
            {
                await ctx.IncrementVariantOccurrenceCountAsync(
                    variantId.Value, s.Sample.CollapsedCount, false, ct
                ).ConfigureAwait(false);
            }
            else
            {
                var contextEmb = missingContextEmbeddings.TryGetValue(s.ContextFingerprint, out var emb)
                    ? emb
                    : await _embeddingProvider.GenerateEmbeddingAsync($"{s.Sample.RawText} {s.ContextJson}", ct).ConfigureAwait(false);

                var knn = await ctx.FindNearestVariantKnnAsync(innerAnchorId, contextEmb, _embeddingProvider.ModelName, ct).ConfigureAwait(false);

                if (knn != null && knn.Distance < _options.KnnMatchThreshold)
                {
                    await ctx.IncrementVariantOccurrenceCountAsync(
                        knn.VariantId, s.Sample.CollapsedCount, true, ct
                    ).ConfigureAwait(false);
                }
                else
                {
                    string status = (knn != null && knn.Distance < _options.KnnReviewThreshold) ? "needs_review" : "active";
                    var newVariant = new IncidentVariant(
                        Id: Guid.NewGuid(),
                        IncidentId: innerAnchorId,
                        ContextFingerprint: s.ContextFingerprint,
                        ContextEmbedding: contextEmb,
                        EmbeddingModel: _embeddingProvider.ModelName,
                        ContextJson: s.ContextJson,
                        ContextSample: s.Sample.RawText,
                        Status: status,
                        Verified: false,
                        MergedByEmbedding: false,
                        OccurrenceCount: s.Sample.CollapsedCount,
                        Version: 1,
                        FirstSeenAt: DateTimeOffset.UtcNow,
                        LastSeenAt: DateTimeOffset.UtcNow
                    );

                    await ctx.InsertVariantAsync(newVariant, ct).ConfigureAwait(false);
                }
            }
            processedCount++;
        }

        return (innerAnchorId, newAnchorCreated, processedCount);
    }
}


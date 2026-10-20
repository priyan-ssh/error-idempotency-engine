using IdempotencyEngine.Domain.Entities;
using IdempotencyEngine.Domain.Interfaces;
using Dapper;
using Npgsql;

namespace IdempotencyEngine.Data.Services.Ingest;

/// <summary>
/// Implements domain-level transaction context (Unit of Work) wrapping active Npgsql connection and transaction.
/// </summary>
public class PostgresIngestionContext : IIngestionTransactionContext
{
    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction _tx;

    public PostgresIngestionContext(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
    }

    /// <summary>
    /// Attempts to acquire a PostgreSQL advisory lock for the current transaction using the provided lock key.
    /// </summary>
    /// <param name="lockKey"></param>
    /// <param name="ct"></param>
    /// <returns>True if the lock was acquired, false otherwise</returns>
    public async Task<bool> TryAcquireAdvisoryXactLockAsync(string lockKey, CancellationToken ct)
    {
        const string sql = "SELECT pg_try_advisory_xact_lock(hashtextextended(@LockKey, 0));";
        var cmd = new CommandDefinition(sql, new { LockKey = lockKey }, transaction: _tx, commandTimeout: 5, cancellationToken: ct);
        return await _conn.QuerySingleAsync<bool>(cmd).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to claim an ingest event by inserting its event ID into the ProcessedIngests table.
    /// </summary>
    /// <param name="eventId"></param>
    /// <param name="ct"></param>
    /// <returns>The claimed event ID if successful, null otherwise</returns>
    public async Task<string?> TryClaimIngestEventAsync(string eventId, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO ProcessedIngests (event_id)
            VALUES (@EventId)
            ON CONFLICT (event_id) DO NOTHING
            RETURNING event_id;";

        var cmd = new CommandDefinition(sql, new { EventId = eventId }, transaction: _tx, commandTimeout: 5, cancellationToken: ct);
        return await _conn.QuerySingleOrDefaultAsync<string?>(cmd).ConfigureAwait(false);
    }

    /// <summary>
    /// Increments the occurrence count of a specific incident variant and updates its last seen timestamp.
    /// </summary>
    /// <param name="variantId"></param>
    /// <param name="count"></param>
    /// <param name="mergedByEmbedding"></param>
    /// <param name="ct"></param>
    public async Task IncrementVariantOccurrenceCountAsync(Guid variantId, long count, bool mergedByEmbedding, CancellationToken ct)
    {
        const string sql = @"
            UPDATE IncidentVariants
            SET occurrence_count = occurrence_count + @Count,
                merged_by_embedding = merged_by_embedding OR @MergedByEmbedding,
                last_seen_at = NOW()
            WHERE id = @VariantId;";

        var cmd = new CommandDefinition(sql, new { VariantId = variantId, Count = count, MergedByEmbedding = mergedByEmbedding }, transaction: _tx, commandTimeout: 5, cancellationToken: ct);
        await _conn.ExecuteAsync(cmd).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new incident anchor into the ProcessedIncidents table if it does not already exist, based on its unique shape and version.
    /// </summary>
    /// <param name="incident"></param>
    /// <param name="ct"></param>
    /// <returns>A tuple containing the incident ID and a boolean indicating if it was newly inserted</returns>
    public async Task<(Guid Id, bool IsNew)> InsertAnchorIfNotExistsAsync(ProcessedIncident incident, CancellationToken ct)
    {
        const string insertSql = @"
            INSERT INTO ProcessedIncidents (id, kind, domain, shape_hash, shape_version, shape_template, origin, error_embedding, embedding_model, created_at, updated_at)
            VALUES (@Id, @Kind, @Domain, @ShapeHash, @ShapeVersion, @ShapeTemplate, @Origin, @ErrorEmbedding::vector, @EmbeddingModel, NOW(), NOW())
            ON CONFLICT (kind, domain, shape_hash, shape_version) DO NOTHING
            RETURNING id;";

        var cmd = new CommandDefinition(
            insertSql,
            new
            {
                Id = incident.Id,
                Kind = incident.Kind,
                Domain = incident.Domain,
                ShapeHash = incident.ShapeHash,
                ShapeVersion = incident.ShapeVersion,
                ShapeTemplate = incident.ShapeTemplate,
                Origin = incident.Origin,
                ErrorEmbedding = incident.ErrorEmbedding,
                EmbeddingModel = incident.EmbeddingModel
            },
            transaction: _tx,
            commandTimeout: 5,
            cancellationToken: ct
        );

        var returnedId = await _conn.QuerySingleOrDefaultAsync<Guid?>(cmd).ConfigureAwait(false);
        if (returnedId.HasValue && returnedId.Value != Guid.Empty)
        {
            return (returnedId.Value, true);
        }

        const string selectSql = @"
            SELECT id FROM ProcessedIncidents
            WHERE kind = @Kind AND domain = @Domain AND shape_hash = @ShapeHash AND shape_version = @ShapeVersion;";

        var selectCmd = new CommandDefinition(
            selectSql,
            new { Kind = incident.Kind, Domain = incident.Domain, ShapeHash = incident.ShapeHash, ShapeVersion = incident.ShapeVersion },
            transaction: _tx,
            commandTimeout: 5,
            cancellationToken: ct
        );

        var existingId = await _conn.QuerySingleAsync<Guid>(selectCmd).ConfigureAwait(false);
        return (existingId, false);
    }

    /// <summary>
    /// Attempts to recheck an incident variant by its associated incident ID and context fingerprint, returning the variant ID if found.
    /// </summary>
    /// <param name="incidentId"></param>
    /// <param name="fingerprint"></param>
    /// <param name="ct"></param>
    /// <returns>The variant ID if found, null otherwise</returns>
    public async Task<Guid?> RecheckVariantByFingerprintAsync(Guid incidentId, string fingerprint, CancellationToken ct)
    {
        const string sql = @"
            SELECT id FROM IncidentVariants
            WHERE incident_id = @IncidentId AND context_fingerprint = @Fingerprint;";

        var cmd = new CommandDefinition(sql, new { IncidentId = incidentId, Fingerprint = fingerprint }, transaction: _tx, commandTimeout: 5, cancellationToken: ct);
        return await _conn.QuerySingleOrDefaultAsync<Guid?>(cmd).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the nearest incident variant to a given context embedding using KNN search.
    /// </summary>
    /// <param name="incidentId"></param>
    /// <param name="contextEmbedding"></param>
    /// <param name="embeddingModel"></param>
    /// <param name="ct"></param>
    /// <returns>The nearest variant result if found, null otherwise</returns>
    public async Task<VariantKNNResult?> FindNearestVariantKnnAsync(Guid incidentId, float[] contextEmbedding, string embeddingModel, CancellationToken ct)
    {
        const string sql = @"
            SELECT id AS VariantId, (context_embedding <=> @Vector::vector) AS Distance
            FROM IncidentVariants
            WHERE incident_id = @IncidentId AND embedding_model = @EmbeddingModel
            ORDER BY context_embedding <=> @Vector::vector ASC
            LIMIT 1;";

        var cmd = new CommandDefinition(
            sql,
            new { IncidentId = incidentId, Vector = contextEmbedding, EmbeddingModel = embeddingModel },
            transaction: _tx,
            commandTimeout: 5,
            cancellationToken: ct
        );

        return await _conn.QuerySingleOrDefaultAsync<VariantKNNResult>(cmd).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new incident variant into the IncidentVariants table.
    /// </summary>
    /// <param name="variant"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task InsertVariantAsync(IncidentVariant variant, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO IncidentVariants (
                id, incident_id, context_fingerprint, context_embedding, embedding_model,
                context_json, context_sample, status, verified, merged_by_embedding,
                occurrence_count, version, first_seen_at, last_seen_at
            )
            VALUES (
                @Id, @IncidentId, @ContextFingerprint, @ContextEmbedding::vector, @EmbeddingModel,
                @ContextJson::jsonb, @ContextSample, @Status, @Verified, @MergedByEmbedding,
                @OccurrenceCount, @Version, @FirstSeenAt, @LastSeenAt
            );";

        var cmd = new CommandDefinition(
            sql,
            new
            {
                Id = variant.Id,
                IncidentId = variant.IncidentId,
                ContextFingerprint = variant.ContextFingerprint,
                ContextEmbedding = variant.ContextEmbedding,
                EmbeddingModel = variant.EmbeddingModel,
                ContextJson = variant.ContextJson,
                ContextSample = variant.ContextSample,
                Status = variant.Status,
                Verified = variant.Verified,
                MergedByEmbedding = variant.MergedByEmbedding,
                OccurrenceCount = variant.OccurrenceCount,
                Version = variant.Version,
                FirstSeenAt = variant.FirstSeenAt,
                LastSeenAt = variant.LastSeenAt
            },
            transaction: _tx,
            commandTimeout: 5,
            cancellationToken: ct
        );

        await _conn.ExecuteAsync(cmd).ConfigureAwait(false);
    }
}

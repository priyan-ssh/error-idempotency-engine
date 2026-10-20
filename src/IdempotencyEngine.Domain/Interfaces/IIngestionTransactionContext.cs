using IdempotencyEngine.Domain.Entities;

namespace IdempotencyEngine.Domain.Interfaces;

/// <summary>
/// Domain-level contract representing an active transactional context (Unit of Work)
/// during ingestion critical section execution. Decouples domain logic from database infrastructure.
/// </summary>
public interface IIngestionTransactionContext
{
    /// <summary>
    /// Attempts to acquire a transaction-scoped 64-bit advisory lock using hashtextextended.
    /// </summary>
    /// <param name="lockKey">The unique lock key string.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the lock was acquired; otherwise, false.</returns>
    Task<bool> TryAcquireAdvisoryXactLockAsync(string lockKey, CancellationToken ct = default);

    /// <summary>
    /// Attempts to claim an event in ProcessedIngests. Returns event_id if newly claimed, or null if duplicate.
    /// </summary>
    /// <param name="eventId">The unique stable event ID.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The event ID if claimed, otherwise null.</returns>
    Task<string?> TryClaimIngestEventAsync(string eventId, CancellationToken ct = default);

    /// <summary>
    /// Increments the occurrence count of an existing variant.
    /// </summary>
    /// <param name="variantId">The unique ID of the incident variant.</param>
    /// <param name="count">The count to increment by.</param>
    /// <param name="mergedByEmbedding">Whether merged via vector KNN.</param>
    /// <param name="ct">A cancellation token.</param>
    Task IncrementVariantOccurrenceCountAsync(Guid variantId, long count, bool mergedByEmbedding, CancellationToken ct = default);

    /// <summary>
    /// Inserts an incident anchor if not exists, returning its ID and whether it was newly created.
    /// </summary>
    /// <param name="incident">The processed incident anchor entity.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A tuple of the anchor ID and a boolean indicating if it is new.</returns>
    Task<(Guid Id, bool IsNew)> InsertAnchorIfNotExistsAsync(ProcessedIncident incident, CancellationToken ct = default);

    /// <summary>
    /// Re-checks whether a variant with the exact context fingerprint exists under an anchor.
    /// </summary>
    /// <param name="incidentId">The anchor ID.</param>
    /// <param name="fingerprint">The context fingerprint.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The variant ID if found, otherwise null.</returns>
    Task<Guid?> RecheckVariantByFingerprintAsync(Guid incidentId, string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Finds the nearest variant using exact vector KNN distance.
    /// </summary>
    /// <param name="incidentId">The anchor ID.</param>
    /// <param name="contextEmbedding">The context vector embedding.</param>
    /// <param name="embeddingModel">The active embedding model name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The nearest variant KNN result if found, otherwise null.</returns>
    Task<VariantKNNResult?> FindNearestVariantKnnAsync(Guid incidentId, float[] contextEmbedding, string embeddingModel, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new incident variant.
    /// </summary>
    /// <param name="variant">The incident variant entity.</param>
    /// <param name="ct">A cancellation token.</param>
    Task InsertVariantAsync(IncidentVariant variant, CancellationToken ct = default);
}

using IdempotencyEngine.Domain.Entities;
using Npgsql;

namespace IdempotencyEngine.Domain.Interfaces;

/// <summary>
/// Contract for Dapper-based write repositories handling ingestion critical sections,
/// advisory locking, idempotency gating, and variant persistence.
/// </summary>
public interface IIngestionWriteRepository
{
    /// <summary>
    /// Attempts to claim an ingest event in ProcessedIngests.
    /// Returns the event_id if newly claimed, or null if already processed.
    /// </summary>
    Task<string?> TryClaimIngestEventAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string eventId, CancellationToken ct = default);

    /// <summary>
    /// Increments the occurrence count of an existing variant.
    /// </summary>
    Task IncrementVariantOccurrenceCountAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid variantId, long count, bool mergedByEmbedding, CancellationToken ct = default);

    /// <summary>
    /// Inserts an incident anchor if not exists, returning its ID.
    /// </summary>
    Task<(Guid Id, bool IsNew)> InsertAnchorIfNotExistsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        ProcessedIncident incident,
        CancellationToken ct = default);

    /// <summary>
    /// Re-checks whether a variant with the exact context fingerprint exists under an anchor.
    /// </summary>
    Task<Guid?> RecheckVariantByFingerprintAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid incidentId, string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Finds the nearest variant using exact vector KNN distance.
    /// </summary>
    Task<VariantKNNResult?> FindNearestVariantKnnAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid incidentId, float[] contextEmbedding, string embeddingModel, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new incident variant.
    /// </summary>
    Task InsertVariantAsync(NpgsqlConnection conn, NpgsqlTransaction tx, IncidentVariant variant, CancellationToken ct = default);

    /// <summary>
    /// Attempts to acquire a transaction-scoped 64-bit advisory lock using hashtextextended.
    /// </summary>
    Task<bool> TryAcquireAdvisoryXactLockAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string lockKey, CancellationToken ct = default);

    /// <summary>
    /// Fires a post-commit NOTIFY anchor_created notification.
    /// </summary>
    Task NotifyAnchorCreatedAsync(Guid anchorId, CancellationToken ct = default);
}

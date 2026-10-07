using IdempotencyEngine.Domain.Entities;

namespace IdempotencyEngine.Domain.Interfaces;

public interface IVariantReadRepository
{
    /// <summary>
    /// Retrieves an incident variant by its associated incident ID and context fingerprint.
    /// </summary>
    /// <param name="incidentId"></param>
    /// <param name="contextFingerprint"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The incident variant if found; otherwise, null.</returns>
    Task<IncidentVariant?> GetVariantByFingerprintAsync(
        Guid incidentId,
        IEnumerable<string> contextFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an incident variant by its unique variant ID.
    /// </summary>
    /// <param name="variantId">The unique ID of the incident variant.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The incident variant if found; otherwise, null.</returns>
    Task<IncidentVariant?> GetVariantByIdAsync(
        Guid variantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the nearest incident variant based on the provided context vector and active model.
    /// </summary>
    /// <param name="incidentId"></param>
    /// <param name="contextVector"></param>
    /// <param name="activeModel"></param>
    /// <param name="ct"></param>
    /// <returns>The nearest incident variant if found; otherwise, null.</returns>
    Task<VariantKNNResult?> FindNearestVariantAsync(
        Guid incidentId,
        float[] contextVector,
        string activeModel,
        CancellationToken ct = default);

}

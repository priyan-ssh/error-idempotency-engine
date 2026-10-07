using IdempotencyEngine.Domain.Entities;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IdempotencyEngine.Data.Services;

/// <summary>
/// Repository for reading incident variants from the database.
/// </summary>
public class VariantReadRepository : IVariantReadRepository
{
    private readonly IdempotencyDbContext _dbContext;

    public VariantReadRepository(IdempotencyDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Finds the nearest variant for a given incident based on the provided context vector and active model.
    /// </summary>
    /// <param name="incidentId"></param>
    /// <param name="contextVector"></param>
    /// <param name="activeModel"></param>
    /// <param name="ct"></param>
    /// <returns>The nearest variant if found, otherwise null.</returns>
    public async Task<VariantKNNResult?> FindNearestVariantAsync(Guid incidentId, float[] contextVector, string activeModel, CancellationToken ct = default)
    {
        //TODO: Implement KNN search logic here. This is a placeholder for the actual implementation.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Retrieves an incident variant by its associated incident ID and context fingerprint.
    /// </summary>
    /// <param name="incidentId"></param>
    /// <param name="contextFingerprint"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The incident variant if found, otherwise null.</returns>
    public async Task<IncidentVariant?> GetVariantByFingerprintAsync(Guid incidentId, IEnumerable<string> contextFingerprint, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IncidentVariants
            .AsNoTracking()
            .FirstOrDefaultAsync(v => 
                v.IncidentId == incidentId &&
                v.ContextFingerprint.Equals(contextFingerprint), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves an incident variant by its unique variant ID.
    /// </summary>
    /// <param name="variantId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The incident variant if found, otherwise null.</returns>
    public async Task<IncidentVariant?> GetVariantByIdAsync(Guid variantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IncidentVariants
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == variantId, cancellationToken)
            .ConfigureAwait(false);
    }
}

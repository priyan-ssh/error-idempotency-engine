using IdempotencyEngine.Data;
using IdempotencyEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdempotencyEngine.Domain.Interfaces;

/// <summary>
/// Represents a repository for reading incidents from the database.
/// </summary>
public class IncidentReadRepository : IIncidentReadRepository
{
    private readonly IdempotencyDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncidentReadRepository"/> class with the specified database context.
    /// </summary>
    /// <param name="dbContext"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public IncidentReadRepository(IdempotencyDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Retrieves an incident by its unique identifier.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The incident if found, otherwise null.</returns>
    public async Task<ProcessedIncident?> GetIncidentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
         return await _dbContext.ProcessedIncidents
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);

    }

    /// <summary>
    /// Retrieves an incident by its shape and version.
    /// </summary>
    /// <param name="kind"></param>
    /// <param name="domain"></param>
    /// <param name="shapeHash"></param>
    /// <param name="shapeVersion"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The incident if found, otherwise null.</returns>
    public async Task<ProcessedIncident?> GetIncidentByShapeAsync(string kind, string domain, string shapeHash, string shapeVersion, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProcessedIncidents
            .AsNoTracking()
            .FirstOrDefaultAsync(i => 
                i.Kind == kind &&
                i.Domain == domain &&
                i.ShapeHash == shapeHash &&
                i.ShapeVersion == shapeVersion, cancellationToken)
            .ConfigureAwait(false);
    }
}
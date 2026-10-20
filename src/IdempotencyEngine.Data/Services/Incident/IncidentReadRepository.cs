using IdempotencyEngine.Data.DbContexts;
using IdempotencyEngine.Domain.Entities;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IdempotencyEngine.Data.Services.Incident;

/// <summary>
/// Represents a repository for reading incidents from the database.
/// </summary>
public class IncidentReadRepository : IIncidentReadRepository
{
    private readonly IdempotencyDbContext _dbContext;
    private readonly IEmbeddingProvider _embeddingProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncidentReadRepository"/> class with the specified database context and embedding provider.
    /// </summary>
    /// <param name="dbContext"></param>
    /// <param name="embeddingProvider"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public IncidentReadRepository(IdempotencyDbContext dbContext, IEmbeddingProvider embeddingProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
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
        var activeModel = _embeddingProvider.ModelName;
        return await _dbContext.ProcessedIncidents
            .AsNoTracking()
            .FirstOrDefaultAsync(i => 
                i.Kind == kind &&
                i.Domain == domain &&
                i.ShapeHash == shapeHash &&
                i.ShapeVersion == shapeVersion &&
                i.EmbeddingModel == activeModel, cancellationToken)
            .ConfigureAwait(false);
    }
}
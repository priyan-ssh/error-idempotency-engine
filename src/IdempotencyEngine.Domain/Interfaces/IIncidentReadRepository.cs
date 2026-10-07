using IdempotencyEngine.Domain.Entities;

namespace IdempotencyEngine.Domain.Interfaces;

public interface IIncidentReadRepository
{
    /// <summary>
    /// Retrieves an incident by its shape and version.
    /// </summary>
    /// <param name="kind"></param>
    /// <param name="domain"></param>
    /// <param name="shapeHash"></param>
    /// <param name="shapeVersion"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The incident if found; otherwise, null.</returns>
    Task<ProcessedIncident?> GetIncidentByShapeAsync(string kind,string domain, string shapeHash, string shapeVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an incident by id
    /// </summary>
    /// <param name="id">The unique id of the incident.</param>
    /// <returns>The incident if found; otherwise, null.</returns>
    Task<ProcessedIncident?> GetIncidentByIdAsync(Guid id, CancellationToken cancellationToken = default);

}

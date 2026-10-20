using IdempotencyEngine.Domain.Dtos;

namespace IdempotencyEngine.Domain.Interfaces;

/// <summary>
/// Orchestrates ingestion batches through Cases A, B, and C service workflows.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Processes a batch of ingestion requests, determining the appropriate case (A, B, or C) for each request and executing the corresponding workflow.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="ct"></param>
    /// <returns>Result of the ingestion process</returns>
    Task<IngestResult> IngestBatchAsync(IngestBatchRequest request, CancellationToken ct = default);
}

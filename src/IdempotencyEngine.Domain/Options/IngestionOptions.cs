using System.ComponentModel.DataAnnotations;

namespace IdempotencyEngine.Domain.Options;

/// <summary>
/// Configuration options for the ingestion critical section engine.
/// Bound from appsettings.json under "Ingestion".
/// </summary>
public class IngestionOptions
{
    public const string SectionName = "Ingestion";

    [Range(100, 30000)]
    public int LockBudgetMs { get; set; } = 2000;

    [Range(10, 1000)]
    public int LockRetryIntervalMs { get; set; } = 50;

    [Range(0, 500)]
    public int LockRetryJitterMs { get; set; } = 25;

    [Range(0.001, 1.0)]
    public float KnnMatchThreshold { get; set; } = 0.10f;

    [Range(0.001, 1.0)]
    public float KnnReviewThreshold { get; set; } = 0.20f;
}

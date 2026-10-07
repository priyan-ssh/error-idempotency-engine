using System.Collections.Generic;

namespace IdempotencyEngine.Domain.Dtos;

public record IngestBatchRequest(
    string Kind,
    string Domain,
    string? Origin,
    string? RulesHash,
    string? ShapeVersion,
    IReadOnlyList<IngestSample> Samples
);

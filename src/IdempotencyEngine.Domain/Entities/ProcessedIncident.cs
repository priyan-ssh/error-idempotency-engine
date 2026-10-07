using System;

namespace IdempotencyEngine.Domain.Entities;

public record ProcessedIncident(
    Guid Id,
    string Kind,
    string Domain,
    string ShapeHash,
    string ShapeVersion,
    string ShapeTemplate,
    string? Origin,
    float[] ErrorEmbedding,
    string EmbeddingModel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

using System;

namespace IdempotencyEngine.Domain.Entities;

public record IncidentVariant(
    Guid Id,
    Guid IncidentId,
    string ContextFingerprint,
    float[] ContextEmbedding,
    string EmbeddingModel,
    string ContextJson,
    string? ContextSample,
    string Status,
    bool Verified,
    bool MergedByEmbedding,
    long OccurrenceCount,
    long Version,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt
);

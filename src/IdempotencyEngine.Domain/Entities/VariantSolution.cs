using System;

namespace IdempotencyEngine.Domain.Entities;

public record VariantSolution(
    Guid Id,
    Guid VariantId,
    string SolutionText,
    string? PatchContent,
    string? SolutionType,
    double? ConfidenceScore,
    bool Verified,
    DateTimeOffset RecordedAt,
    DateTimeOffset? SupersededAt,
    string? SupersededReason
);

using System;

namespace IdempotencyEngine.Domain.Entities;

public record SolutionFailure(
    Guid Id,
    Guid VariantId,
    Guid? SolutionId,
    string? FailureReason,
    DateTimeOffset RecordedAt
);

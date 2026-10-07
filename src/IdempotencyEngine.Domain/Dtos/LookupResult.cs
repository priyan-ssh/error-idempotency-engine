using System;

namespace IdempotencyEngine.Domain.Dtos;

public record LookupResult(
    bool Found,
    Guid? AnchorId,
    Guid? VariantId,
    string? Solution,
    float? Distance = null,
    bool Verified = false
);

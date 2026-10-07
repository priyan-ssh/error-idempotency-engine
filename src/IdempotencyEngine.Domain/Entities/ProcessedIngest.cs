using System;

namespace IdempotencyEngine.Domain.Entities;

public record ProcessedIngest(
    string EventId,
    DateTimeOffset ProcessedAt
);

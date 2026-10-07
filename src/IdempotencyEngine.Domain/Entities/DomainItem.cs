using System;

namespace IdempotencyEngine.Domain.Entities;

public record DomainItem(
    string Name,
    string? Description,
    string ShapeVersion,
    DateTimeOffset CreatedAt
);

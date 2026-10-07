using System;

namespace IdempotencyEngine.Domain.Entities;

public record IncidentLink(
    Guid Id,
    Guid FromIncident,
    Guid ToIncident,
    float Similarity,
    string Relation,
    string Kind,
    DateTimeOffset CreatedAt
);

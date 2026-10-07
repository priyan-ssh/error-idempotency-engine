using System.Collections.Generic;

namespace IdempotencyEngine.Domain.Dtos;

public record LookupRequest(
    string Kind,
    string Domain,
    string Text,
    IReadOnlyDictionary<string, object>? Context = null
);

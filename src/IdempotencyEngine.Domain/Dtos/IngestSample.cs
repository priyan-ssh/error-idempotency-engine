using System.Collections.Generic;

namespace IdempotencyEngine.Domain.Dtos;

public record IngestSample(
    string EventId,
    string RawText,
    IReadOnlyDictionary<string, object>? Context = null,
    long CollapsedCount = 1
);

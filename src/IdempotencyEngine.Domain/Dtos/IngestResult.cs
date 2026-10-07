using System;
using System.Collections.Generic;

namespace IdempotencyEngine.Domain.Dtos;

public record IngestResult(
    bool Success,
    Guid? IncidentId,
    int ProcessedCount,
    IReadOnlyList<string>? ProcessedEventIds = null,
    string? Message = null
);

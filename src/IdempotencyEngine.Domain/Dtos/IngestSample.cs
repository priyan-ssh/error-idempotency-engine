using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IdempotencyEngine.Domain.Dtos;

public record IngestSample(
    [Required(ErrorMessage = "EventId is required.")]
    string EventId,

    [Required(ErrorMessage = "RawText is required.")]
    string RawText,

    IReadOnlyDictionary<string, object>? Context = null,

    [Range(1, long.MaxValue, ErrorMessage = "CollapsedCount must be at least 1.")]
    long CollapsedCount = 1
);

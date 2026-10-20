using System.ComponentModel.DataAnnotations;

namespace IdempotencyEngine.Domain.Dtos;

public record IngestBatchRequest(
    string? BatchId,

    [Required(ErrorMessage = "Kind is required.")]
    string? Kind,

    [Required(ErrorMessage = "Domain is required.")]
    string? Domain,

    string? Origin,
    string? RulesHash,
    string? ShapeVersion,

    [Required(ErrorMessage = "Samples collection cannot be null.")]
    [MinLength(1, ErrorMessage = "At least one sample must be provided in the batch.")]
    IReadOnlyList<IngestSample> Samples
);

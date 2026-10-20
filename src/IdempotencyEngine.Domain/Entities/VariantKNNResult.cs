namespace IdempotencyEngine.Domain.Entities;

public class VariantKNNResult
{
    public Guid VariantId { get; set; }
    public Guid IncidentId { get; set; }
    public double Distance { get; set; }
}

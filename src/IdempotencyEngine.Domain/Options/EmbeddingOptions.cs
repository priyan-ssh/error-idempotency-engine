namespace IdempotencyEngine.Domain.Options;

/// <summary>
/// Configuration options for the vector embedding model.
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// Identifier of the embedding model (defaults to 'all-MiniLM-L6-v2').
    /// </summary>
    public string ModelId { get; set; } = "all-MiniLM-L6-v2";

    /// <summary>
    /// Vector dimensions produced by the model (defaults to 384).
    /// </summary>
    public int Dimensions { get; set; } = 384;

    /// <summary>
    /// Path to the ONNX model file on disk.
    /// </summary>
    public string ModelPath { get; set; } = "models/all-MiniLM-L6-v2.onnx";
}

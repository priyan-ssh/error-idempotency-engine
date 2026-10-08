namespace IdempotencyEngine.Domain.Interfaces;

/// <summary>
/// Contract for generating embeddings for text data, typically used in machine learning and natural language processing tasks.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Gets the name of the embedding model being used.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the dimensionality of the embeddings produced by the model.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Generates an embedding vector for the given text input asynchronously.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Embedded Vector</returns>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for the given text inputs asynchronously.
    /// </summary>
    /// <param name="texts"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Embedded Vectors</returns>
    Task<float[][]> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
}

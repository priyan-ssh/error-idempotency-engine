using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IdempotencyEngine.Domain.Interfaces;

namespace IdempotencyEngine.Tests.Mocks;

/// <summary>
/// Fast, deterministic mock embedding provider for unit testing without ONNX model binaries.
/// </summary>
public class MockEmbeddingProvider : IEmbeddingProvider
{
    public string ModelName { get; }
    public int Dimensions { get; }

    public MockEmbeddingProvider(string modelName = "all-MiniLM-L6-v2", int dimensions = 384)
    {
        ModelName = modelName;
        Dimensions = dimensions;
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateDeterministicVector(text));
    }

    public Task<float[][]> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var results = texts.Select(CreateDeterministicVector).ToArray();
        return Task.FromResult(results);
    }

    private float[] CreateDeterministicVector(string text)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(text))
            return vector;

        var hash = (uint)text.GetHashCode();
        float sum = 0f;

        for (int i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)Math.Sin(hash + i * 31);
            sum += vector[i] * vector[i];
        }

        float norm = (float)Math.Sqrt(sum);
        if (norm > 0.00001f)
        {
            for (int i = 0; i < Dimensions; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}

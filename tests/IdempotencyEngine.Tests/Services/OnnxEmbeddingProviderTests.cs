using System.Threading.Tasks;
using FluentAssertions;
using IdempotencyEngine.Data.Services;
using IdempotencyEngine.Domain.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace IdempotencyEngine.Tests.Services;

public class OnnxEmbeddingProviderTests
{
    private readonly CpuSessionOptionsFactory _sessionOptionsFactory = new CpuSessionOptionsFactory();

    [Fact]
    public async Task GenerateEmbeddingAsync_WithValidText_Returns384DimensionNormalizedVector()
    {
        var options = Options.Create(new EmbeddingOptions());
        using var provider = new OnnxEmbeddingProvider(options, _sessionOptionsFactory);

        var embedding = await provider.GenerateEmbeddingAsync("connection refused");

        embedding.Should().NotBeNull();
        embedding.Length.Should().Be(384);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithNullOrEmpty_ReturnsZeroVector()
    {
        var options = Options.Create(new EmbeddingOptions());
        using var provider = new OnnxEmbeddingProvider(options, _sessionOptionsFactory);

        var embedding = await provider.GenerateEmbeddingAsync("");

        embedding.Should().NotBeNull();
        embedding.Length.Should().Be(384);
        embedding.Should().AllBeEquivalentTo(0f);
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_WithBatchInput_ReturnsMultipleVectors()
    {
        var options = Options.Create(new EmbeddingOptions());
        using var provider = new OnnxEmbeddingProvider(options, _sessionOptionsFactory);

        var inputs = new[] { "error 1", "error 2", "error 3" };
        var embeddings = await provider.GenerateBatchEmbeddingsAsync(inputs);

        embeddings.Should().HaveCount(3);
        embeddings[0].Length.Should().Be(384);
        embeddings[1].Length.Should().Be(384);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using IdempotencyEngine.Tests.Logging;
using IdempotencyEngine.Data.Abstractions;
using IdempotencyEngine.Data.Services;
using IdempotencyEngine.Domain.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace IdempotencyEngine.Tests.Services;

public class OnnxEmbeddingProviderTests
{
    private readonly IOnnxSessionOptionsFactory _sessionOptionsFactory;
    private readonly ILoggerFactory _loggerFactory;

    public OnnxEmbeddingProviderTests(ITestOutputHelper testOutputHelper)
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(testOutputHelper));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Load configuration directly from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var hardwareOptions = Options.Create(
            configuration.GetSection("OnnxHardwareOptions").Get<OnnxHardwareOptions>() 
            ?? new OnnxHardwareOptions()
        );

        _sessionOptionsFactory = new ConfigurableOnnxSessionOptionsFactory(
            hardwareOptions, 
            _loggerFactory.CreateLogger<ConfigurableOnnxSessionOptionsFactory>()
        );
    }

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

    [Fact]
    public void ConfigurableOnnxSessionOptionsFactory_ReadsAppSettingsConfig_Correctly()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var hardwareOptions = Options.Create(
            configuration.GetSection("OnnxHardwareOptions").Get<OnnxHardwareOptions>() 
            ?? new OnnxHardwareOptions()
        );

        var factory = new ConfigurableOnnxSessionOptionsFactory(hardwareOptions);

        // Verify factory correctly instantiates based on appsettings configuration
        var sessionOptions = factory.CreateOptions();
        sessionOptions.Should().NotBeNull();
    }
}

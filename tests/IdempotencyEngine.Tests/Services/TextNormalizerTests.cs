using System;
using Xunit;
using FluentAssertions;
using IdempotencyEngine.Domain.Constants;
using IdempotencyEngine.Domain.Services;

namespace IdempotencyEngine.Tests.Services;

public class TextNormalizerTests
{
    [Fact]
    public void Normalize_WithNullOrWhitespaceText_ThrowsArgumentException()
    {
        // Arrange

        // Act
        Action act = () => new TextNormalizer().Normalize("");
        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage($"{ValidationMessages.TextCannotBeNullOrWhitespace}*");
    }

    [Fact]
    public void Normalize_ShouldLowercaseAndTrimWhitespace()
    {
        // Arrange
        string input = "   Hello World!   ";
        // Act
        string result = new TextNormalizer().Normalize(input);
        // Assert
        result.Should().Be("hello world!");
    }

    [Fact]
    public void Normalize_ShouldReplaceTimestampsWithPlaceholder()
    {
        // Arrange
        string input = "2023-10-01T12:00:00Z";
        // Act
        string result = new TextNormalizer().Normalize(input);
        // Assert
        result.Should().Be("<TIMESTAMP>");
    }

    [Fact]
    public void Normalize_ShouldReplaceIPAddressesWithPlaceholder()
    {
        // Arrange
        string input = "User connected from 192.168.1.1";
        // Act
        string result = new TextNormalizer().Normalize(input);
        // Assert
        result.Should().Be("user connected from <IP_ADDRESS>");
    }

    [Fact]
    public void Normalize_ShouldReplaceUUIDsWithPlaceholder()
    {
        // Arrange
        string input = "User ID: 123e4567-e89b-12d3-a456-426614174000";
        // Act
        string result = new TextNormalizer().Normalize(input);
        // Assert
        result.Should().Be("user id: <UUID>");
    }

    [Fact]
    public void Normalize_ShouldReplaceProcessIdsAndNumbersWithPlaceholder()
    {
        // Arrange
        string input = "Process ID: 12345";
        // Act
        string result = new TextNormalizer().Normalize(input);
        // Assert
        result.Should().Be("process id: <PROCESS_ID>");
    }

    [Fact]
    public void Normalize_ShouldReplaceMemoryAddressesWithPlaceholder()
    {
        // Arrange
        string input = "Memory address: 0x7ffdfc3a";
        // Act
        string result = new TextNormalizer().Normalize(input);
        // Assert
        result.Should().Be("memory address: <MEMORY_ADDRESS>");
    }

    [Fact]
    public void ComputeShapeHash_Returns64CharLowercaseHex()
    {
        // Arrange
        var normalizer = new TextNormalizer(new Sha256Hasher());
        string normalized = normalizer.Normalize("user connected from 192.168.1.1");

        // Act
        string hash = normalizer.ComputeShapeHash(normalized);

        // Assert
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}

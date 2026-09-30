using System;
using FluentAssertions;
using IdempotencyEngine.Domain.Constants;
using IdempotencyEngine.Domain.Services;
using Xunit;

namespace IdempotencyEngine.Tests.Services;

public class Sha256HasherTests
{
    private readonly Sha256Hasher _hasher;

    public Sha256HasherTests()
    {
        _hasher = new Sha256Hasher();
    }

    [Fact]
    public void ComputeSha256_WithNullOrWhitespaceInput_ThrowsArgumentException()
    {
        Action act = () => _hasher.ComputeSha256("   ");
        act.Should().Throw<ArgumentException>()
           .WithMessage($"{ValidationMessages.TextCannotBeNullOrWhitespace}*");
    }

    [Fact]
    public void ComputeSha256_WithValidInput_Returns64CharacterHexSHA256()
    {
        var input = "hello world";
        var hash = _hasher.ComputeSha256(input);

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void ComputeSha256_WithIdenticalInput_ReturnsSameHash()
    {
        var input = "test_string";
        var hash1 = _hasher.ComputeSha256(input);
        var hash2 = _hasher.ComputeSha256(input);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeSha256_WithDifferentInput_ReturnsDifferentHash()
    {
        var hash1 = _hasher.ComputeSha256("input_a");
        var hash2 = _hasher.ComputeSha256("input_b");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeSha256_WithKnownValue_MatchesExpectedSHA256()
    {
        // Known SHA-256 for "{}" is "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"
        var input = "{}";
        var expectedHash = "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a";

        var actualHash = _hasher.ComputeSha256(input);

        actualHash.Should().Be(expectedHash);
    }
}

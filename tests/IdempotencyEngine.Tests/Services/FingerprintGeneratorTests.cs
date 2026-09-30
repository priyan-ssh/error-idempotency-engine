using System;
using System.Collections.Generic;
using FluentAssertions;
using IdempotencyEngine.Domain.Services;
using Xunit;

namespace IdempotencyEngine.Tests.Services;

public class FingerprintGeneratorTests
{
    [Fact]
    public void ComputeContextFingerprint_WithNullContext_ReturnsEmptyContextHash()
    {
        var generator = new FingerprintGenerator();
        var emptyHash = new Sha256Hasher().ComputeSha256("{}");

        var result = generator.ComputeContextFingerprint(null);

        result.Should().Be(emptyHash);
    }

    [Fact]
    public void ComputeContextFingerprint_WithEmptyContext_ReturnsEmptyContextHash()
    {
        var generator = new FingerprintGenerator();
        var emptyHash = new Sha256Hasher().ComputeSha256("{}");

        var result = generator.ComputeContextFingerprint(new Dictionary<string, object>());

        result.Should().Be(emptyHash);
    }

    [Fact]
    public void ComputeContextFingerprint_WithDifferentKeyOrdering_ReturnsIdenticalHash()
    {
        var generator = new FingerprintGenerator();
        var context1 = new Dictionary<string, object>
        {
            { "b", 2 },
            { "a", 1 }
        };
        var context2 = new Dictionary<string, object>
        {
            { "a", 1 },
            { "b", 2 }
        };

        var hash1 = generator.ComputeContextFingerprint(context1);
        var hash2 = generator.ComputeContextFingerprint(context2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContextFingerprint_WithDifferentValues_ReturnsDifferentHash()
    {
        var generator = new FingerprintGenerator();
        var context1 = new Dictionary<string, object> { { "a", 1 } };
        var context2 = new Dictionary<string, object> { { "a", 2 } };

        var hash1 = generator.ComputeContextFingerprint(context1);
        var hash2 = generator.ComputeContextFingerprint(context2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeContextFingerprint_WithNestedDictionaries_SortsKeysRecursively()
    {
        var generator = new FingerprintGenerator();
        var nested1 = new Dictionary<string, object>
        {
            { "z", new Dictionary<string, object> { { "b", 2 }, { "a", 1 } } },
            { "a", "test" }
        };
        var nested2 = new Dictionary<string, object>
        {
            { "a", "test" },
            { "z", new Dictionary<string, object> { { "a", 1 }, { "b", 2 } } }
        };

        var hash1 = generator.ComputeContextFingerprint(nested1);
        var hash2 = generator.ComputeContextFingerprint(nested2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContextFingerprint_WithMixedPrimitiveTypes_ProducesDeterministicHash()
    {
        var generator = new FingerprintGenerator();
        var context = new Dictionary<string, object>
        {
            { "str", "val" },
            { "num", 42 },
            { "bool", true },
            { "double", 3.14 }
        };

        var hash1 = generator.ComputeContextFingerprint(context);
        var hash2 = generator.ComputeContextFingerprint(context);

        hash1.Should().NotBeNullOrEmpty();
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeContextFingerprint_WithJsonElementContext_ProducesSameHashAsDictionary()
    {
        var generator = new FingerprintGenerator();

        var dictContext = new Dictionary<string, object>
        {
            { "a", "value1" },
            { "b", 42 },
            { "c", true },
            { "d", new Dictionary<string, object> { { "nestedKey", "nestedVal" } } },
            { "e", new List<object> { 1, "two", false } }
        };

        var jsonText = System.Text.Json.JsonSerializer.Serialize(dictContext);
        using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
        var jsonElementContext = new Dictionary<string, object>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            jsonElementContext[prop.Name] = prop.Value.Clone();
        }

        var hashFromDict = generator.ComputeContextFingerprint(dictContext);
        var hashFromJsonElement = generator.ComputeContextFingerprint(jsonElementContext);

        hashFromJsonElement.Should().Be(hashFromDict);
    }
}

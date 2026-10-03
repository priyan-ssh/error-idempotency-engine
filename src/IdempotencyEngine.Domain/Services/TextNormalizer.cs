using System.Text.RegularExpressions;
using IdempotencyEngine.Domain.Constants;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdempotencyEngine.Domain.Services;

/// <summary>
/// A service that normalizes text by applying a series of transformations to produce a standardized template and computes a deterministic SHA256 hash of the normalized text.
/// </summary>
public class TextNormalizer : ITextNormalizer
{
    private readonly IHasher _hasher;
    private readonly ILogger<TextNormalizer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextNormalizer"/> class.
    /// </summary>
    /// <param name="hasher">The hasher instance to use. If null, a default <see cref="Sha256Hasher"/> instance is used.</param>
    /// <param name="logger">The logger instance for logging normalization progress and errors.</param>
    public TextNormalizer(IHasher? hasher = null, ILogger<TextNormalizer>? logger = null)
    {
        _hasher = hasher ?? new Sha256Hasher();
        _logger = logger ?? NullLogger<TextNormalizer>.Instance;
    }

    /// <summary>
    /// Normalizes the input text by trimming whitespace, converting to lowercase, and applying canonicalization rules to replace dynamic data with placeholders.
    /// Output - shape_template/anchor
    /// </summary>
    /// <param name="text">The raw error text to normalize.</param>
    /// <returns>The normalized shape template string with dynamic placeholders.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public string Normalize(string text)
    {
        var normalizedText = text.Trim().ToLowerInvariant();
        
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new ArgumentException(ValidationMessages.TextCannotBeNullOrWhitespace, nameof(text));
        }
        
        normalizedText = Canonicalize(normalizedText);
        return normalizedText;
    }

    /// <summary>
    /// Computes a deterministic SHA256 hash of the normalized text returning a 64-character lowercase hex string.
    /// </summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>A 64-character lowercase hexadecimal shape hash string.</returns>
    public string ComputeShapeHash(string text)
    {
        return _hasher.ComputeSha256(text);
    }

    /// <summary>
    /// Replaces dynamic tokens (memory addresses, timestamps, UUIDs, IP addresses, standalone process numbers) with standardized placeholders.
    /// </summary>
    /// <param name="text">The lowercased and trimmed error text to canonicalize.</param>
    /// <returns>The canonicalized text containing placeholder tokens.</returns>
    private string Canonicalize(string text)
    {
        // 1. Memory addresses (0x[0-9a-fA-F]+) -> <MEMORY_ADDRESS>
        text = Regex.Replace(text, @"0x[0-9a-fA-F]+", "<MEMORY_ADDRESS>");

        // 2. Timestamps / ISO dates -> <TIMESTAMP>
        text = Regex.Replace(text, @"\b\d{4}-\d{2}-\d{2}(?:t\d{2}:\d{2}:\d{2}(?:\.\d+)?z)?\b", "<TIMESTAMP>");
        text = Regex.Replace(text, @"\b\d{2}:\d{2}:\d{2}\b", "<TIMESTAMP>");
        text = Regex.Replace(text, @"\b\d{1,2}/\d{1,2}/\d{2,4}\b", "<TIMESTAMP>");

        // 3. UUIDs ([0-9a-fA-F]{8}-[0-9a-fA-F]{4}...) -> <UUID>
        text = Regex.Replace(text, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", "<UUID>");

        // 4. IP addresses (\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b) -> <IP_ADDRESS>
        text = Regex.Replace(text, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", "<IP_ADDRESS>");

        // 5. Process IDs / Standalone numbers (\b\d+\b) -> <PROCESS_ID>
        text = Regex.Replace(text, @"\b\d+\b", "<PROCESS_ID>");

        return text;
    }
}

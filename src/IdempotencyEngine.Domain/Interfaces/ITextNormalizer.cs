using System;

namespace IdempotencyEngine.Domain.Interfaces;

public interface ITextNormalizer
{
    /// <summary>
    /// Converts raw error text into a standard template (shape_template) and calculates a deterministic SHA256 shape_hash.
    /// </summary>
    /// <param name="text">The raw error text to normalize.</param>
    /// <returns>The normalized shape template string with dynamic values replaced by placeholders.</returns>
    string Normalize(string text);

    /// <summary>
    /// Computes a deterministic SHA256 hash of the normalized text returning a 64-character lowercase hex string.
    /// </summary>
    /// <param name="text">The normalized text to compute a shape hash for.</param>
    /// <returns>A 64-character lowercase hexadecimal shape hash string.</returns>
    string ComputeShapeHash(string text);
}

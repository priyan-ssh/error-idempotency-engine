using System.Security.Cryptography;
using System.Text;
using IdempotencyEngine.Domain.Constants;
using IdempotencyEngine.Domain.Interfaces;

namespace IdempotencyEngine.Domain.Services;

/// <summary>
/// A service that computes SHA256 hashes for given input strings.
/// </summary>
public class Sha256Hasher : IHasher
{
    /// <summary>
    /// Computes a SHA256 hash for the given input string and returns it as a lowercase hexadecimal string.
    /// Output - fingerprint hash
    /// </summary>
    /// <param name="input">The input string to hash.</param>
    /// <returns>A lowercase hexadecimal SHA256 hash string.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="input"/> is null or whitespace.</exception>
    public string ComputeSha256(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException(ValidationMessages.TextCannotBeNullOrWhitespace, nameof(input));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}

namespace IdempotencyEngine.Domain.Interfaces;

public interface IHasher
{
    /// <summary>
    /// Computes a SHA256 hash for the given input string and returns it as a lowercase hexadecimal string.
    /// </summary>
    /// <param name="input">The input string to hash.</param>
    /// <returns>A lowercase hexadecimal SHA256 hash string.</returns>
    string ComputeSha256(string input);
}

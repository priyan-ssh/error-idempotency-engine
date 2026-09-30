using System.Collections.Generic;

namespace IdempotencyEngine.Domain.Interfaces;

public interface IFingerprintGenerator
{
    /// <summary>
    /// Computes a deterministic fingerprint for the given context represented as a dictionary of key-value pairs.
    /// </summary>
    /// <param name="context">The context dictionary containing key-value pairs to fingerprint.</param>
    /// <returns>A lowercase hexadecimal SHA256 fingerprint hash of the context dictionary.</returns>
    string ComputeContextFingerprint(IDictionary<string, object>? context);
}

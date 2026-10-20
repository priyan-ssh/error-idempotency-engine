namespace IdempotencyEngine.Domain.Models;

/// <summary>
/// Helper for building 64-bit advisory lock keys.
/// </summary>
public static class AdvisoryLockKey
{
    /// <summary>
    /// Formats the deterministic string kind:domain:shape_hash:shape_version
    /// passed to hashtextextended(..., 0).
    /// </summary>
    public static string Format(string kind, string domain, string shapeHash, string shapeVersion)
    {
        return $"{kind}:{domain}:{shapeHash}:{shapeVersion}";
    }
}

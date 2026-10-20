namespace IdempotencyEngine.Domain.Exceptions;

/// <summary>
/// Thrown when an advisory lock cannot be acquired within the configured lock budget.
/// Maps to HTTP 503 Service Unavailable with Retry-After header.
/// </summary>
public class AdvisoryLockTimeoutException : Exception
{
    public string LockKey { get; }
    public int RetryAfterSeconds { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AdvisoryLockTimeoutException"/> class with the specified lock key, retry-after duration, and optional message.
    /// </summary>
    /// <param name="lockKey"></param>
    /// <param name="retryAfterSeconds"></param>
    /// <param name="message"></param>
    public AdvisoryLockTimeoutException(string lockKey, int retryAfterSeconds = 1, string? message = null)
        : base(message ?? $"Failed to acquire advisory lock for key '{lockKey}' within timeout budget.")
    {
        LockKey = lockKey;
        RetryAfterSeconds = retryAfterSeconds;
    }
}

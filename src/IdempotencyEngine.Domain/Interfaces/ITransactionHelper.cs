namespace IdempotencyEngine.Domain.Interfaces;

/// <summary>
/// Infrastructure contract for executing operations within a transaction context.
/// </summary>
public interface ITransactionHelper
{
    /// <summary>
    /// Executes a callback action inside a database transaction with explicit retry/timeout handling,
    /// providing a pure domain-level transaction context (Unit of Work).
    /// </summary>
    /// <typeparam name="T">The return type of the transactional action.</typeparam>
    /// <param name="action">The action to execute taking the transaction context.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The result of the action.</returns>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<IIngestionTransactionContext, Task<T>> action,
        CancellationToken ct = default);

    /// <summary>
    /// Fires a post-commit NOTIFY anchor_created notification.
    /// </summary>
    /// <param name="anchorId">The newly created anchor ID.</param>
    /// <param name="ct">A cancellation token.</param>
    Task NotifyAnchorCreatedAsync(
        Guid anchorId,
        CancellationToken ct = default);
}

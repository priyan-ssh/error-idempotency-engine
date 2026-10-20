using System.Data;
using IdempotencyEngine.Domain.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace IdempotencyEngine.Data.Services.Ingest;

/// <summary>
/// Implements database transaction management and post-commit notifications for ingestion.
/// </summary>
public class PostgresTransactionManager : ITransactionHelper
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresTransactionManager> _logger;

    public PostgresTransactionManager(string connectionString, ILogger<PostgresTransactionManager>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? NullLogger<PostgresTransactionManager>.Instance;
    }

    /// <summary>
    /// Executes an action within a database transaction.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="action"></param>
    /// <param name="ct"></param>
    /// <returns>Result of the action</returns>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<IIngestionTransactionContext, Task<T>> action,
        CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);

        try
        {
            var ctx = new PostgresIngestionContext(conn, tx);
            var result = await action(ctx).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Sends a PostgreSQL NOTIFY event to inform listeners that a new incident anchor has been created.
    /// </summary>
    /// <param name="anchorId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task NotifyAnchorCreatedAsync(Guid anchorId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        const string sql = "SELECT pg_notify('anchor_created', @AnchorId);";
        var cmd = new CommandDefinition(sql, new { AnchorId = anchorId.ToString() }, commandTimeout: 5, cancellationToken: ct);
        await conn.ExecuteAsync(cmd).ConfigureAwait(false);
    }
}

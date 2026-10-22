using IdempotencyEngine.Data.DbContexts;
using IdempotencyEngine.Domain.Dtos;
using IdempotencyEngine.Domain.Exceptions;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IdempotencyEngine.Api.Controllers;

/// <summary>
/// Controller for ingesting error events and batches via HTTP.
/// Supports asynchronous watcher ingest (202 Accepted) and synchronous client ingest (200 OK).
/// Translates advisory lock timeout exceptions to 503 Service Unavailable with Retry-After header.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IngestController : ControllerBase
{
    private readonly IIngestionService _ingestionService;
    private readonly ILogger<IngestController> _logger;
    private readonly IdempotencyDbContext _dbContext;

    public IngestController(IIngestionService ingestionService, ILogger<IngestController> logger, IdempotencyDbContext dbContext)
    {
        _ingestionService = ingestionService;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Ingests a batch of error events asynchronously (returns 202 Accepted).
    /// Used primarily by the background go-journalctl-watcher binary.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IngestAsync([FromBody] IngestBatchRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _ingestionService.IngestBatchAsync(request, ct).ConfigureAwait(false);
            return Accepted(result);
        }
        catch (AdvisoryLockTimeoutException ex)
        {
            _logger.LogWarning(ex, "Advisory lock timeout during ingest batch for key {LockKey}", ex.LockKey);
            Response.Headers.Append("Retry-After", ex.RetryAfterSeconds.ToString());
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (PostgresException ex) when (ex.SqlState == "23503" || ex.SqlState == "23514" || ex.MessageText.Contains("constraint", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Database constraint violation during async ingest: {Message}", ex.Message);
            var validDomains = await _dbContext.Domains.Select(d => d.Name).ToListAsync(ct).ConfigureAwait(false);
            var validKinds = new[] { "error", "problem", "decision" };
            return UnprocessableEntity(new
            {
                error = "Unprocessable Entity due to database constraint violation (invalid domain or kind)",
                detail = ex.Message,
                valid_domains = validDomains,
                valid_kinds = validKinds
            });
        }
        catch (Exception ex) when (ex.InnerException is PostgresException pgEx && (pgEx.SqlState == "23503" || pgEx.SqlState == "23514"))
        {
            _logger.LogWarning(ex, "Database constraint violation inner exception during async ingest: {Message}", ex.Message);
            var validDomains = await _dbContext.Domains.Select(d => d.Name).ToListAsync(ct).ConfigureAwait(false);
            var validKinds = new[] { "error", "problem", "decision" };
            return UnprocessableEntity(new
            {
                error = "Unprocessable Entity due to database constraint violation (invalid domain or kind)",
                detail = pgEx.Message,
                valid_domains = validDomains,
                valid_kinds = validKinds
            });
        }
    }

    /// <summary>
    /// Ingests a batch of error events synchronously (returns 200 OK with full IngestResult).
    /// Used by MCP tools, CLI clients, and synchronous callers.
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IngestResult>> IngestBatchAsync([FromBody] IngestBatchRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _ingestionService.IngestBatchAsync(request, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (AdvisoryLockTimeoutException ex)
        {
            _logger.LogWarning(ex, "Advisory lock timeout during sync ingest batch for key {LockKey}", ex.LockKey);
            Response.Headers.Append("Retry-After", ex.RetryAfterSeconds.ToString());
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (PostgresException ex) when (ex.SqlState == "23503" || ex.SqlState == "23514" || ex.MessageText.Contains("constraint", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Database constraint violation during sync ingest: {Message}", ex.Message);
            var validDomains = await _dbContext.Domains.Select(d => d.Name).ToListAsync(ct).ConfigureAwait(false);
            var validKinds = new[] { "error", "problem", "decision" };
            return UnprocessableEntity(new
            {
                error = "Unprocessable Entity due to database constraint violation (invalid domain or kind)",
                detail = ex.Message,
                valid_domains = validDomains,
                valid_kinds = validKinds
            });
        }
        catch (Exception ex) when (ex.InnerException is PostgresException pgEx && (pgEx.SqlState == "23503" || pgEx.SqlState == "23514"))
        {
            _logger.LogWarning(ex, "Database constraint violation inner exception during sync ingest: {Message}", ex.Message);
            var validDomains = await _dbContext.Domains.Select(d => d.Name).ToListAsync(ct).ConfigureAwait(false);
            var validKinds = new[] { "error", "problem", "decision" };
            return UnprocessableEntity(new
            {
                error = "Unprocessable Entity due to database constraint violation (invalid domain or kind)",
                detail = pgEx.Message,
                valid_domains = validDomains,
                valid_kinds = validKinds
            });
        }
    }
}

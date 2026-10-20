using IdempotencyEngine.Domain.Dtos;
using IdempotencyEngine.Domain.Exceptions;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

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

    public IngestController(IIngestionService ingestionService, ILogger<IngestController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>
    /// Ingests a batch of error events asynchronously (returns 202 Accepted).
    /// Used primarily by the background go-journalctl-watcher binary.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
    }

    /// <summary>
    /// Ingests a batch of error events synchronously (returns 200 OK with full IngestResult).
    /// Used by MCP tools, CLI clients, and synchronous callers.
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
    }
}

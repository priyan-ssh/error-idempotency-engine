using System.Net;
using System.Text.Json;
using IdempotencyEngine.Domain.Exceptions;

namespace IdempotencyEngine.Api.Middleware;

/// <summary>
/// Global exception handling middleware adhering to RFC 7807 (Problem Details).
/// Translates unhandled exceptions and domain exceptions (such as AdvisoryLockTimeoutException)
/// into standardized HTTP problem responses.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware to handle exceptions during HTTP request processing.
    /// </summary>
    /// <param name="context"></param>
    /// <returns>Result of the middleware invocation</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (AdvisoryLockTimeoutException ex)
        {
            _logger.LogWarning(ex, "Advisory lock timeout during request handling for key {LockKey}", ex.LockKey);
            
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers.Append("Retry-After", ex.RetryAfterSeconds.ToString());

            var problem = new
            {
                type = "https://tools.ietf.org/html/rfc7807#section-3.1",
                title = "Service Unavailable",
                status = StatusCodes.Status503ServiceUnavailable,
                detail = ex.Message,
                instance = context.Request.Path
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred while processing request {Path}", context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new
            {
                type = "https://tools.ietf.org/html/rfc7807#section-3.1",
                title = "Internal Server Error",
                status = (int)HttpStatusCode.InternalServerError,
                detail = ex.Message,
                instance = context.Request.Path
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem)).ConfigureAwait(false);
        }
    }
}

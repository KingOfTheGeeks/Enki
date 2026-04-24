namespace SDI.Enki.WebApi.Infrastructure;

/// <summary>
/// Honors or mints an <c>X-Request-Id</c> header for every request,
/// overwrites <see cref="HttpContext.TraceIdentifier"/> with it so
/// every downstream log line + the ProblemDetails <c>traceId</c> extension
/// carry the same value, and echoes it back on the response so the
/// browser / caller can see it.
///
/// Use case: user reports "I got an error", we ask them to paste the
/// <c>X-Request-Id</c> from the response, we grep logs, we have the full
/// picture in one line without server-side spelunking.
/// </summary>
public sealed class RequestCorrelationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext ctx, ILogger<RequestCorrelationMiddleware> logger)
    {
        var inbound = ctx.Request.Headers[HeaderName].ToString();
        var id = string.IsNullOrWhiteSpace(inbound)
            ? Guid.NewGuid().ToString("N")
            : inbound.Trim();

        ctx.TraceIdentifier = id;
        ctx.Response.Headers[HeaderName] = id;

        // Push RequestId + any user identity we have into the log scope so
        // everything emitted during this request is auto-enriched. A null
        // user (unauthenticated / preflight) still has a RequestId.
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = id,
            ["UserId"]    = ctx.User?.FindFirst("sub")?.Value,
            ["Path"]      = ctx.Request.Path.Value,
        });

        await next(ctx);
    }
}

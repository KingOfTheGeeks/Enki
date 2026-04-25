namespace SDI.Enki.WebApi.Infrastructure;

/// <summary>
/// Honors or mints an <c>X-Request-Id</c> header for every request,
/// overwrites <see cref="HttpContext.TraceIdentifier"/> with it so
/// every downstream log line + the ProblemDetails <c>traceId</c>
/// extension carry the same value, and echoes it back on the response
/// so the browser / caller can see it.
///
/// <para>
/// Use case: user reports "I got an error", we ask them to paste the
/// <c>X-Request-Id</c> from the response, we grep logs, we have the
/// full picture in one line without server-side spelunking.
/// </para>
///
/// <para>
/// Sits early in the pipeline (before <c>UseAuthentication</c>) so
/// 401 responses also carry an id. The user-identity half of the log
/// scope is opened later by <see cref="UserScopeMiddleware"/> — once
/// the principal has been populated by the auth middleware.
/// </para>
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

        // Push RequestId + Path into the log scope. UserId can't be
        // resolved here yet — UseAuthentication runs later — so it
        // gets its own middleware downstream.
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = id,
            ["Path"]      = ctx.Request.Path.Value,
        });

        await next(ctx);
    }
}

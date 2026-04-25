namespace SDI.Enki.WebApi.Infrastructure;

/// <summary>
/// Pushes the authenticated user's <c>sub</c> claim into the log scope
/// so every log line for the rest of the request is enriched with
/// <c>UserId</c>. Sits after <c>UseAuthentication</c>; before that point
/// <see cref="HttpContext.User"/> is the unauthenticated default and
/// resolving the claim is meaningless.
///
/// <para>
/// Anonymous endpoints still pass through cleanly — the scope is opened
/// with <c>UserId = null</c> rather than skipped, so log queries
/// filtering on RequestId always find a complete row even for 401s.
/// </para>
/// </summary>
public sealed class UserScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ILogger<UserScopeMiddleware> logger)
    {
        var sub = ctx.User?.FindFirst("sub")?.Value;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = sub,
        });

        await next(ctx);
    }
}

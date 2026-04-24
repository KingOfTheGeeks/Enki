using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.WebApi.Infrastructure;

/// <summary>
/// <see cref="ICurrentUser"/> backed by the WebApi's incoming request
/// principal. Reads the standard OIDC <c>sub</c> and <c>name</c> claims.
/// When HttpContext is null (rare — only for non-request scopes like
/// background tasks), returns null so the audit interceptor falls back
/// to "system".
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor ctx) : ICurrentUser
{
    public string? UserId => ctx.HttpContext?.User?.FindFirst("sub")?.Value;

    public string? UserName =>
        ctx.HttpContext?.User?.FindFirst("name")?.Value
        ?? ctx.HttpContext?.User?.FindFirst("preferred_username")?.Value
        ?? UserId;
}

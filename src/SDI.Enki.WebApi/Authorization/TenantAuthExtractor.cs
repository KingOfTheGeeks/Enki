using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// Shared preconditions for tenant-scoped authorization handlers.
/// Resolves the principal's <c>sub</c> claim into an <see cref="IdentityId"/>
/// and reads <c>{tenantCode}</c> from the route. The pair is what every
/// tenant-scoped policy needs before it can run its specific membership
/// check; pulling the extraction out of the handlers means the
/// individual handlers shrink to their actual decision logic.
///
/// <para>
/// Logging is the caller's responsibility — they pass an
/// <see cref="ILogger"/> + a short handler name so denial reasons get
/// attributed to the right handler in the log stream.
/// </para>
/// </summary>
public readonly record struct TenantAuthContext(Guid IdentityId, string TenantCode);

public static class TenantAuthExtractor
{
    /// <summary>
    /// Returns true with both fields populated on success, false on
    /// any failure (with a debug log line on the supplied logger).
    /// Failure modes (in order of check):
    /// <list type="bullet">
    ///   <item>No <c>sub</c> claim on the principal.</item>
    ///   <item><c>sub</c> isn't a valid Guid.</item>
    ///   <item>No <c>tenantCode</c> in the route values.</item>
    /// </list>
    /// </summary>
    public static bool TryExtract(
        AuthorizationHandlerContext context,
        ILogger logger,
        string handlerName,
        [NotNullWhen(true)] out TenantAuthContext? result)
    {
        result = null;

        var sub = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            logger.LogDebug("{Handler} denied: no sub claim on caller.", handlerName);
            return false;
        }

        if (!Guid.TryParse(sub, out var identityId))
        {
            logger.LogDebug("{Handler} denied: sub '{Sub}' is not a user GUID.", handlerName, sub);
            return false;
        }

        // Endpoint routing puts the HttpContext on AuthorizationHandlerContext.Resource
        // — no IHttpContextAccessor needed.
        if (context.Resource is not HttpContext http)
        {
            logger.LogDebug("{Handler} denied: AuthorizationHandlerContext.Resource is not an HttpContext.", handlerName);
            return false;
        }

        var tenantCode = http.Request.RouteValues["tenantCode"] as string;
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            logger.LogDebug("{Handler} denied: no tenantCode in route.", handlerName);
            return false;
        }

        result = new TenantAuthContext(identityId, tenantCode);
        return true;
    }
}

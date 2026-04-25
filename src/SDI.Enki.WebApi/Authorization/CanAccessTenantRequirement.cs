using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// Authorization requirement enforced on tenant-scoped endpoints
/// (anything with <c>{tenantCode}</c> in the route — Jobs, Wells, Runs,
/// and anything under them in later phases).
///
/// The caller's principal must satisfy one of:
/// <list type="bullet">
///   <item>A row exists in <c>TenantUser</c> linking the caller's
///   <c>sub</c> claim to the <c>{tenantCode}</c> resolved from the route.</item>
///   <item>The caller has the <c>enki-admin</c> role claim — SDI-side
///   admins who operate cross-tenant. (The role is seeded into the
///   token by Identity once we flip that on; for now this path is
///   documented and ready.)</item>
/// </list>
///
/// Apply with <c>[Authorize(Policy = EnkiPolicies.CanAccessTenant)]</c>.
/// The master-registry endpoints on <c>TenantsController</c> stay on
/// <c>EnkiApiScope</c>; this requirement is specifically for
/// per-tenant drill-down routes.
/// </summary>
public sealed class CanAccessTenantRequirement : IAuthorizationRequirement
{
}

public sealed class CanAccessTenantHandler(
    IHttpContextAccessor httpContextAccessor,
    AthenaMasterDbContext master,
    ILogger<CanAccessTenantHandler> logger) : AuthorizationHandler<CanAccessTenantRequirement>
{
    /// <summary>Re-export. Canonical home is <see cref="AuthConstants.EnkiAdminRole"/>.</summary>
    public const string AdminRole = AuthConstants.EnkiAdminRole;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanAccessTenantRequirement requirement)
    {
        // Hard gate: must be authenticated.
        var sub = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            logger.LogDebug("CanAccessTenant denied: no sub claim on caller.");
            return;
        }

        // Cross-tenant admin skips the membership check.
        if (context.User.IsInRole(AdminRole) ||
            context.User.HasClaim("role", AdminRole))
        {
            context.Succeed(requirement);
            return;
        }

        // Otherwise the route must carry {tenantCode} and the user must
        // be a member of that tenant.
        var routeValues = httpContextAccessor.HttpContext?.Request.RouteValues;
        var tenantCode = routeValues?["tenantCode"] as string;
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            logger.LogDebug("CanAccessTenant denied: no tenantCode in route.");
            return;
        }

        if (!Guid.TryParse(sub, out var identityId))
        {
            logger.LogDebug("CanAccessTenant denied: sub '{Sub}' is not a user GUID.", sub);
            return;
        }

        // sub is AspNetUsers.Id (the Identity row id). TenantUser.UserId
        // points at the master User.Id, not the Identity id — so resolve
        // the master row via User.IdentityId before checking membership.
        var member = await master.TenantUsers
            .AsNoTracking()
            .AnyAsync(tu => tu.User!.IdentityId == identityId
                         && tu.Tenant!.Code == tenantCode);

        if (member)
            context.Succeed(requirement);
        else
            logger.LogInformation(
                "CanAccessTenant denied: identity {IdentityId} is not a member of tenant {TenantCode}.",
                identityId, tenantCode);
    }
}

/// <summary>
/// Policy name constants — reference these rather than typing string
/// literals at each <c>[Authorize(Policy = "...")]</c> so typos fail at
/// compile time.
/// </summary>
public static class EnkiPolicies
{
    /// <summary>Any signed-in caller with the <c>enki</c> scope.</summary>
    public const string EnkiApiScope = "EnkiApiScope";

    /// <summary>Tenant-scoped; caller must be a TenantUser or an admin.</summary>
    public const string CanAccessTenant = "CanAccessTenant";

    /// <summary>
    /// Tighter than <see cref="CanAccessTenant"/>: caller must be a
    /// tenant Admin (TenantUserRole.Admin) or hold the system
    /// <c>enki-admin</c> role. Applied to membership-management endpoints.
    /// </summary>
    public const string CanManageTenantMembers = "CanManageTenantMembers";
}

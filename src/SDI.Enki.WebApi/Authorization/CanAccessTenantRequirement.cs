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
///   admins who operate cross-tenant.</item>
/// </list>
///
/// Apply with <c>[Authorize(Policy = EnkiPolicies.CanAccessTenant)]</c>.
/// The master-registry endpoints on <c>TenantsController</c> stay on
/// <c>EnkiApiScope</c>; this requirement is specifically for
/// per-tenant drill-down routes.
/// </summary>
public sealed class CanAccessTenantRequirement : IAuthorizationRequirement;

public sealed class CanAccessTenantHandler(
    AthenaMasterDbContext master,
    ILogger<CanAccessTenantHandler> logger) : AuthorizationHandler<CanAccessTenantRequirement>
{
    private const string Name = nameof(CanAccessTenantHandler);

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanAccessTenantRequirement requirement)
    {
        // Cross-tenant admin skips the membership check.
        if (context.User.HasEnkiAdminRole())
        {
            context.Succeed(requirement);
            return;
        }

        if (!TenantAuthExtractor.TryExtract(context, logger, Name, out var auth))
            return;

        // sub is AspNetUsers.Id (the Identity row id). TenantUser.UserId
        // points at the master User.Id, not the Identity id — so resolve
        // the master row via User.IdentityId before checking membership.
        var member = await master.TenantUsers
            .AsNoTracking()
            .AnyAsync(tu => tu.User!.IdentityId == auth.Value.IdentityId
                         && tu.Tenant!.Code == auth.Value.TenantCode);

        if (member)
            context.Succeed(requirement);
        else
            logger.LogInformation(
                "{Handler} denied: identity {IdentityId} is not a member of tenant {TenantCode}.",
                Name, auth.Value.IdentityId, auth.Value.TenantCode);
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

    /// <summary>
    /// System-admin only — must hold the <c>enki-admin</c> role plus
    /// the <c>enki</c> scope. Applied to cross-tenant administrative
    /// endpoints (system settings, future audit, etc.).
    /// </summary>
    public const string EnkiAdminOnly = "EnkiAdminOnly";
}

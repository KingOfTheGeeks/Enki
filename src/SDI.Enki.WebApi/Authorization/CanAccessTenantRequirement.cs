using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    EnkiMasterDbContext master,
    IMemoryCache cache,
    IAuthzDenialAuditor denialAuditor,
    ILogger<CanAccessTenantHandler> logger) : AuthorizationHandler<CanAccessTenantRequirement>
{
    private const string Name = nameof(CanAccessTenantHandler);

    /// <summary>
    /// How long a positive (or negative) membership decision stays in
    /// the cache before being re-queried. Short enough that a missed
    /// invalidation can't lock a user out (or in) for long; long enough
    /// to amortise the master-DB roundtrip across normal user flows
    /// where the same user hits the same tenant repeatedly.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Stable cache-key formatter. Exposed so mutation endpoints
    /// (`TenantMembersController` Add / Remove) can bust the entry
    /// for the affected (IdentityId, TenantCode) pair.
    /// </summary>
    public static string CacheKeyFor(Guid identityId, string tenantCode) =>
        $"enki.membership.{identityId}.{tenantCode}";

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

        // Tenant-type users carry a hard binding to exactly one tenant
        // on their access token (tenant_id claim). If the GUID resolves
        // to the same tenant as {tenantCode} on the route, they're in;
        // any other tenant is a 404 from the route's perspective (per
        // TenantRoutingMiddleware) but worth recording as a denial here
        // so the audit log shows the cross-tenant attempt.
        if (context.User.HasClaim(c => c.Type == AuthConstants.UserTypeClaim
                                    && c.Value == UserType.Tenant.Name))
        {
            await HandleTenantTypeUserAsync(context, requirement, auth.Value);
            return;
        }

        // sub is AspNetUsers.Id (the Identity row id). TenantUser.UserId
        // points at the master User.Id, not the Identity id — so resolve
        // the master row via User.IdentityId before checking membership.
        // Result cached for CacheDuration; busted by TenantMembersController
        // on Add / Remove so a fresh-grant or revoke takes effect on the
        // next request rather than waiting out the TTL.
        var key = CacheKeyFor(auth.Value.IdentityId, auth.Value.TenantCode);
        var member = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await master.TenantUsers
                .AsNoTracking()
                .AnyAsync(tu => tu.User!.IdentityId == auth.Value.IdentityId
                             && tu.Tenant!.Code == auth.Value.TenantCode);
        });

        if (member)
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogInformation(
                "{Handler} denied: identity {IdentityId} is not a member of tenant {TenantCode}.",
                Name, auth.Value.IdentityId, auth.Value.TenantCode);
            await denialAuditor.RecordAsync(
                policy:     EnkiPolicies.CanAccessTenant,
                tenantCode: auth.Value.TenantCode,
                actorSub:   auth.Value.IdentityId.ToString(),
                reason:     "NotAMember");
        }
    }

    /// <summary>
    /// Decision path for principals carrying <c>user_type=Tenant</c>:
    /// resolve the bound <c>tenant_id</c> claim to a tenant Code via
    /// the master registry (cached) and compare against the route's
    /// <c>{tenantCode}</c>. Match → succeed; mismatch (or missing
    /// claim, or stale tenant id) → deny.
    /// </summary>
    private async Task HandleTenantTypeUserAsync(
        AuthorizationHandlerContext context,
        CanAccessTenantRequirement requirement,
        TenantAuthContext auth)
    {
        var rawTenantId = context.User.FindFirst(AuthConstants.TenantIdClaim)?.Value;
        if (!Guid.TryParse(rawTenantId, out var boundTenantId) || boundTenantId == Guid.Empty)
        {
            logger.LogWarning(
                "{Handler} denied: Tenant-type principal {IdentityId} carries no valid tenant_id claim.",
                Name, auth.IdentityId);
            await denialAuditor.RecordAsync(
                policy:     EnkiPolicies.CanAccessTenant,
                tenantCode: auth.TenantCode,
                actorSub:   auth.IdentityId.ToString(),
                reason:     "TenantUserMissingTenantClaim");
            return;
        }

        var boundCodeKey = $"enki.tenant-id-to-code.{boundTenantId}";
        var boundCode = await cache.GetOrCreateAsync(boundCodeKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await master.Tenants
                .AsNoTracking()
                .Where(t => t.Id == boundTenantId)
                .Select(t => t.Code)
                .FirstOrDefaultAsync();
        });

        if (string.IsNullOrEmpty(boundCode))
        {
            logger.LogWarning(
                "{Handler} denied: Tenant-type principal {IdentityId} bound to unknown tenant {TenantId}.",
                Name, auth.IdentityId, boundTenantId);
            await denialAuditor.RecordAsync(
                policy:     EnkiPolicies.CanAccessTenant,
                tenantCode: auth.TenantCode,
                actorSub:   auth.IdentityId.ToString(),
                reason:     "TenantUserBoundToUnknownTenant");
            return;
        }

        if (string.Equals(boundCode, auth.TenantCode, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return;
        }

        logger.LogInformation(
            "{Handler} denied: Tenant-type principal {IdentityId} bound to {BoundCode} requested {RequestedCode}.",
            Name, auth.IdentityId, boundCode, auth.TenantCode);
        await denialAuditor.RecordAsync(
            policy:     EnkiPolicies.CanAccessTenant,
            tenantCode: auth.TenantCode,
            actorSub:   auth.IdentityId.ToString(),
            reason:     "TenantUserBoundToDifferentTenant");
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

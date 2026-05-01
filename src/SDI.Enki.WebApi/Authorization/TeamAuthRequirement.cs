using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// Single parametric authorization requirement that drives every
/// Enki authorization decision. Combines the four orthogonal axes
/// (admin bypass, Tenant-user segregation, tenant membership scope,
/// Team subtype + capability verb gate) into one decision tree
/// implemented in <see cref="TeamAuthHandler"/>.
///
/// <para>
/// One requirement type, one handler, twelve named policies layered
/// on top. The matrix is documented in
/// <c>docs/sop-authorization-redesign.md</c> (section E).
/// </para>
///
/// <para>
/// Parameters:
/// <list type="bullet">
///   <item><see cref="MinimumSubtype"/> — minimum
///   <see cref="TeamSubtype"/> required for Team users. Null = any
///   tenant member (Field-or-above effectively).</item>
///   <item><see cref="GrantingCapability"/> — capability claim value
///   that grants this regardless of subtype. OR-ed with the subtype
///   gate.</item>
///   <item><see cref="TenantScoped"/> — true for tenant-scoped
///   endpoints (route includes <c>{tenantCode}</c>). The handler
///   enforces tenant membership for Team users and tenant-binding
///   match for Tenant users.</item>
///   <item><see cref="RequireAdmin"/> — short-circuits to admin-only.
///   Used by <c>EnkiAdminOnly</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed record TeamAuthRequirement(
    TeamSubtype? MinimumSubtype     = null,
    string?      GrantingCapability = null,
    bool         TenantScoped       = false,
    bool         RequireAdmin       = false
) : IAuthorizationRequirement;

/// <summary>
/// One handler, one decision tree. First matching rule wins.
/// </summary>
/// <remarks>
/// Decision tree (mirrors SOP-002 section 5):
/// <list type="number">
///   <item>If <c>RequireAdmin</c> → succeed iff <c>IsEnkiAdmin</c>.</item>
///   <item>If <c>IsEnkiAdmin</c> → succeed (root bypass).</item>
///   <item>If <c>user_type = Tenant</c> → tenant-user clause:
///     deny on master ops; bind-check tenant code; succeed only if
///     no <c>MinimumSubtype</c> requested.</item>
///   <item>If <c>TenantScoped</c> → require TenantUser membership
///     (cached lookup, same shape as the legacy handler).</item>
///   <item>If <c>MinimumSubtype</c> is null → succeed.</item>
///   <item>If subtype ≥ minimum → succeed.</item>
///   <item>If capability matches <c>GrantingCapability</c> → succeed.</item>
///   <item>Deny.</item>
/// </list>
/// </remarks>
public sealed class TeamAuthHandler(
    EnkiMasterDbContext master,
    IMemoryCache cache,
    IAuthzDenialAuditor denialAuditor,
    ILogger<TeamAuthHandler> logger) : AuthorizationHandler<TeamAuthRequirement>
{
    private const string Name = nameof(TeamAuthHandler);

    /// <summary>How long a tenant-membership decision stays cached.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Stable cache key for membership decisions. Exposed so
    /// <c>TenantMembersController</c> can bust the entry on
    /// Add / Remove for the affected (IdentityId, TenantCode) pair.
    /// </summary>
    public static string MembershipCacheKey(Guid identityId, string tenantCode) =>
        $"enki.membership.{identityId}.{tenantCode}";

    /// <summary>
    /// Stable cache key for "tenant_id GUID → tenant Code" lookups
    /// used by the Tenant-user clause. Busted by tenant lifecycle
    /// changes (deactivate / archive) since the bound code may
    /// effectively retire even though the row stays.
    /// </summary>
    public static string TenantIdToCodeCacheKey(Guid tenantId) =>
        $"enki.tenant-id-to-code.{tenantId}";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TeamAuthRequirement requirement)
    {
        // 1. RequireAdmin short-circuit.
        if (requirement.RequireAdmin)
        {
            if (context.User.HasEnkiAdminRole())
            {
                context.Succeed(requirement);
            }
            else
            {
                await DenyAsync(context, requirement, "NotEnkiAdmin", tenantCode: null);
            }
            return;
        }

        // 2. Cross-tenant admin bypass.
        if (context.User.HasEnkiAdminRole())
        {
            context.Succeed(requirement);
            return;
        }

        // 3. Tenant-type principal (interim — see SOP-002 Section D.2).
        if (context.User.IsTenantTypeUser())
        {
            await HandleTenantTypeAsync(context, requirement);
            return;
        }

        // 4. Team-user path.
        if (requirement.TenantScoped)
        {
            if (!TenantAuthExtractor.TryExtract(context, logger, Name, out var auth))
                return;

            var member = await IsTenantMemberAsync(auth.Value.IdentityId, auth.Value.TenantCode);
            if (!member)
            {
                await DenyAsync(context, requirement, "NotAMember", auth.Value.TenantCode);
                return;
            }
        }

        // 5. No subtype gate → succeed.
        if (requirement.MinimumSubtype is null)
        {
            context.Succeed(requirement);
            return;
        }

        // 6. Subtype-or-capability gate.
        if (context.User.HasTeamSubtypeAtLeast(requirement.MinimumSubtype))
        {
            context.Succeed(requirement);
            return;
        }

        if (!string.IsNullOrEmpty(requirement.GrantingCapability)
            && context.User.HasCapability(requirement.GrantingCapability))
        {
            context.Succeed(requirement);
            return;
        }

        // 7. All gates exhausted.
        await DenyAsync(context, requirement, "InsufficientSubtypeOrCapability", tenantCode: null);
    }

    private async Task HandleTenantTypeAsync(
        AuthorizationHandlerContext context,
        TeamAuthRequirement requirement)
    {
        // Master ops never reach Tenant users — they're segregated to
        // their bound tenant (and, in a future release, to the dedicated
        // Tenant portal controllers).
        if (!requirement.TenantScoped)
        {
            await DenyAsync(context, requirement, "TenantUserOnMasterEndpoint", tenantCode: null);
            return;
        }

        if (!TenantAuthExtractor.TryExtract(context, logger, Name, out var auth))
            return;

        var rawTenantId = context.User.FindFirst(AuthConstants.TenantIdClaim)?.Value;
        if (!Guid.TryParse(rawTenantId, out var boundTenantId) || boundTenantId == Guid.Empty)
        {
            await DenyAsync(context, requirement, "TenantUserMissingTenantClaim", auth.Value.TenantCode);
            return;
        }

        var boundCode = await ResolveTenantCodeAsync(boundTenantId);
        if (string.IsNullOrEmpty(boundCode))
        {
            await DenyAsync(context, requirement, "TenantUserBoundToUnknownTenant", auth.Value.TenantCode);
            return;
        }

        if (!string.Equals(boundCode, auth.Value.TenantCode, StringComparison.OrdinalIgnoreCase))
        {
            await DenyAsync(context, requirement, "TenantUserBoundToDifferentTenant", auth.Value.TenantCode);
            return;
        }

        // Tenant users get Field-equivalent operations only (read +
        // Runs writes). Any policy with a MinimumSubtype set is denied;
        // the tenant ops they CAN do (CanAccessTenant) have no
        // MinimumSubtype set, so this falls through to succeed.
        if (requirement.MinimumSubtype is not null)
        {
            await DenyAsync(context, requirement, "TenantUserBeyondFieldEquivalent", auth.Value.TenantCode);
            return;
        }

        context.Succeed(requirement);
    }

    private async Task<bool> IsTenantMemberAsync(Guid identityId, string tenantCode)
    {
        var key = MembershipCacheKey(identityId, tenantCode);
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await master.TenantUsers
                .AsNoTracking()
                .AnyAsync(tu => tu.User!.IdentityId == identityId
                             && tu.Tenant!.Code == tenantCode);
        });
    }

    private async Task<string?> ResolveTenantCodeAsync(Guid tenantId)
    {
        var key = TenantIdToCodeCacheKey(tenantId);
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await master.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Code)
                .FirstOrDefaultAsync();
        });
    }

    private async Task DenyAsync(
        AuthorizationHandlerContext context,
        TeamAuthRequirement requirement,
        string reason,
        string? tenantCode)
    {
        var sub = context.User.FindFirst("sub")?.Value ?? "(unknown)";

        logger.LogInformation(
            "{Handler} denied: caller {Sub} on policy {Policy} — reason {Reason}.",
            Name, sub, RequirementShortName(requirement), reason);

        await denialAuditor.RecordAsync(
            policy:     RequirementShortName(requirement),
            tenantCode: tenantCode,
            actorSub:   sub,
            reason:     reason);
    }

    /// <summary>
    /// Compact label for the audit row when no policy name is in
    /// scope (the requirement doesn't carry the policy name; we
    /// synthesize one from its parameters for log readability).
    /// </summary>
    private static string RequirementShortName(TeamAuthRequirement r)
    {
        if (r.RequireAdmin) return "EnkiAdminOnly";
        var parts = new List<string>(4);
        parts.Add(r.TenantScoped ? "Tenant" : "Master");
        parts.Add(r.MinimumSubtype?.Name ?? "AnyMember");
        if (!string.IsNullOrEmpty(r.GrantingCapability))
            parts.Add($"orCap={r.GrantingCapability}");
        return string.Join("/", parts);
    }
}

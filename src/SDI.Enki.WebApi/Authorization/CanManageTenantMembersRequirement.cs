using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// Authorization requirement for tenant-membership management endpoints
/// (<c>POST/DELETE/PATCH /tenants/{code}/members/...</c>). Tighter than
/// <see cref="CanAccessTenantRequirement"/>: just being a member isn't
/// enough — you must be a tenant Admin (or a system <c>enki-admin</c>).
///
/// Apply with <c>[Authorize(Policy = EnkiPolicies.CanManageTenantMembers)]</c>.
/// </summary>
public sealed class CanManageTenantMembersRequirement : IAuthorizationRequirement;

public sealed class CanManageTenantMembersHandler(
    EnkiMasterDbContext master,
    IAuthzDenialAuditor denialAuditor,
    ILogger<CanManageTenantMembersHandler> logger)
    : AuthorizationHandler<CanManageTenantMembersRequirement>
{
    private const string Name = nameof(CanManageTenantMembersHandler);

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanManageTenantMembersRequirement requirement)
    {
        // Cross-tenant admin bypass — same shape as CanAccessTenant.
        if (context.User.HasEnkiAdminRole())
        {
            context.Succeed(requirement);
            return;
        }

        if (!TenantAuthExtractor.TryExtract(context, logger, Name, out var auth))
            return;

        // Only tenant Admins (TenantUserRole.Admin) can manage their
        // tenant's memberships; Contributors and Viewers cannot.
        var isTenantAdmin = await master.TenantUsers
            .AsNoTracking()
            .AnyAsync(tu => tu.User!.IdentityId == auth.Value.IdentityId
                         && tu.Tenant!.Code == auth.Value.TenantCode
                         && tu.Role == TenantUserRole.Admin);

        if (isTenantAdmin)
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogInformation(
                "{Handler} denied: identity {IdentityId} is not a tenant Admin of {TenantCode}.",
                Name, auth.Value.IdentityId, auth.Value.TenantCode);
            await denialAuditor.RecordAsync(
                policy:     EnkiPolicies.CanManageTenantMembers,
                tenantCode: auth.Value.TenantCode,
                actorSub:   auth.Value.IdentityId.ToString(),
                reason:     "NotATenantAdmin");
        }
    }
}

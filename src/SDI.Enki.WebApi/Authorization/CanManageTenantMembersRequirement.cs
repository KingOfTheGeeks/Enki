using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Identity;

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
    IHttpContextAccessor httpContextAccessor,
    AthenaMasterDbContext master,
    ILogger<CanManageTenantMembersHandler> logger)
    : AuthorizationHandler<CanManageTenantMembersRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanManageTenantMembersRequirement requirement)
    {
        var sub = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            logger.LogDebug("CanManageTenantMembers denied: no sub claim.");
            return;
        }

        // Cross-tenant admin bypass — same shape as CanAccessTenant.
        if (context.User.IsInRole(AuthConstants.EnkiAdminRole) ||
            context.User.HasClaim("role", AuthConstants.EnkiAdminRole))
        {
            context.Succeed(requirement);
            return;
        }

        var tenantCode = httpContextAccessor.HttpContext?.Request.RouteValues?["tenantCode"] as string;
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            logger.LogDebug("CanManageTenantMembers denied: no tenantCode in route.");
            return;
        }

        if (!Guid.TryParse(sub, out var identityId))
        {
            logger.LogDebug("CanManageTenantMembers denied: sub '{Sub}' is not a user GUID.", sub);
            return;
        }

        // Only tenant Admins (TenantUserRole.Admin == 1) can manage their
        // tenant's memberships; Contributors and Viewers cannot.
        var isTenantAdmin = await master.TenantUsers
            .AsNoTracking()
            .AnyAsync(tu => tu.User!.IdentityId == identityId
                         && tu.Tenant!.Code == tenantCode
                         && tu.Role == TenantUserRole.Admin);

        if (isTenantAdmin)
            context.Succeed(requirement);
        else
            logger.LogInformation(
                "CanManageTenantMembers denied: identity {IdentityId} is not a tenant Admin of {TenantCode}.",
                identityId, tenantCode);
    }
}

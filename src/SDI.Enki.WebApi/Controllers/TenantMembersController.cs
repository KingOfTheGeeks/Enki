using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Tenant-membership management. <c>TenantUser</c> rows attach a master
/// User to a tenant with a role (Admin / Contributor / Viewer). System
/// admins (<c>enki-admin</c>) and the tenant's own Admins can manage;
/// Contributors and Viewers cannot — see
/// <see cref="EnkiPolicies.CanManageTenantMembers"/>.
///
/// <para>
/// The list endpoint sits on the looser <see cref="EnkiPolicies.CanAccessTenant"/>
/// because every member of the tenant should be able to see who else is
/// in it; only mutations require Admin.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/members")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class TenantMembersController(
    EnkiMasterDbContext master,
    IMemoryCache cache) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = EnkiPolicies.CanAccessTenant)]
    [ProducesResponseType<IEnumerable<TenantMemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(string tenantCode, CancellationToken ct)
    {
        var tenant = await master.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        // Two-stage projection so RowVersion can be base64-encoded.
        var rows = await master.TenantUsers
            .AsNoTracking()
            .Where(tu => tu.TenantId == tenant.Id)
            .Include(tu => tu.User)
            .OrderBy(tu => tu.User!.Name)
            .Select(tu => new
            {
                tu.UserId,
                IdentityId = tu.User!.IdentityId,
                Username   = tu.User!.Name,
                RoleName   = tu.Role.Name,
                tu.GrantedAt,
                tu.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new TenantMemberDto(
            r.UserId, r.IdentityId, r.Username, r.RoleName, r.GrantedAt,
            ConcurrencyHelper.EncodeRowVersion(r.RowVersion))));
    }

    [HttpPost]
    [Authorize(Policy = EnkiPolicies.CanManageTenantMembers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add(
        string tenantCode,
        [FromBody] AddTenantMemberDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<TenantUserRole>(dto.Role, out var role))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AddTenantMemberDto.Role)] =
                    [SmartEnumExtensions.UnknownNameMessage<TenantUserRole>(dto.Role)],
            });

        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        // Pull the User row (not just AnyAsync) — we need IdentityId
        // for the membership-cache bust below.
        var user = await master.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId, ct);
        if (user is null) return this.NotFoundProblem("User", dto.UserId.ToString());

        var alreadyMember = await master.TenantUsers
            .AnyAsync(tu => tu.TenantId == tenant.Id && tu.UserId == dto.UserId, ct);
        if (alreadyMember)
            return this.ConflictProblem(
                "User is already a member of this tenant. Use PATCH to change the role.");

        master.TenantUsers.Add(new TenantUser(tenant.Id, dto.UserId, role));
        await master.SaveChangesAsync(ct);

        // Bust the cached "not a member" decision so the next request
        // from this user re-queries and sees the new membership.
        // Without this, the user could see 403s on tenant-scoped routes
        // for up to CanAccessTenantHandler.CacheDuration after being
        // granted access.
        cache.Remove(CanAccessTenantHandler.CacheKeyFor(user.IdentityId, tenantCode));

        return NoContent();
    }

    [HttpPatch("{userId:guid}")]
    [Authorize(Policy = EnkiPolicies.CanManageTenantMembers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRole(
        string tenantCode,
        Guid userId,
        [FromBody] SetTenantMemberRoleDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<TenantUserRole>(dto.Role, out var role))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(SetTenantMemberRoleDto.Role)] =
                    [SmartEnumExtensions.UnknownNameMessage<TenantUserRole>(dto.Role)],
            });

        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        var membership = await master.TenantUsers
            .FirstOrDefaultAsync(tu => tu.TenantId == tenant.Id && tu.UserId == userId, ct);
        if (membership is null)
            return this.NotFoundProblem("Membership", $"{tenantCode}/{userId}");

        if (this.ApplyClientRowVersion(master, membership, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        if (membership.Role == role) return NoContent();

        membership.Role = role;
        if (await master.SaveOrConflictAsync(this, "TenantUser", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    [Authorize(Policy = EnkiPolicies.CanManageTenantMembers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(
        string tenantCode,
        Guid userId,
        CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        var membership = await master.TenantUsers
            .Include(tu => tu.User)
            .FirstOrDefaultAsync(tu => tu.TenantId == tenant.Id && tu.UserId == userId, ct);
        if (membership is null)
            return this.NotFoundProblem("Membership", $"{tenantCode}/{userId}");

        master.TenantUsers.Remove(membership);
        await master.SaveChangesAsync(ct);

        // Bust the cached "is a member" decision so the next request
        // from this user re-queries and sees the revocation. Without
        // this, the user could continue accessing tenant-scoped routes
        // for up to CanAccessTenantHandler.CacheDuration after being
        // removed.
        if (membership.User is not null)
            cache.Remove(CanAccessTenantHandler.CacheKeyFor(membership.User.IdentityId, tenantCode));

        return NoContent();
    }

}

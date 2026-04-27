using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Authorization;
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
public sealed class TenantMembersController(EnkiMasterDbContext master) : ControllerBase
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

        var rows = await master.TenantUsers
            .AsNoTracking()
            .Where(tu => tu.TenantId == tenant.Id)
            .Include(tu => tu.User)
            .OrderBy(tu => tu.User!.Name)
            .Select(tu => new TenantMemberDto(
                tu.UserId,
                tu.User!.IdentityId,
                tu.User!.Name,
                tu.Role.Name,
                tu.GrantedAt))
            .ToListAsync(ct);

        return Ok(rows);
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

        var userExists = await master.Users.AnyAsync(u => u.Id == dto.UserId, ct);
        if (!userExists) return this.NotFoundProblem("User", dto.UserId.ToString());

        var alreadyMember = await master.TenantUsers
            .AnyAsync(tu => tu.TenantId == tenant.Id && tu.UserId == dto.UserId, ct);
        if (alreadyMember)
            return this.ConflictProblem(
                "User is already a member of this tenant. Use PATCH to change the role.");

        master.TenantUsers.Add(new TenantUser(tenant.Id, dto.UserId, role));
        await master.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{userId:guid}")]
    [Authorize(Policy = EnkiPolicies.CanManageTenantMembers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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

        if (membership.Role == role) return NoContent();

        membership.Role = role;
        await master.SaveChangesAsync(ct);
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
            .FirstOrDefaultAsync(tu => tu.TenantId == tenant.Id && tu.UserId == userId, ct);
        if (membership is null)
            return this.NotFoundProblem("Membership", $"{tenantCode}/{userId}");

        master.TenantUsers.Remove(membership);
        await master.SaveChangesAsync(ct);
        return NoContent();
    }

}

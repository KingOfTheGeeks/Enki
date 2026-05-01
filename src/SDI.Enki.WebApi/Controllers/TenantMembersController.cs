using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Tenant-membership management. <c>TenantUser</c> rows attach a master
/// User to a tenant.
///
/// <para>
/// <b>Role retired (2026-05-01).</b> The previous Admin / Contributor /
/// Viewer per-tenant role is gone — see <see cref="TenantUser"/> comments.
/// Member management is keyed off the system-wide <c>TeamSubtype</c>
/// hierarchy via <see cref="EnkiPolicies.CanManageTenantMembers"/>.
/// </para>
///
/// <para>
/// The list endpoint sits on the looser <see cref="EnkiPolicies.CanAccessTenant"/>
/// because every member of the tenant should be able to see who else is
/// in it; only mutations require the tighter policy.
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
                tu.GrantedAt,
                tu.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new TenantMemberDto(
            r.UserId, r.IdentityId, r.Username, r.GrantedAt,
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
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        // Pull the User row (not just AnyAsync) — we need IdentityId
        // for the membership-cache bust below.
        var user = await master.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId, ct);
        if (user is null) return this.NotFoundProblem("User", dto.UserId.ToString());

        var alreadyMember = await master.TenantUsers
            .AnyAsync(tu => tu.TenantId == tenant.Id && tu.UserId == dto.UserId, ct);
        if (alreadyMember)
            return this.ConflictProblem("User is already a member of this tenant.");

        master.TenantUsers.Add(new TenantUser(tenant.Id, dto.UserId));
        await master.SaveChangesAsync(ct);

        // Bust the cached "not a member" decision so the next request
        // from this user re-queries and sees the new membership.
        cache.Remove(TeamAuthHandler.MembershipCacheKey(user.IdentityId, tenantCode));

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
        // from this user re-queries and sees the revocation.
        if (membership.User is not null)
            cache.Remove(TeamAuthHandler.MembershipCacheKey(membership.User.IdentityId, tenantCode));

        return NoContent();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Authorization;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// "About me" endpoints scoped to the calling user. Today only one
/// member: the membership roster the Blazor side uses to populate
/// <c>IUserCapabilities</c>'s tenant-membership cache.
///
/// <para>
/// Sits on the universal <see cref="EnkiPolicies.EnkiApiScope"/> —
/// any signed-in caller can ask "which tenants am I a member of?";
/// the response is filtered to that caller's own memberships.
/// </para>
/// </summary>
[ApiController]
[Route("me")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class MeController(EnkiMasterDbContext master) : ControllerBase
{
    /// <summary>
    /// Returns the tenant codes the caller is a member of. Admins
    /// receive an empty list with <c>IsAdmin = true</c> — the Blazor
    /// side short-circuits gating against the admin claim before
    /// consulting this endpoint, but the flag is informative.
    /// Tenant-type users return their bound tenant code (resolved
    /// from <c>tenant_id</c>).
    /// </summary>
    [HttpGet("memberships")]
    [ProducesResponseType<MeMembershipsDto>(StatusCodes.Status200OK)]
    public async Task<MeMembershipsDto> Memberships(CancellationToken ct)
    {
        var isAdmin = User.HasEnkiAdminRole();
        if (isAdmin)
            return new MeMembershipsDto(IsAdmin: true, TenantCodes: Array.Empty<string>());

        // Tenant users: resolve their bound tenant_id to its code.
        if (User.IsTenantTypeUser())
        {
            var rawId = User.FindFirst(SDI.Enki.Shared.Identity.AuthConstants.TenantIdClaim)?.Value;
            if (Guid.TryParse(rawId, out var boundId) && boundId != Guid.Empty)
            {
                var code = await master.Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == boundId)
                    .Select(t => t.Code)
                    .FirstOrDefaultAsync(ct);
                return new MeMembershipsDto(
                    IsAdmin:     false,
                    TenantCodes: code is null ? Array.Empty<string>() : new[] { code });
            }
            return new MeMembershipsDto(IsAdmin: false, TenantCodes: Array.Empty<string>());
        }

        // Team users: pull every TenantUser row for them.
        var sub = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var identityId))
            return new MeMembershipsDto(IsAdmin: false, TenantCodes: Array.Empty<string>());

        var codes = await master.TenantUsers
            .AsNoTracking()
            .Where(tu => tu.User!.IdentityId == identityId)
            .Select(tu => tu.Tenant!.Code)
            .OrderBy(c => c)
            .ToListAsync(ct);

        return new MeMembershipsDto(IsAdmin: false, TenantCodes: codes);
    }
}

/// <summary>
/// Wire shape for <c>GET /me/memberships</c>.
/// </summary>
public sealed record MeMembershipsDto(
    bool IsAdmin,
    IReadOnlyList<string> TenantCodes);

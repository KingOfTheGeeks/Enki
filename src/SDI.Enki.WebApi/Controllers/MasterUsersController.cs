using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Authorization;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Master-DB User picker. Backs the "Add member" dialog on the tenant
/// detail page — the admin types a name, the dropdown lists matching
/// master Users, the selection's Id is what
/// <c>POST /tenants/{code}/members</c> expects.
///
/// <para>
/// Sits on <see cref="EnkiPolicies.EnkiApiScope"/> (any signed-in
/// caller with the enki scope) because picking from a list of names
/// isn't sensitive — the action of adding a member is the gated
/// operation, not knowing who exists.
/// </para>
/// </summary>
[ApiController]
[Route("admin/master-users")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class MasterUsersController(EnkiMasterDbContext master) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<MasterUserSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<MasterUserSummaryDto>> List(
        [FromQuery] string? q,
        CancellationToken ct)
    {
        var query = master.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            // .Contains translates to a LIKE with the wildcard inside the
            // bound parameter — so a user typing literal % or _ matches
            // those characters instead of expanding the search. Cheaper
            // to read than EF.Functions.Like + Regex.Escape, same
            // generated SQL shape.
            var trimmed = q.Trim();
            query = query.Where(u => u.Name.Contains(trimmed));
        }

        return await query
            .OrderBy(u => u.Name)
            .Select(u => new MasterUserSummaryDto(u.Id, u.IdentityId, u.Name))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Idempotent upsert of a master <c>User</c> row mirroring an
    /// existing Identity (<c>AspNetUsers</c>) row. Called by the
    /// Blazor admin Create flow right after the Identity-side
    /// <c>POST /admin/users</c> succeeds for a Team user.
    ///
    /// <para>
    /// <b>EnkiAdminOnly</b> overrides the controller's looser
    /// <see cref="EnkiPolicies.EnkiApiScope"/> default — listing
    /// existing master users is fine for any signed-in admin UI
    /// caller, but creating one is a privileged write. Returns the
    /// resolved (existing or newly-created) master <c>User.Id</c>;
    /// <c>Created = false</c> on the idempotent no-op path so the
    /// caller can distinguish "I just created this" from "this was
    /// already here" without a follow-up GET.
    /// </para>
    /// </summary>
    [HttpPost("sync")]
    [Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
    [ProducesResponseType<SyncMasterUserResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<SyncMasterUserResponseDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Sync(
        [FromBody] SyncMasterUserDto dto,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (dto.IdentityId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(dto.IdentityId), "IdentityId must be a non-empty GUID.");
            return ValidationProblem(ModelState);
        }

        var existing = await master.Users
            .FirstOrDefaultAsync(u => u.IdentityId == dto.IdentityId, ct);

        if (existing is not null)
        {
            // Refresh the display name on existing rows so a profile
            // edit on the Identity side propagates here. No-op when
            // the column already matches; cheaper than a separate
            // "rename master user" endpoint.
            if (!string.Equals(existing.Name, dto.DisplayName, StringComparison.Ordinal))
            {
                existing.Name = dto.DisplayName;
                await master.SaveChangesAsync(ct);
            }
            return Ok(new SyncMasterUserResponseDto(existing.Id, Created: false));
        }

        var created = new User(dto.DisplayName, dto.IdentityId);
        master.Users.Add(created);
        await master.SaveChangesAsync(ct);

        return CreatedAtAction(
            actionName: nameof(List),
            value:      new SyncMasterUserResponseDto(created.Id, Created: true));
    }
}

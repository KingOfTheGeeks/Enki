using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
public sealed class MasterUsersController(EnkiMasterDbContext master) : ControllerBase
{
    [HttpGet]
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
}

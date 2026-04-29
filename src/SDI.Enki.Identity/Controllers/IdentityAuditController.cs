using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.Identity.Controllers;

/// <summary>
/// Read-only audit-log endpoints for the Identity DB. The third twin
/// of the tenant + master audit controllers, scoped to sensitive
/// admin-DB actions: admin-role flips, password resets, lockouts.
///
/// <para>
/// Lives in the Identity host (not WebApi) for the same reason
/// <c>AdminUsersController</c> does — every byte of data this serves
/// lives in the Identity DB, and adding a cross-host hop just to
/// serve a read would mean WebApi holds an Identity DbContext too.
/// Admin pages already hit <c>https://identity/admin/users/...</c>;
/// they hit this endpoint the same way.
/// </para>
///
/// <para>
/// Auth uses the OpenIddict validation scheme (same as the rest of
/// this controller surface) gated by the <c>EnkiAdmin</c> policy —
/// only enki-admins can read the Identity audit feed.
/// </para>
/// </summary>
[ApiController]
[Route("admin/audit/identity")]
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Policy = "EnkiAdmin")]
public sealed class IdentityAuditController(ApplicationDbContext db) : ControllerBase
{
    private const int DefaultTake = 50;
    private const int MaxTake     = 500;

    [HttpGet]
    public async Task<PagedResult<AuditLogEntryDto>> List(
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var pageSkip = Math.Max(0, skip ?? 0);
        var pageTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        var total = await db.IdentityAuditLogs.CountAsync(ct);
        var rows = await db.IdentityAuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .Skip(pageSkip)
            .Take(pageTake)
            .Select(a => new AuditLogEntryDto(
                a.Id, a.EntityType, a.EntityId, a.Action,
                a.OldValues, a.NewValues, a.ChangedColumns,
                a.ChangedAt, a.ChangedBy))
            .ToListAsync(ct);

        return new PagedResult<AuditLogEntryDto>(rows, total, pageSkip, pageTake);
    }

    [HttpGet("{entityType}/{entityId}")]
    public async Task<PagedResult<AuditLogEntryDto>> ListForEntity(
        string entityType,
        string entityId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var pageSkip = Math.Max(0, skip ?? 0);
        var pageTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        var query = db.IdentityAuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .Skip(pageSkip)
            .Take(pageTake)
            .Select(a => new AuditLogEntryDto(
                a.Id, a.EntityType, a.EntityId, a.Action,
                a.OldValues, a.NewValues, a.ChangedColumns,
                a.ChangedAt, a.ChangedBy))
            .ToListAsync(ct);

        return new PagedResult<AuditLogEntryDto>(rows, total, pageSkip, pageTake);
    }
}

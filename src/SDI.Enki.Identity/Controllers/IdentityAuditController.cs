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
/// </para>
///
/// <para>
/// Three endpoints, same shape as the master + tenant twins:
/// <c>GET /admin/audit/identity</c> (paged JSON),
/// <c>GET /admin/audit/identity/csv</c> (full CSV export),
/// <c>GET /admin/audit/identity/{entityType}/{entityId}</c> (per-entity).
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
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null,
        [FromQuery] string? changedBy = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
    {
        var pageSkip = Math.Max(0, skip ?? 0);
        var pageTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        var query = ApplyFilters(db.IdentityAuditLogs.AsNoTracking(),
            from, to, entityType, action, changedBy);

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

    [HttpGet("csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null,
        [FromQuery] string? changedBy = null,
        CancellationToken ct = default)
    {
        var query = ApplyFilters(db.IdentityAuditLogs.AsNoTracking(),
            from, to, entityType, action, changedBy);

        var rows = await query
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new AuditLogEntryDto(
                a.Id, a.EntityType, a.EntityId, a.Action,
                a.OldValues, a.NewValues, a.ChangedColumns,
                a.ChangedAt, a.ChangedBy))
            .ToListAsync(ct);

        var csv = AuditCsv.Serialize(rows,
        [
            ("Id",             r => r.Id),
            ("ChangedAt",      r => r.ChangedAt),
            ("EntityType",     r => r.EntityType),
            ("EntityId",       r => r.EntityId),
            ("Action",         r => r.Action),
            ("ChangedBy",      r => r.ChangedBy),
            ("ChangedColumns", r => r.ChangedColumns),
            ("OldValues",      r => r.OldValues),
            ("NewValues",      r => r.NewValues),
        ]);

        return new FileContentResult(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv")
        {
            FileDownloadName = $"enki-identity-audit-{DateTimeOffset.UtcNow:yyyy-MM-dd}.csv",
        };
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

    private static IQueryable<IdentityAuditLog> ApplyFilters(
        IQueryable<IdentityAuditLog> q,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? entityType,
        string? action,
        string? changedBy)
    {
        if (from is { } f)            q = q.Where(a => a.ChangedAt >= f);
        if (to is { } t)              q = q.Where(a => a.ChangedAt <  t);
        if (!string.IsNullOrWhiteSpace(entityType))
            q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(changedBy))
            q = q.Where(a => a.ChangedBy.Contains(changedBy));
        return q;
    }
}

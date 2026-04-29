using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Audit;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;
using SDI.Enki.WebApi.Authorization;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Read-only audit-log endpoints for the master DB. The twin of
/// <see cref="AuditController"/> (tenant-side) for cross-tenant
/// admin / ops events: Tenant provision / update / deactivate,
/// TenantUser role grants and revocations, License generation
/// and revocation, and AuthzDenial rows captured by the
/// authorization handlers.
///
/// <para>
/// <b>Why <c>EnkiAdminOnly</c>:</b> tenant audit (per-tenant) is
/// readable by tenant members because it never crosses tenant
/// boundaries. Master audit reveals every customer's
/// privilege churn + license issuance — an SDI-internal view by
/// definition.
/// </para>
///
/// <para>
/// Three endpoints:
/// <list type="bullet">
///   <item><c>GET /admin/audit/master</c> — paged JSON feed with
///   filters: from/to (date range), entityType, action, changedBy
///   (partial match). All optional.</item>
///   <item><c>GET /admin/audit/master/csv</c> — same filter set,
///   CSV body, no pagination (full export of matching rows).</item>
///   <item><c>GET /admin/audit/master/{entityType}/{entityId}</c>
///   — full history for a single entity.</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("admin/audit/master")]
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class MasterAuditController(EnkiMasterDbContext master) : ControllerBase
{
    private const int DefaultTake = 50;
    private const int MaxTake     = 500;

    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogEntryDto>>(StatusCodes.Status200OK)]
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

        var query = ApplyFilters(master.MasterAuditLogs.AsNoTracking(),
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
        var query = ApplyFilters(master.MasterAuditLogs.AsNoTracking(),
            from, to, entityType, action, changedBy);

        // Stream the full filtered set — no pagination on CSV. The
        // caller already filtered server-side; if the result is huge
        // the operator chose a wide window.
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
            FileDownloadName = $"enki-master-audit-{DateTimeOffset.UtcNow:yyyy-MM-dd}.csv",
        };
    }

    [HttpGet("{entityType}/{entityId}")]
    [ProducesResponseType<PagedResult<AuditLogEntryDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<AuditLogEntryDto>> ListForEntity(
        string entityType,
        string entityId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var pageSkip = Math.Max(0, skip ?? 0);
        var pageTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        var query = master.MasterAuditLogs
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

    private static IQueryable<MasterAuditLog> ApplyFilters(
        IQueryable<MasterAuditLog> q,
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

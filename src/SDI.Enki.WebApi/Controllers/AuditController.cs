using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Audit;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Read-only audit-log endpoints scoped to a tenant. The audit table
/// itself is append-only and populated by
/// <c>TenantDbContext.SaveChangesAsync</c> — there is no Create /
/// Update / Delete here, by design.
///
/// <para>
/// Three endpoints:
/// <list type="bullet">
///   <item><c>GET /tenants/{tenantCode}/audit</c> — paged JSON feed
///   with filters: from/to (date range), entityType, action,
///   changedBy (partial match). All optional.</item>
///   <item><c>GET /tenants/{tenantCode}/audit/csv</c> — same filter
///   set, CSV body, no pagination.</item>
///   <item><c>GET /tenants/{tenantCode}/audit/{entityType}/{entityId}</c>
///   — full history for a single entity. Powers the "Recent
///   changes" tile on entity detail pages.</item>
/// </list>
/// </para>
///
/// <para>
/// Authorization mirrors <c>JobsController</c> — requires
/// <see cref="EnkiPolicies.CanAccessTenant"/> (tenant member or
/// system admin). The audit log surfaces every change anyone has
/// made to the tenant; it's not more sensitive than the data
/// itself, but it shouldn't leak across tenants.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/audit")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AuditController(ITenantDbContextFactory dbFactory) : ControllerBase
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

        await using var db = dbFactory.CreateActive();

        var query = ApplyFilters(db.AuditLogs.AsNoTracking(),
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
        await using var db = dbFactory.CreateActive();

        var query = ApplyFilters(db.AuditLogs.AsNoTracking(),
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
            FileDownloadName = $"enki-tenant-audit-{DateTimeOffset.UtcNow:yyyy-MM-dd}.csv",
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

        await using var db = dbFactory.CreateActive();

        var query = db.AuditLogs
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

    private static IQueryable<AuditLog> ApplyFilters(
        IQueryable<AuditLog> q,
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

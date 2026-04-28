using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Read-only audit-log endpoints scoped to a tenant. The audit table
/// itself is append-only and populated by
/// <c>TenantDbContext.SaveChangesAsync</c> — there is no Create /
/// Update / Delete here, by design. Two query shapes:
///
/// <list type="bullet">
///   <item>
///     <c>GET /tenants/{tenantCode}/audit</c> — recent changes across
///     the whole tenant, ordered by ChangedAt desc. The default for
///     a tenant-wide history view.
///   </item>
///   <item>
///     <c>GET /tenants/{tenantCode}/audit/{entityType}/{entityId}</c>
///     — full history for a single entity (e.g. Survey #42's edit
///     trail), same ordering. Powers the "Recent changes" tile on
///     entity detail pages.
///   </item>
/// </list>
///
/// <para>
/// Both endpoints are paged via <see cref="PagedResult{T}"/>; the
/// audit table is unbounded (every IAuditable mutation appends a
/// row) so a default <c>take=50</c> guards against a runaway
/// payload on a long-lived tenant.
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

    /// <summary>
    /// Tenant-wide recent-changes feed. Returns a paged view of every
    /// audit row, newest first. Suited to an admin "what just
    /// happened" dashboard; for a single entity's history use the
    /// entity-scoped endpoint below.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogEntryDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<AuditLogEntryDto>> List(
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var pageSkip = Math.Max(0, skip ?? 0);
        var pageTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        await using var db = dbFactory.CreateActive();

        var total = await db.AuditLogs.CountAsync(ct);
        var rows = await db.AuditLogs
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

    /// <summary>
    /// Per-entity history feed. Returns a paged view of every audit
    /// row for one entity (matched by CLR class name + primary-key
    /// string), newest first. Powers the "Recent changes" tile on
    /// entity detail pages — the UI passes <c>entityType=Survey</c>,
    /// <c>entityId=42</c> and renders the response.
    /// </summary>
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
}

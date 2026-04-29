using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
/// and revocation. The master audit table is append-only and
/// populated by <c>EnkiMasterDbContext.SaveChangesAsync</c>.
///
/// <para>
/// <b>Why <c>EnkiAdminOnly</c>:</b> tenant audit (per-tenant) is
/// readable by tenant members because it never crosses tenant
/// boundaries. Master audit reveals every customer's
/// privilege churn + license issuance — an SDI-internal view by
/// definition. Sits at the same bar as Provision / Deactivate.
/// </para>
///
/// <para>
/// Two query shapes mirror the tenant-side controller:
/// <list type="bullet">
///   <item><c>GET /admin/audit/master</c> — recent changes across
///   the master DB, ordered by ChangedAt desc.</item>
///   <item><c>GET /admin/audit/master/{entityType}/{entityId}</c>
///   — full history for a single entity. Composite-PK entities
///   (TenantUser is keyed on TenantId+UserId) use the pipe-joined
///   form: <c>tenants/PERMIAN/audit/master/TenantUser/{tenantId}|{userId}</c>.
///   </item>
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

    /// <summary>
    /// Master-wide recent-changes feed. Newest first, paged via
    /// <see cref="PagedResult{T}"/>; default <c>take=50</c>, capped at 500.
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

        var total = await master.MasterAuditLogs.CountAsync(ct);
        var rows = await master.MasterAuditLogs
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
    /// Per-entity history feed. <paramref name="entityId"/> is the
    /// pipe-joined PK string the interceptor wrote — see
    /// <c>EnkiMasterDbContext.BuildAuditEntry</c> for the shape.
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
}

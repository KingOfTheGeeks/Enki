using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Audit;
using SDI.Enki.Infrastructure.Data;
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

    /// <summary>
    /// Per-entity audit history. Default behaviour returns rows for
    /// only this exact <c>(entityType, entityId)</c> pair — the
    /// AuditHistoryTile uses this for its initial render.
    ///
    /// <para>
    /// When <paramref name="includeChildren"/> is true the controller
    /// also resolves the entity's child rows in the tenant DB and
    /// includes their audit history. Container types supported:
    /// <list type="bullet">
    ///   <item><c>Job</c> → its Wells (with their subtrees) + Runs (with theirs).</item>
    ///   <item><c>Well</c> → its Surveys, TieOns, Tubulars, Formations, CommonMeasures, Magnetics.</item>
    ///   <item><c>Run</c> → its Shots and Logs.</item>
    /// </list>
    /// Other entity types ignore <paramref name="includeChildren"/>
    /// and return their own rows only — they have no children that
    /// own audit rows. The tenant-wide feed at
    /// <c>GET /tenants/{tenantCode}/audit</c> already covers the
    /// "everything in the tenant" case so we don't add a Tenant-level
    /// subtree here.
    /// </para>
    ///
    /// <para>
    /// <b>Why opt-in:</b> resolving the subtree means N+1-style
    /// queries to enumerate child IDs, then a per-EntityType audit
    /// fetch. For most page renders the user only wants the current
    /// entity; the tile fires this with <c>includeChildren=false</c>
    /// on first paint and only flips to <c>true</c> when the user
    /// asks for the wider view via the toggle.
    /// </para>
    /// </summary>
    [HttpGet("{entityType}/{entityId}")]
    [ProducesResponseType<PagedResult<AuditLogEntryDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<AuditLogEntryDto>> ListForEntity(
        string entityType,
        string entityId,
        [FromQuery] bool includeChildren = false,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
    {
        var pageSkip = Math.Max(0, skip ?? 0);
        var pageTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        await using var db = dbFactory.CreateActive();

        if (!includeChildren)
        {
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

        // Subtree path. Resolve the (EntityType, EntityId) pairs that
        // make up the parent + all its children, then fetch + merge.
        var pairs = await ResolveSubtreePairsAsync(db, entityType, entityId, ct);

        // Group by EntityType and issue one query per type — each is
        // an indexed seek (EntityType, EntityId) on AuditLog. Smaller
        // round-trips than one huge OR-chain, and EF Core won't
        // translate the latter into anything pretty.
        var allRows = new List<AuditLog>();
        foreach (var grouping in pairs.GroupBy(p => p.EntityType))
        {
            var ids = grouping.Select(p => p.EntityId).Distinct().ToList();
            var rows = await db.AuditLogs
                .AsNoTracking()
                .Where(a => a.EntityType == grouping.Key && ids.Contains(a.EntityId))
                .ToListAsync(ct);
            allRows.AddRange(rows);
        }

        var ordered = allRows
            .OrderByDescending(a => a.ChangedAt)
            .ThenByDescending(a => a.Id)
            .ToList();

        var page = ordered
            .Skip(pageSkip)
            .Take(pageTake)
            .Select(a => new AuditLogEntryDto(
                a.Id, a.EntityType, a.EntityId, a.Action,
                a.OldValues, a.NewValues, a.ChangedColumns,
                a.ChangedAt, a.ChangedBy))
            .ToList();

        return new PagedResult<AuditLogEntryDto>(page, ordered.Count, pageSkip, pageTake);
    }

    /// <summary>
    /// Walk the tenant DB to enumerate the (EntityType, EntityId)
    /// pairs that make up <paramref name="parentType"/>'s subtree.
    /// Always includes the parent itself; unknown parent types yield
    /// only the parent (no subtree).
    /// </summary>
    private static async Task<List<(string EntityType, string EntityId)>> ResolveSubtreePairsAsync(
        TenantDbContext db,
        string parentType,
        string parentId,
        CancellationToken ct)
    {
        var pairs = new List<(string, string)> { (parentType, parentId) };

        switch (parentType)
        {
            case "Well":
                if (int.TryParse(parentId, out var wellId))
                    await AppendWellChildrenAsync(db, wellId, pairs, ct);
                break;

            case "Run":
                if (Guid.TryParse(parentId, out var runId))
                    await AppendRunChildrenAsync(db, runId, pairs, ct);
                break;

            case "Job":
                if (Guid.TryParse(parentId, out var jobId))
                    await AppendJobSubtreeAsync(db, jobId, pairs, ct);
                break;

            // Tenant-level subtree is the same as the existing tenant-wide
            // feed (GET /tenants/{code}/audit). Not wired here.
        }

        return pairs;
    }

    private static async Task AppendWellChildrenAsync(
        TenantDbContext db, int wellId,
        List<(string, string)> pairs, CancellationToken ct)
    {
        var surveys     = await db.Surveys       .AsNoTracking().Where(x => x.WellId == wellId).Select(x => x.Id).ToListAsync(ct);
        var tieOns      = await db.TieOns        .AsNoTracking().Where(x => x.WellId == wellId).Select(x => x.Id).ToListAsync(ct);
        var tubulars    = await db.Tubulars      .AsNoTracking().Where(x => x.WellId == wellId).Select(x => x.Id).ToListAsync(ct);
        var formations  = await db.Formations    .AsNoTracking().Where(x => x.WellId == wellId).Select(x => x.Id).ToListAsync(ct);
        var measures    = await db.CommonMeasures.AsNoTracking().Where(x => x.WellId == wellId).Select(x => x.Id).ToListAsync(ct);
        var magnetics   = await db.Magnetics     .AsNoTracking().Where(x => x.WellId == wellId).Select(x => x.Id).ToListAsync(ct);

        pairs.AddRange(surveys     .Select(id => ("Survey",        id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        pairs.AddRange(tieOns      .Select(id => ("TieOn",         id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        pairs.AddRange(tubulars    .Select(id => ("Tubular",       id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        pairs.AddRange(formations  .Select(id => ("Formation",     id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        pairs.AddRange(measures    .Select(id => ("CommonMeasure", id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        pairs.AddRange(magnetics   .Select(id => ("Magnetics",     id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static async Task AppendRunChildrenAsync(
        TenantDbContext db, Guid runId,
        List<(string, string)> pairs, CancellationToken ct)
    {
        var shots = await db.Shots.AsNoTracking().Where(x => x.RunId == runId).Select(x => x.Id).ToListAsync(ct);
        var logs  = await db.Logs .AsNoTracking().Where(x => x.RunId == runId).Select(x => x.Id).ToListAsync(ct);

        pairs.AddRange(shots.Select(id => ("Shot", id.ToString())));
        pairs.AddRange(logs .Select(id => ("Log",  id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static async Task AppendJobSubtreeAsync(
        TenantDbContext db, Guid jobId,
        List<(string, string)> pairs, CancellationToken ct)
    {
        var wellIds = await db.Wells.AsNoTracking().Where(x => x.JobId == jobId).Select(x => x.Id).ToListAsync(ct);
        foreach (var wellId in wellIds)
        {
            pairs.Add(("Well", wellId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await AppendWellChildrenAsync(db, wellId, pairs, ct);
        }

        var runIds = await db.Runs.AsNoTracking().Where(x => x.JobId == jobId).Select(x => x.Id).ToListAsync(ct);
        foreach (var runId in runIds)
        {
            pairs.Add(("Run", runId.ToString()));
            await AppendRunChildrenAsync(db, runId, pairs, ct);
        }
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

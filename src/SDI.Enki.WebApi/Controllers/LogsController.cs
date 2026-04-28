using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Logs;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Logs;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Logs under a Run. A Log is one captured "log activity" — each
/// run can have many. Each Log carries its own samples / time-windows /
/// EFD samples + a binary file payload + run-type-specific processing
/// satellites (LogProcessing / RotaryLogProcessing /
/// PassiveLogProcessing). Phase 1 exposes only the metadata CRUD —
/// child sample-shape endpoints land in Phase 2 alongside type-
/// specific UI.
///
/// <para>
/// Routes nest fully:
/// <c>/tenants/{tenantCode}/jobs/{jobId:guid}/runs/{runId:guid}/logs</c>.
/// Every action confirms the parent (jobId, runId) pair belongs
/// together — an unknown pair surfaces as a 404 NotFoundProblem
/// rather than a silent empty list or wrong-tenant data leak. Same
/// shape as <c>SurveysController</c>'s parent-pair guard.
/// </para>
///
/// <para>
/// Concurrency + audit ride along on the standard
/// <see cref="ConcurrencyHelper"/> / <c>SaveOrConflictAsync</c>
/// pattern. Hard-delete (not soft-delete) — Logs are operational
/// records under a Run; if you want to "remove" them, archive the
/// Run instead and the children stay in the DB for restore.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/runs/{runId:guid}/logs")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class LogsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<LogSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        // Two-stage projection so the post-query map can encode
        // RowVersion to base64.
        var rows = await db.Logs
            .AsNoTracking()
            .Where(l => l.RunId == runId)
            .OrderByDescending(l => l.FileTime)
            .Select(l => new
            {
                l.Id, l.RunId, l.ShotName, l.FileTime,
                l.CalibrationId, l.MagneticId, l.LogSettingId,
                l.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(l => new LogSummaryDto(
            l.Id, l.RunId, l.ShotName, l.FileTime,
            l.CalibrationId, l.MagneticId, l.LogSettingId,
            ConcurrencyHelper.EncodeRowVersion(l.RowVersion))));
    }

    // ---------- detail ----------

    [HttpGet("{logId:int}")]
    [ProducesResponseType<LogDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, Guid runId, int logId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var row = await db.Logs
            .AsNoTracking()
            .Where(l => l.Id == logId && l.RunId == runId)
            .Select(l => new
            {
                l.Id, l.RunId, l.ShotName, l.FileTime,
                l.CalibrationId, l.MagneticId, l.LogSettingId,
                l.CreatedAt, l.CreatedBy, l.UpdatedAt, l.UpdatedBy,
                l.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Log", logId.ToString());

        return Ok(new LogDetailDto(
            row.Id, row.RunId, row.ShotName, row.FileTime,
            row.CalibrationId, row.MagneticId, row.LogSettingId,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<LogDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId,
        Guid runId,
        [FromBody] CreateLogDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var log = new Log(runId, dto.ShotName, dto.FileTime)
        {
            CalibrationId = dto.CalibrationId,
            MagneticId    = dto.MagneticId,
            LogSettingId  = dto.LogSettingId,
        };
        db.Logs.Add(log);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId, logId = log.Id },
            new LogDetailDto(
                log.Id, log.RunId, log.ShotName, log.FileTime,
                log.CalibrationId, log.MagneticId, log.LogSettingId,
                log.CreatedAt, log.CreatedBy, log.UpdatedAt, log.UpdatedBy,
                ConcurrencyHelper.EncodeRowVersion(log.RowVersion)));
    }

    // ---------- update ----------

    [HttpPut("{logId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId,
        Guid runId,
        int logId,
        [FromBody] UpdateLogDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var log = await db.Logs.FirstOrDefaultAsync(l => l.Id == logId && l.RunId == runId, ct);
        if (log is null) return this.NotFoundProblem("Log", logId.ToString());

        if (this.ApplyClientRowVersion(log, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        log.ShotName      = dto.ShotName;
        log.FileTime      = dto.FileTime;
        log.CalibrationId = dto.CalibrationId;
        log.MagneticId    = dto.MagneticId;
        log.LogSettingId  = dto.LogSettingId;

        if (await db.SaveOrConflictAsync(this, "Log", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // ---------- delete ----------

    /// <summary>
    /// Hard-delete a Log. Cascades to its sample-shape children
    /// (LogSample, LogFile, LogTimeWindow, LogEfdSample) and the
    /// 1:0..1 processing satellites. If you want a non-destructive
    /// "remove from view," archive the parent Run instead — the
    /// global query filter on Run hides the entire branch.
    /// </summary>
    [HttpDelete("{logId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid jobId, Guid runId, int logId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var log = await db.Logs.FirstOrDefaultAsync(l => l.Id == logId && l.RunId == runId, ct);
        if (log is null) return this.NotFoundProblem("Log", logId.ToString());

        db.Logs.Remove(log);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- helpers ----------

    /// <summary>
    /// Confirms the (jobId, runId) pair belongs together. Catches
    /// the wrong-tenant / wrong-job hot-route error before any
    /// child query — parallel to <c>WellLookup.WellExistsAsync</c>.
    /// </summary>
    private static Task<bool> RunExistsAsync(
        TenantDbContext db,
        Guid jobId,
        Guid runId,
        CancellationToken ct) =>
        db.Runs.AsNoTracking().AnyAsync(r => r.Id == runId && r.JobId == jobId, ct);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Concurrency;
using SDI.Enki.Shared.Runs;
using SDI.Enki.Shared.Tools;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Calibrations;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;
using SDI.Enki.WebApi.Validation;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Runs under a Job. A Run is one operational session — Gradient,
/// Rotary, or Passive — during which the BHA is downhole collecting
/// data. Each run carries one or more <c>Log</c> entries with the
/// captured samples / files / time-windows; type-specific processing
/// satellites (LogProcessing / RotaryLogProcessing / PassiveLogProcessing)
/// hang off each Log.
///
/// <para>
/// Routes nest under Job: <c>/tenants/{tenantCode}/jobs/{jobId:guid}/runs</c>.
/// CRUD shape mirrors <see cref="JobsController"/> + soft-delete shape
/// mirrors <see cref="WellsController"/> — same patterns, same helper
/// vocabulary (<see cref="ConcurrencyHelper"/> for the rowversion
/// round-trip; <c>IgnoreQueryFilters()</c> for archived-row reads).
/// </para>
///
/// <para>
/// Lifecycle is richer than Job's Draft→Active→Archived because a
/// run is operational — see <see cref="RunLifecycle"/>. Five status
/// values (Planned, Active, Suspended, Completed, Cancelled) and
/// transitions for start/pause/resume/finish/cancel.
/// </para>
///
/// <para>
/// <b>Tool / Calibration / Magnetics wiring (issue #26 follow-up):</b>
/// </para>
/// <list type="bullet">
///   <item><c>ToolId</c> is OPTIONAL on Create. Validated against the
///   master <c>Tools</c> table when supplied. Settable later via
///   Update; once shots / logs exist, it can be CHANGED but not
///   CLEARED.</item>
///   <item>Whenever a ToolId is assigned (Create or Update),
///   <see cref="CalibrationSnapshotService"/> copies the tool's
///   latest non-superseded master Calibration into the tenant DB and
///   stamps <c>Run.SnapshotCalibrationId</c>. The snapshot is the
///   default <c>CalibrationId</c> for new Shots / Logs under this run.
///   Re-assigning the same tool is idempotent (existing snapshot
///   reused); changing tools creates a new snapshot — old captures
///   keep their original snapshot row.</item>
///   <item>Magnetics (BTotal / Dip / Declination) is REQUIRED at
///   Create. The controller creates a per-run <c>Magnetics</c> row
///   (WellId null) and stamps <c>Run.MagneticsId</c>. Update writes
///   straight to the existing row.</item>
/// </list>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/runs")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class RunsController(
    ITenantDbContextFactory dbFactory,
    EnkiMasterDbContext master,
    CalibrationSnapshotService snapshotter) : ControllerBase
{
    /// <summary>≤ 250 KB Passive capture binary. Mirrors the Shot
    /// primary cap; enforced server-side.</summary>
    public const long MaxPassiveBinaryBytes = 250 * 1024;

    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<RunSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, ct))
            return this.NotFoundProblem("Job", jobId.ToString());

        var rows = await db.Runs
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.Name, r.Description,
                TypeName = r.Type.Name, StatusName = r.Status.Name,
                r.StartDepth, r.EndDepth,
                r.StartTimestamp, r.EndTimestamp,
                r.ToolId,
                LogCount  = r.Logs.Count,
                ShotCount = r.Shots.Count,
                r.RowVersion,
            })
            .ToListAsync(ct);

        // Resolve display names from master in one shot — small in-
        // memory join keyed on ToolId. Per-row master roundtrips would
        // be wasteful for a list endpoint.
        var toolIds = rows.Where(r => r.ToolId is not null).Select(r => r.ToolId!.Value).Distinct().ToList();
        var toolDisplay = await ResolveToolDisplayNamesAsync(toolIds, ct);

        return Ok(rows.Select(r => new RunSummaryDto(
            r.Id, r.Name, r.Description,
            r.TypeName, r.StatusName,
            r.StartDepth, r.EndDepth,
            r.StartTimestamp, r.EndTimestamp,
            r.ToolId,
            r.ToolId is { } tid && toolDisplay.TryGetValue(tid, out var dn) ? dn : null,
            r.LogCount, r.ShotCount,
            ConcurrencyHelper.EncodeRowVersion(r.RowVersion))));
    }

    // ---------- archived (admin/restore) ----------

    /// <summary>
    /// List archived (soft-deleted) runs under the job. Bypasses the
    /// global query filter via <c>IgnoreQueryFilters()</c>. Used by
    /// admin / cleanup flows that need to find a previously-archived
    /// run to restore. Active runs are excluded — see
    /// <see cref="List"/> for those.
    /// </summary>
    [HttpGet("archived")]
    [ProducesResponseType<IEnumerable<RunSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListArchived(Guid jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, ct))
            return this.NotFoundProblem("Job", jobId.ToString());

        var rows = await db.Runs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.ArchivedAt != null)
            .OrderByDescending(r => r.ArchivedAt)
            .Select(r => new
            {
                r.Id, r.Name, r.Description,
                TypeName = r.Type.Name, StatusName = r.Status.Name,
                r.StartDepth, r.EndDepth,
                r.StartTimestamp, r.EndTimestamp,
                r.ToolId,
                LogCount  = r.Logs.Count,
                ShotCount = r.Shots.Count,
                r.RowVersion,
            })
            .ToListAsync(ct);

        var toolIds = rows.Where(r => r.ToolId is not null).Select(r => r.ToolId!.Value).Distinct().ToList();
        var toolDisplay = await ResolveToolDisplayNamesAsync(toolIds, ct);

        return Ok(rows.Select(r => new RunSummaryDto(
            r.Id, r.Name, r.Description,
            r.TypeName, r.StatusName,
            r.StartDepth, r.EndDepth,
            r.StartTimestamp, r.EndTimestamp,
            r.ToolId,
            r.ToolId is { } tid && toolDisplay.TryGetValue(tid, out var dn) ? dn : null,
            r.LogCount, r.ShotCount,
            ConcurrencyHelper.EncodeRowVersion(r.RowVersion))));
    }

    // ---------- detail ----------

    [HttpGet("{runId:guid}")]
    [ProducesResponseType<RunDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var row = await db.Runs
            .AsNoTracking()
            .Where(r => r.Id == runId && r.JobId == jobId)
            .Select(r => new
            {
                r.Id, r.JobId, r.Name, r.Description,
                TypeName = r.Type.Name, StatusName = r.Status.Name,
                r.StartDepth, r.EndDepth,
                r.StartTimestamp, r.EndTimestamp,
                r.CreatedAt, r.CreatedBy, r.UpdatedAt, r.UpdatedBy,
                r.BridleLength, r.CurrentInjection,
                r.ToolId,
                r.SnapshotCalibrationId,
                SnapshotDate          = r.SnapshotCalibration != null ? (DateTimeOffset?)r.SnapshotCalibration.CalibrationDate : null,
                SnapshotSerialNumber  = r.SnapshotCalibration != null ? (int?)r.SnapshotCalibration.SerialNumber : null,
                BTotal                = r.Magnetics!.BTotal,
                Dip                   = r.Magnetics.Dip,
                Declination           = r.Magnetics.Declination,
                OperatorNames = r.Operators.Select(o => o.Name).ToList(),
                LogCount  = r.Logs.Count,
                ShotCount = r.Shots.Count,
                HasPassiveBinary = r.PassiveBinary != null,
                r.PassiveBinaryName, r.PassiveBinaryUploadedAt,
                r.PassiveConfigJson, r.PassiveConfigUpdatedAt,
                r.PassiveResultJson, r.PassiveResultComputedAt, r.PassiveResultMardukVersion,
                r.PassiveResultStatus, r.PassiveResultError,
                r.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Run", runId.ToString());

        // Tool display: resolve from master if a tool is assigned.
        string? toolDisplayName = null;
        if (row.ToolId is { } toolIdForDisplay)
        {
            var displays = await ResolveToolDisplayNamesAsync(new[] { toolIdForDisplay }, ct);
            displays.TryGetValue(toolIdForDisplay, out toolDisplayName);
        }

        // Snapshot calibration display = "{Date:yyyy-MM-dd} • SN {Serial}"
        string? snapshotDisplay = null;
        if (row.SnapshotDate is { } d && row.SnapshotSerialNumber is { } sn)
            snapshotDisplay = $"{d.UtcDateTime:yyyy-MM-dd} • SN {sn}";

        return Ok(new RunDetailDto(
            row.Id, row.JobId, row.Name, row.Description,
            row.TypeName, row.StatusName,
            row.StartDepth, row.EndDepth,
            row.StartTimestamp, row.EndTimestamp,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            row.BridleLength, row.CurrentInjection,
            row.ToolId, toolDisplayName,
            row.SnapshotCalibrationId, row.SnapshotDate, snapshotDisplay,
            row.BTotal, row.Dip, row.Declination,
            row.OperatorNames,
            row.LogCount, row.ShotCount,
            row.HasPassiveBinary, row.PassiveBinaryName, row.PassiveBinaryUploadedAt,
            row.PassiveConfigJson, row.PassiveConfigUpdatedAt,
            row.PassiveResultJson, row.PassiveResultComputedAt, row.PassiveResultMardukVersion,
            row.PassiveResultStatus, row.PassiveResultError,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<RunDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId,
        [FromBody] CreateRunDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<RunType>(dto.Type, out var runType))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateRunDto.Type)] = [SmartEnumExtensions.UnknownNameMessage<RunType>(dto.Type)],
            });

        // ToolId is optional; reject up-front if it's set but doesn't
        // resolve in the master fleet.
        if (await this.ValidateToolIdAsync(master, dto.ToolId, ct) is { } badTool)
            return badTool;

        await using var db = dbFactory.CreateActive();
        if (!await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, ct))
            return this.NotFoundProblem("Job", jobId.ToString());

        // Per-run Magnetics row — required, manually entered. WellId
        // null distinguishes per-run rows from the well-canonical and
        // legacy per-shot-lookup uses of this table.
        var magnetics = new Magnetics(
            bTotal:      dto.BTotalNanoTesla,
            dip:         dto.DipDegrees,
            declination: dto.DeclinationDegrees);

        var run = new Run(dto.Name, dto.Description, dto.StartDepth, dto.EndDepth, runType)
        {
            JobId            = jobId,
            StartTimestamp   = dto.StartTimestamp,
            EndTimestamp     = dto.EndTimestamp,
            // Gradient-only fields ignored on non-Gradient runs to keep
            // the post-create state coherent regardless of which fields
            // a chatty client sent.
            BridleLength     = runType == RunType.Gradient ? dto.BridleLength     : null,
            CurrentInjection = runType == RunType.Gradient ? dto.CurrentInjection : null,
            // Tool only meaningful on Gradient/Rotary; Passive runs
            // don't process via Marduk's calibration pipeline.
            ToolId           = runType == RunType.Passive  ? null : dto.ToolId,
            Magnetics        = magnetics,    // EF wires MagneticsId on save
        };

        // If a tool was assigned at creation, snapshot its calibration
        // now so Shots / Logs can be added immediately afterward.
        if (run.ToolId is { } toolId)
        {
            var snapResult = await snapshotter.EnsureSnapshotAsync(db, toolId, ct);
            if (TranslateSnapshotFailure(snapResult) is { } snapError) return snapError;
            ApplySnapshot(run, snapResult);
        }

        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId = run.Id },
            await ToDetailAsync(run, ct));
    }

    // ---------- update ----------

    [HttpPut("{runId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId,
        Guid runId,
        [FromBody] UpdateRunDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs
            .Include(r => r.Magnetics)
            .FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        // Terminal lifecycle states are read-only on the content side.
        // Editing Cancelled or Completed runs would falsify the
        // historical record; same rationale as Archived jobs.
        if (run.Status == RunStatus.Completed || run.Status == RunStatus.Cancelled)
            return this.ConflictProblem(
                $"Runs in {run.Status.Name} status are read-only. Cannot edit; archive or delete instead.");

        if (this.ApplyClientRowVersion(db, run, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        // Validate the requested ToolId (unless cleared).
        if (await this.ValidateToolIdAsync(master, dto.ToolId, ct) is { } badTool)
            return badTool;

        // Tool-assignment guard: clearing a tool on a run that already
        // has shots / logs would orphan their snapshot references.
        // Operators have to delete or move the captures first.
        if (dto.ToolId is null && run.ToolId is not null)
        {
            var hasCaptures = await db.Shots.AsNoTracking().AnyAsync(s => s.RunId == runId, ct)
                           || await db.Logs.AsNoTracking().AnyAsync(l => l.RunId == runId, ct);
            if (hasCaptures)
                return this.ConflictProblem(
                    "Cannot clear the tool on a run that already has shots or logs. " +
                    "Delete the existing captures first, or assign a different tool.");
        }

        run.Name             = dto.Name;
        run.Description      = dto.Description;
        run.StartDepth       = dto.StartDepth;
        run.EndDepth         = dto.EndDepth;
        run.StartTimestamp   = dto.StartTimestamp;
        run.EndTimestamp     = dto.EndTimestamp;
        // Same Gradient-only filter as Create: a non-Gradient run that
        // somehow accumulates a value here keeps null on the entity.
        run.BridleLength     = run.Type == RunType.Gradient ? dto.BridleLength     : null;
        run.CurrentInjection = run.Type == RunType.Gradient ? dto.CurrentInjection : null;

        // Magnetics — write straight through to the existing row. The
        // optimistic-concurrency check on Run.RowVersion already guards
        // the surface; Magnetics doesn't need its own conflict check
        // since the form posts both as one transactional unit.
        run.Magnetics!.BTotal      = dto.BTotalNanoTesla;
        run.Magnetics.Dip          = dto.DipDegrees;
        run.Magnetics.Declination  = dto.DeclinationDegrees;

        // Tool change → re-snapshot. New tool: insert a fresh snapshot.
        // Same tool: idempotent. Cleared tool: clear the snapshot id
        // (existing snapshot row stays in place for any historical
        // shots that still reference it).
        var newToolId = run.Type == RunType.Passive ? null : dto.ToolId;
        if (newToolId != run.ToolId)
        {
            run.ToolId = newToolId;
            run.SnapshotCalibrationId = null;
            run.SnapshotCalibration   = null;

            if (newToolId is { } toolId)
            {
                var snapResult = await snapshotter.EnsureSnapshotAsync(db, toolId, ct);
                if (TranslateSnapshotFailure(snapResult) is { } snapError) return snapError;
                ApplySnapshot(run, snapResult);
            }
        }

        if (await db.SaveOrConflictAsync(this, "Run", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // ---------- run-scoped calibration list (for Shot/Log dropdowns) ----------

    /// <summary>
    /// Returns the tenant-side calibration snapshots that exist for
    /// this run's tool. Used by <c>ShotEdit</c> / <c>LogEdit</c> to
    /// populate the calibration dropdown — there's exactly one
    /// snapshot per (run.ToolId, masterCalId) pair, so today the list
    /// is normally a single row (the run's
    /// <see cref="Run.SnapshotCalibrationId"/>). Future re-snapshot
    /// flows (operator opts a run into a newer master cal) will land
    /// additional rows here without endpoint changes.
    /// </summary>
    [HttpGet("{runId:guid}/calibrations")]
    [ProducesResponseType<IEnumerable<RunCalibrationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListRunCalibrations(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs
            .AsNoTracking()
            .Where(r => r.Id == runId && r.JobId == jobId)
            .Select(r => new { r.ToolId })
            .FirstOrDefaultAsync(ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        // Tool-less run = no calibrations available. The page renders
        // an empty list + a hint to assign a tool.
        if (run.ToolId is null)
            return Ok(Array.Empty<RunCalibrationDto>());

        var rows = await db.Calibrations
            .AsNoTracking()
            .Where(c => c.ToolId == run.ToolId)
            .OrderByDescending(c => c.CalibrationDate)
            .Select(c => new RunCalibrationDto(
                c.Id,
                c.CalibrationDate,
                c.SerialNumber,
                $"{c.CalibrationDate.UtcDateTime:yyyy-MM-dd} • SN {c.SerialNumber}",
                c.IsNominal))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ---------- lifecycle transitions ----------
    // Each endpoint is a thin delegate to TransitionAsync. Adding a new
    // transition (e.g. "Restart" from Suspended) means: update RunLifecycle,
    // copy one of these methods, point it at the new target. Nothing else
    // changes — Blazor's button rendering reads from the same map.

    [HttpPost("{runId:guid}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Start(Guid jobId, Guid runId, [FromBody] LifecycleTransitionDto dto, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Active, dto.RowVersion, ct);

    [HttpPost("{runId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Suspend(Guid jobId, Guid runId, [FromBody] LifecycleTransitionDto dto, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Suspended, dto.RowVersion, ct);

    [HttpPost("{runId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Complete(Guid jobId, Guid runId, [FromBody] LifecycleTransitionDto dto, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Completed, dto.RowVersion, ct);

    [HttpPost("{runId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Cancel(Guid jobId, Guid runId, [FromBody] LifecycleTransitionDto dto, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Cancelled, dto.RowVersion, ct);

    /// <summary>
    /// Restore a previously-archived run. Clears <c>ArchivedAt</c>;
    /// the run reappears in <see cref="List"/> with whatever lifecycle
    /// status it had when archived. 404 if the run doesn't exist;
    /// 204 if it exists but isn't archived (idempotent — same
    /// shape as <c>WellsController.Restore</c>).
    /// </summary>
    [HttpPost("{runId:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(
        Guid jobId, Guid runId,
        [FromBody] LifecycleTransitionDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        if (this.ApplyClientRowVersion(db, run, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        if (run.ArchivedAt is null) return NoContent(); // already active — idempotent

        run.ArchivedAt = null;
        if (await db.SaveOrConflictAsync(this, "Run", ct) is { } conflict)
            return conflict;
        return NoContent();
    }

    // ---------- delete (soft-archive) ----------

    /// <summary>
    /// Soft-archive a run. Sets <c>ArchivedAt</c> rather than removing
    /// the row, so the run disappears from default views (via the
    /// global query filter) but stays in the DB for audit and the
    /// <see cref="Restore"/> endpoint. Same semantic as
    /// <c>WellsController.Delete</c> — non-destructive; child Logs
    /// remain intact.
    ///
    /// <para>
    /// The lookup goes through the default query filter (no
    /// IgnoreQueryFilters) so an already-archived run surfaces as
    /// 404 to a user holding a stale URL — same shape they'd see
    /// for a never-existed run. Restore lives on its own endpoint
    /// that explicitly bypasses the filter.
    /// </para>
    /// </summary>
    [HttpDelete("{runId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs
            .FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        run.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- passive binary (Passive runs only) ----------
    //
    // Passive runs don't have Shots — captured data, processing
    // config, and Marduk's result attach directly to the Run row
    // through the Passive* columns. Mirrors the Shot binary +
    // config endpoints in shape and 250 KB cap.

    [HttpPost("{runId:guid}/passive/binary")]
    [RequestTimeout("LongRunning")]
    [RequestSizeLimit(MaxPassiveBinaryBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadPassiveBinary(
        Guid jobId, Guid runId,
        IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A non-empty binary file is required."],
            });

        if (file.Length > MaxPassiveBinaryBytes)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [$"Binary exceeds the {MaxPassiveBinaryBytes:N0}-byte limit."],
            });

        await using var db = dbFactory.CreateActive();
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());
        if (run.Type != RunType.Passive)
            return this.ConflictProblem(
                $"Passive binary is only valid on Passive runs; this run is {run.Type.Name}.");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        run.PassiveBinary = ms.ToArray();
        run.PassiveBinaryName = file.FileName;
        run.PassiveBinaryUploadedAt = DateTimeOffset.UtcNow;
        run.PassiveResultJson = null;
        run.PassiveResultComputedAt = null;
        run.PassiveResultError = null;
        run.PassiveResultStatus = "Pending";

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{runId:guid}/passive/binary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadPassiveBinary(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var row = await db.Runs
            .AsNoTracking()
            .Where(r => r.Id == runId && r.JobId == jobId)
            .Select(r => new { r.PassiveBinary, r.PassiveBinaryName })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.PassiveBinary is null)
            return this.NotFoundProblem("Passive binary", runId.ToString());

        return File(row.PassiveBinary, "application/octet-stream",
            row.PassiveBinaryName ?? $"run-{runId:N}.passive.bin");
    }

    [HttpDelete("{runId:guid}/passive/binary")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePassiveBinary(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());
        if (run.Type != RunType.Passive)
            return this.ConflictProblem(
                $"Passive binary is only valid on Passive runs; this run is {run.Type.Name}.");

        run.PassiveBinary = null;
        run.PassiveBinaryName = null;
        run.PassiveBinaryUploadedAt = null;
        run.PassiveResultJson = null;
        run.PassiveResultComputedAt = null;
        run.PassiveResultStatus = null;
        run.PassiveResultError = null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{runId:guid}/passive/config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetPassiveConfig(
        Guid jobId, Guid runId,
        [FromBody] string configJson, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());
        if (run.Type != RunType.Passive)
            return this.ConflictProblem(
                $"Passive config is only valid on Passive runs; this run is {run.Type.Name}.");

        run.PassiveConfigJson = configJson;
        run.PassiveConfigUpdatedAt = DateTimeOffset.UtcNow;
        run.PassiveResultJson = null;
        run.PassiveResultComputedAt = null;
        run.PassiveResultError = null;
        run.PassiveResultStatus = run.PassiveBinary is null ? null : "Pending";

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- helpers ----------

    /// <summary>
    /// Core of the lifecycle: look up the run, check the transition
    /// is allowed per <see cref="RunLifecycle"/>, apply, save.
    /// </summary>
    private async Task<IActionResult> TransitionAsync(
        Guid jobId, Guid runId, RunStatus target, string? rowVersion, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        if (this.ApplyClientRowVersion(db, run, rowVersion) is { } badRowVersion)
            return badRowVersion;

        if (run.Status == target) return NoContent();

        if (!RunLifecycle.CanTransition(run.Status, target))
            return this.ConflictProblem(
                $"Cannot transition run from {run.Status.Name} to {target.Name}.");

        run.Status = target;
        if (await db.SaveOrConflictAsync(this, "Run", ct) is { } conflict)
            return conflict;
        return NoContent();
    }

    /// <summary>
    /// Maps <see cref="CalibrationSnapshotResult"/> failure variants
    /// to controller responses. Returns null on success variants —
    /// the caller branches on those and stamps the run accordingly.
    /// </summary>
    private IActionResult? TranslateSnapshotFailure(CalibrationSnapshotResult result) => result switch
    {
        CalibrationSnapshotResult.ToolNotFound tnf =>
            this.ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["toolId"] = [$"Tool {tnf.ToolId} was not found in the master fleet registry."],
            })),
        CalibrationSnapshotResult.ToolHasNoCalibrations tnc =>
            this.ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["toolId"] = [
                    $"Tool {tnc.ToolId} has no calibrations on file in the master registry. " +
                    "Either pick a different tool or upload a calibration first.",
                ],
            })),
        _ => null,
    };

    /// <summary>
    /// Wires the snapshot result onto the run via the EF nav. EF
    /// translates either form (<c>Existing</c> reference, <c>Created</c>
    /// pending insert) into the right <c>SnapshotCalibrationId</c> on
    /// SaveChanges.
    /// </summary>
    private static void ApplySnapshot(Run run, CalibrationSnapshotResult result)
    {
        switch (result)
        {
            case CalibrationSnapshotResult.Existing existing:
                run.SnapshotCalibration   = existing.Snapshot;
                run.SnapshotCalibrationId = existing.Snapshot.Id;
                break;
            case CalibrationSnapshotResult.Created created:
                run.SnapshotCalibration = created.Snapshot;   // FK populated by EF on save
                break;
        }
    }

    /// <summary>
    /// Bulk-resolve master Tool display names for a list of tool ids.
    /// One master query; returns an in-memory dictionary the caller
    /// joins against. Empty list short-circuits to an empty dict so
    /// the no-tools-assigned path skips the master roundtrip.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveToolDisplayNamesAsync(
        IReadOnlyCollection<Guid> toolIds, CancellationToken ct)
    {
        if (toolIds.Count == 0)
            return new Dictionary<Guid, string>(0);

        var rows = await master.Tools
            .AsNoTracking()
            .Where(t => toolIds.Contains(t.Id))
            .Select(t => new { t.Id, t.SerialNumber, GenerationName = t.Generation.Name })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Id,
            r => ToolDisplay.Name(r.GenerationName, r.SerialNumber));
    }

    /// <summary>
    /// Map an in-memory <see cref="Run"/> to its detail DTO. Used by
    /// the Create response (entity is freshly inserted; no operators,
    /// logs, or shots yet). Resolves the tool display name from
    /// master if a tool was assigned at creation.
    /// </summary>
    private async Task<RunDetailDto> ToDetailAsync(Run r, CancellationToken ct)
    {
        string? toolDisplay = null;
        if (r.ToolId is { } toolId)
        {
            var displays = await ResolveToolDisplayNamesAsync(new[] { toolId }, ct);
            displays.TryGetValue(toolId, out toolDisplay);
        }

        string? snapshotDisplay = null;
        if (r.SnapshotCalibration is { } snap)
            snapshotDisplay = $"{snap.CalibrationDate.UtcDateTime:yyyy-MM-dd} • SN {snap.SerialNumber}";

        return new RunDetailDto(
            r.Id, r.JobId, r.Name, r.Description,
            r.Type.Name, r.Status.Name,
            r.StartDepth, r.EndDepth,
            r.StartTimestamp, r.EndTimestamp,
            r.CreatedAt, r.CreatedBy, r.UpdatedAt, r.UpdatedBy,
            r.BridleLength, r.CurrentInjection,
            r.ToolId, toolDisplay,
            r.SnapshotCalibrationId,
            r.SnapshotCalibration?.CalibrationDate,
            snapshotDisplay,
            r.Magnetics!.BTotal, r.Magnetics.Dip, r.Magnetics.Declination,
            OperatorNames: Array.Empty<string>(),
            LogCount: 0, ShotCount: 0,
            HasPassiveBinary: r.PassiveBinary is not null,
            r.PassiveBinaryName, r.PassiveBinaryUploadedAt,
            r.PassiveConfigJson, r.PassiveConfigUpdatedAt,
            r.PassiveResultJson, r.PassiveResultComputedAt, r.PassiveResultMardukVersion,
            r.PassiveResultStatus, r.PassiveResultError,
            r.EncodeRowVersion());
    }
}

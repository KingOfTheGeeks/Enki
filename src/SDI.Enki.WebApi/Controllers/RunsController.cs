using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Shared.Runs;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

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
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/runs")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class RunsController(ITenantDbContextFactory dbFactory) : ControllerBase
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

        // Two-stage projection: anonymous in EF (correlated LogCount
        // subquery + raw RowVersion bytes) + post-query map for the
        // base64 encode of RowVersion.
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
                r.ToolName,
                LogCount  = r.Logs.Count,
                ShotCount = r.Shots.Count,
                r.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new RunSummaryDto(
            r.Id, r.Name, r.Description,
            r.TypeName, r.StatusName,
            r.StartDepth, r.EndDepth,
            r.StartTimestamp, r.EndTimestamp,
            r.ToolName,
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
                r.ToolName,
                LogCount  = r.Logs.Count,
                ShotCount = r.Shots.Count,
                r.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new RunSummaryDto(
            r.Id, r.Name, r.Description,
            r.TypeName, r.StatusName,
            r.StartDepth, r.EndDepth,
            r.StartTimestamp, r.EndTimestamp,
            r.ToolName,
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
                r.ToolName,
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

        return Ok(new RunDetailDto(
            row.Id, row.JobId, row.Name, row.Description,
            row.TypeName, row.StatusName,
            row.StartDepth, row.EndDepth,
            row.StartTimestamp, row.EndTimestamp,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            row.BridleLength, row.CurrentInjection,
            row.ToolName,
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

        await using var db = dbFactory.CreateActive();
        if (!await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, ct))
            return this.NotFoundProblem("Job", jobId.ToString());

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
            // skip calibration entirely (stub).
            ToolName         = runType == RunType.Passive  ? null : dto.ToolName,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId = run.Id },
            ToDetail(run));
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
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        // Terminal lifecycle states are read-only on the content side.
        // Editing Cancelled or Completed runs would falsify the
        // historical record; same rationale as Archived jobs.
        if (run.Status == RunStatus.Completed || run.Status == RunStatus.Cancelled)
            return this.ConflictProblem(
                $"Runs in {run.Status.Name} status are read-only. Cannot edit; archive or delete instead.");

        if (this.ApplyClientRowVersion(run, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

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
        run.ToolName         = run.Type == RunType.Passive  ? null : dto.ToolName;

        if (await db.SaveOrConflictAsync(this, "Run", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // ---------- lifecycle transitions ----------
    // Each endpoint is a thin delegate to TransitionAsync. Adding a new
    // transition (e.g. "Restart" from Suspended) means: update RunLifecycle,
    // copy one of these methods, point it at the new target. Nothing else
    // changes — Blazor's button rendering reads from the same map.

    [HttpPost("{runId:guid}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Start(Guid jobId, Guid runId, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Active, ct);

    [HttpPost("{runId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Suspend(Guid jobId, Guid runId, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Suspended, ct);

    [HttpPost("{runId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Complete(Guid jobId, Guid runId, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Completed, ct);

    [HttpPost("{runId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Cancel(Guid jobId, Guid runId, CancellationToken ct) =>
        TransitionAsync(jobId, runId, RunStatus.Cancelled, ct);

    /// <summary>
    /// Restore a previously-archived run. Clears <c>ArchivedAt</c>;
    /// the run reappears in <see cref="List"/> with whatever lifecycle
    /// status it had when archived. 404 if the run doesn't exist;
    /// 204 if it exists but isn't archived (idempotent — same
    /// shape as <c>WellsController.Restore</c>).
    /// </summary>
    [HttpPost("{runId:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        if (run.ArchivedAt is null) return NoContent(); // already active — idempotent

        run.ArchivedAt = null;
        await db.SaveChangesAsync(ct);
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
    //
    // Every endpoint guards `Type == Passive` and returns 409
    // ConflictProblem on Gradient / Rotary runs — same as the
    // shot-side guard against running Passive flows on the wrong
    // run type. The calc seam is the same `ResultStatus = "Pending"`
    // flag the Shot endpoints flip; future Marduk service reads
    // `WHERE PassiveResultStatus = 'Pending'`.

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
        // Calc seam: clear prior result + flag pending.
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
        // Result is meaningless without the binary it derived from —
        // clear it so the next pipeline run starts clean.
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
        // Calc seam: config change invalidates prior result. Status
        // only flips to Pending if there's a binary on file (calc has
        // nothing to chew on otherwise) — matches Shot.SetConfig.
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
    /// Same-status is a no-op returning 204 — matches the idempotent
    /// pattern used on Tenant deactivate/reactivate and Job
    /// activate/archive.
    /// </summary>
    private async Task<IActionResult> TransitionAsync(
        Guid jobId, Guid runId, RunStatus target, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        if (run.Status == target) return NoContent();

        if (!RunLifecycle.CanTransition(run.Status, target))
            return this.ConflictProblem(
                $"Cannot transition run from {run.Status.Name} to {target.Name}.");

        run.Status = target;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Maps an in-memory <see cref="Run"/> to its detail DTO. Used by
    /// the Create response (entity is freshly inserted; no operators,
    /// logs, or shots yet) — Get's projection-based path avoids this
    /// helper so EF can translate the count subqueries to SQL.
    /// </summary>
    private static RunDetailDto ToDetail(Run r) => new(
        r.Id, r.JobId, r.Name, r.Description,
        r.Type.Name, r.Status.Name,
        r.StartDepth, r.EndDepth,
        r.StartTimestamp, r.EndTimestamp,
        r.CreatedAt, r.CreatedBy, r.UpdatedAt, r.UpdatedBy,
        r.BridleLength, r.CurrentInjection,
        r.ToolName,
        OperatorNames: Array.Empty<string>(),
        LogCount: 0, ShotCount: 0,
        HasPassiveBinary: r.PassiveBinary is not null,
        r.PassiveBinaryName, r.PassiveBinaryUploadedAt,
        r.PassiveConfigJson, r.PassiveConfigUpdatedAt,
        r.PassiveResultJson, r.PassiveResultComputedAt, r.PassiveResultMardukVersion,
        r.PassiveResultStatus, r.PassiveResultError,
        r.EncodeRowVersion());
}

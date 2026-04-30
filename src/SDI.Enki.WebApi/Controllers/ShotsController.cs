using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Comments;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Comments;
using SDI.Enki.Shared.Shots;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;
using SDI.Enki.WebApi.Validation;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Shots under a Run. Each Shot is one captured event (one
/// gradient-pulse-and-measure cycle for Gradient runs; one rotary
/// event for Rotary runs). Phase 2 reshape: Shot collapsed from a
/// 16-column structured row + per-sample children to a slim record
/// carrying a primary <c>Binary + Config + Result</c> set and an
/// optional gyro <c>Binary + Config + Result</c> companion.
///
/// <para>
/// Routes nest under Run:
/// <c>/tenants/{tenantCode}/jobs/{jobId:guid}/runs/{runId:guid}/shots</c>.
/// Every action confirms the parent (jobId, runId) pair belongs
/// together — same parent-pair guard pattern as
/// <c>LogsController</c> + <c>SurveysController</c>.
/// </para>
///
/// <para>
/// <b>Binary uploads:</b> primary and gyro have separate POST
/// endpoints. Primary capped at 250 KB; gyro capped at 10 KB. Any
/// successful upload (or config change) sets the corresponding
/// <c>ResultStatus</c> to <c>Pending</c> and clears the result
/// fields — that's the seam the future Marduk calc trigger reads.
/// </para>
///
/// <para>
/// <b>Comments subresource:</b>
/// <c>POST /shots/{id}/comments</c> + <c>GET /shots/{id}/comments</c>.
/// 1:N — was m:n with Gradient/Rotary/Passive in the legacy shape;
/// reparented to Shot in Phase 2.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/runs/{runId:guid}/shots")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ShotsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    /// <summary>≤ 250 KB primary capture binary. Enforced server-side.</summary>
    public const long MaxBinaryBytes = 250 * 1024;
    /// <summary>≤ 10 KB gyro capture binary. Enforced server-side.</summary>
    public const long MaxGyroBinaryBytes = 10 * 1024;

    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<ShotSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        // Two-stage projection: anonymous EF projection that exposes
        // HasBinary/HasGyroBinary as `Binary != null` server-side
        // (so the bytes never cross the wire), then post-query map to
        // the wire DTO with the base64 RowVersion encode.
        var rows = await db.Shots
            .AsNoTracking()
            .Where(s => s.RunId == runId)
            .OrderByDescending(s => s.FileTime)
            .Select(s => new
            {
                s.Id, s.RunId, s.ShotName, s.FileTime, s.CalibrationId,
                HasBinary     = s.Binary != null,
                HasGyroBinary = s.GyroBinary != null,
                s.ResultStatus, s.GyroResultStatus,
                s.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(s => new ShotSummaryDto(
            s.Id, s.RunId, s.ShotName, s.FileTime, s.CalibrationId,
            s.HasBinary, s.HasGyroBinary,
            s.ResultStatus, s.GyroResultStatus,
            ConcurrencyHelper.EncodeRowVersion(s.RowVersion))));
    }

    // ---------- detail ----------

    [HttpGet("{shotId:int}")]
    [ProducesResponseType<ShotDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, Guid runId, int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var row = await db.Shots
            .AsNoTracking()
            .Where(s => s.Id == shotId && s.RunId == runId)
            .Select(s => new
            {
                s.Id, s.RunId, s.ShotName, s.FileTime, s.CalibrationId,
                s.CreatedAt, s.CreatedBy, s.UpdatedAt, s.UpdatedBy,
                HasBinary = s.Binary != null,
                s.BinaryName, s.BinaryUploadedAt,
                s.ConfigJson, s.ConfigUpdatedAt,
                s.ResultJson, s.ResultComputedAt, s.ResultMardukVersion,
                s.ResultStatus, s.ResultError,
                HasGyroBinary = s.GyroBinary != null,
                s.GyroBinaryName, s.GyroBinaryUploadedAt,
                s.GyroConfigJson, s.GyroConfigUpdatedAt,
                s.GyroResultJson, s.GyroResultComputedAt, s.GyroResultMardukVersion,
                s.GyroResultStatus, s.GyroResultError,
                s.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Shot", shotId.ToString());

        return Ok(new ShotDetailDto(
            row.Id, row.RunId, row.ShotName, row.FileTime, row.CalibrationId,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            row.HasBinary, row.BinaryName, row.BinaryUploadedAt,
            row.ConfigJson, row.ConfigUpdatedAt,
            row.ResultJson, row.ResultComputedAt, row.ResultMardukVersion,
            row.ResultStatus, row.ResultError,
            row.HasGyroBinary, row.GyroBinaryName, row.GyroBinaryUploadedAt,
            row.GyroConfigJson, row.GyroConfigUpdatedAt,
            row.GyroResultJson, row.GyroResultComputedAt, row.GyroResultMardukVersion,
            row.GyroResultStatus, row.GyroResultError,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<ShotDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId, Guid runId,
        [FromBody] CreateShotDto dto, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        // Need to know the run's ToolId + SnapshotCalibrationId before
        // creating: a tool-less run can't carry shots (Marduk has
        // nothing to process them with), and the run's snapshot is
        // the default calibration for the new shot.
        var run = await db.Runs
            .AsNoTracking()
            .Where(r => r.Id == runId && r.JobId == jobId)
            .Select(r => new { r.Id, r.ToolId, r.SnapshotCalibrationId })
            .FirstOrDefaultAsync(ct);
        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        // Tool-required guard for issue #26 follow-up: shots can only
        // be added once the run has a tool assigned. RunsController
        // surfaces this as an "Assign tool" CTA on the run detail.
        if (run.ToolId is null)
            return this.ConflictProblem(
                "Cannot add a shot to a run with no tool assigned. " +
                "Assign a tool on the run detail page first.");

        // Identity only — the processing config is a typed Marduk
        // class populated server-side. Calibration defaults to the
        // run's snapshot; operators can override on Edit.
        var shot = new Shot
        {
            RunId = runId,
            ShotName = dto.ShotName,
            FileTime = dto.FileTime,
            CalibrationId = run.SnapshotCalibrationId,
        };
        db.Shots.Add(shot);

        // Defence-in-depth — see LogsController.Create for the full
        // rationale. Translates SQL FK / unique violations to a clean
        // 409 instead of bubbling 500.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbEx) when (this.TryTranslate(dbEx) is { } problem)
        {
            return problem;
        }

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId, shotId = shot.Id },
            await ToDetailAsync(db, shot.Id, ct));
    }

    // ---------- update (identity columns only) ----------

    [HttpPut("{shotId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId, Guid runId, int shotId,
        [FromBody] UpdateShotDto dto, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        if (this.ApplyClientRowVersion(db, shot, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        // Soft-FK guard for issue #26: a CalibrationId that doesn't
        // resolve in this tenant would otherwise hit SaveChanges and
        // raise DbUpdateException (SQL FK constraint 547) → 500 to the
        // user. Reject upfront with a clean field-level 400.
        if (await this.ValidateCalibrationIdAsync(db, dto.CalibrationId, ct) is { } badCal)
            return badCal;

        shot.ShotName = dto.ShotName;
        shot.FileTime = dto.FileTime;
        shot.CalibrationId = dto.CalibrationId;

        if (await db.SaveOrConflictAsync(this, "Shot", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // ---------- binary upload (primary) ----------

    [HttpPost("{shotId:int}/binary")]
    [RequestTimeout("LongRunning")]
    [RequestSizeLimit(MaxBinaryBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadBinary(
        Guid jobId, Guid runId, int shotId,
        IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A non-empty binary file is required."],
            });

        if (file.Length > MaxBinaryBytes)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [$"Binary exceeds the {MaxBinaryBytes:N0}-byte limit."],
            });

        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        // Read into memory — capped at 250 KB so this is safe.
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        shot.Binary = ms.ToArray();
        shot.BinaryName = file.FileName;
        shot.BinaryUploadedAt = DateTimeOffset.UtcNow;
        // Calc seam: clear prior result + flag pending so the future
        // Marduk service knows there's work.
        shot.ResultJson = null;
        shot.ResultComputedAt = null;
        shot.ResultError = null;
        shot.ResultStatus = "Pending";

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{shotId:int}/binary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadBinary(Guid jobId, Guid runId, int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var row = await db.Shots
            .AsNoTracking()
            .Where(s => s.Id == shotId && s.RunId == runId)
            .Select(s => new { s.Binary, s.BinaryName })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.Binary is null)
            return this.NotFoundProblem("Shot binary", shotId.ToString());

        return File(row.Binary, "application/octet-stream", row.BinaryName ?? $"shot-{shotId}.bin");
    }

    [HttpDelete("{shotId:int}/binary")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBinary(Guid jobId, Guid runId, int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        shot.Binary = null;
        shot.BinaryName = null;
        shot.BinaryUploadedAt = null;
        shot.ResultJson = null;
        shot.ResultComputedAt = null;
        shot.ResultStatus = null;
        shot.ResultError = null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- gyro binary upload ----------

    [HttpPost("{shotId:int}/gyro-binary")]
    [RequestSizeLimit(MaxGyroBinaryBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadGyroBinary(
        Guid jobId, Guid runId, int shotId,
        IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A non-empty gyro binary file is required."],
            });

        if (file.Length > MaxGyroBinaryBytes)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [$"Gyro binary exceeds the {MaxGyroBinaryBytes:N0}-byte limit."],
            });

        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        // Domain rule: gyro binary only meaningful when the primary
        // is present.
        if (shot.Binary is null)
            return this.ConflictProblem(
                "Cannot upload a gyro binary before the primary binary. " +
                "Upload the primary first.");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        shot.GyroBinary = ms.ToArray();
        shot.GyroBinaryName = file.FileName;
        shot.GyroBinaryUploadedAt = DateTimeOffset.UtcNow;
        shot.GyroResultJson = null;
        shot.GyroResultComputedAt = null;
        shot.GyroResultError = null;
        shot.GyroResultStatus = "Pending";

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{shotId:int}/gyro-binary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadGyroBinary(Guid jobId, Guid runId, int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var row = await db.Shots
            .AsNoTracking()
            .Where(s => s.Id == shotId && s.RunId == runId)
            .Select(s => new { s.GyroBinary, s.GyroBinaryName })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.GyroBinary is null)
            return this.NotFoundProblem("Shot gyro binary", shotId.ToString());

        return File(row.GyroBinary, "application/octet-stream", row.GyroBinaryName ?? $"shot-{shotId}.gyro.bin");
    }

    [HttpDelete("{shotId:int}/gyro-binary")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGyroBinary(Guid jobId, Guid runId, int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        shot.GyroBinary = null;
        shot.GyroBinaryName = null;
        shot.GyroBinaryUploadedAt = null;
        shot.GyroResultJson = null;
        shot.GyroResultComputedAt = null;
        shot.GyroResultStatus = null;
        shot.GyroResultError = null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- config (primary + gyro) ----------

    [HttpPut("{shotId:int}/config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetConfig(
        Guid jobId, Guid runId, int shotId,
        [FromBody] string configJson, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        shot.ConfigJson = configJson;
        shot.ConfigUpdatedAt = DateTimeOffset.UtcNow;
        // Calc seam: config change invalidates any prior result.
        shot.ResultJson = null;
        shot.ResultComputedAt = null;
        shot.ResultError = null;
        shot.ResultStatus = shot.Binary is null ? null : "Pending";

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Import a pre-computed result for a shot. Used during the
    /// upload flow when the user already has a Marduk-shaped
    /// solution (<c>GradientSolution</c> /
    /// <c>RotatingDipoleSolution</c>) from prior processing — the
    /// binary may still be uploaded for archival, but there's
    /// nothing for the calc service to redo. Sets
    /// <c>ResultStatus = Success</c> and stamps a sentinel marduk
    /// version so downstream consumers can tell imported results
    /// from server-computed ones.
    ///
    /// <para>Distinct from <see cref="SetConfig"/>: that flips
    /// status to Pending; this is the terminal "already done"
    /// state. A subsequent binary upload or config change will
    /// reset status to Pending and supersede this import.</para>
    /// </summary>
    [HttpPut("{shotId:int}/result")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetResult(
        Guid jobId, Guid runId, int shotId,
        [FromBody] string resultJson, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        shot.ResultJson = resultJson;
        shot.ResultComputedAt = DateTimeOffset.UtcNow;
        shot.ResultMardukVersion = "imported";
        shot.ResultStatus = "Success";
        shot.ResultError = null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{shotId:int}/gyro-config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetGyroConfig(
        Guid jobId, Guid runId, int shotId,
        [FromBody] string gyroConfigJson, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        shot.GyroConfigJson = gyroConfigJson;
        shot.GyroConfigUpdatedAt = DateTimeOffset.UtcNow;
        shot.GyroResultJson = null;
        shot.GyroResultComputedAt = null;
        shot.GyroResultError = null;
        shot.GyroResultStatus = shot.GyroBinary is null ? null : "Pending";

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- delete ----------

    [HttpDelete("{shotId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid jobId, Guid runId, int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == shotId && s.RunId == runId, ct);
        if (shot is null) return this.NotFoundProblem("Shot", shotId.ToString());

        db.Shots.Remove(shot);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- comments subresource ----------

    [HttpGet("{shotId:int}/comments")]
    [ProducesResponseType<IEnumerable<CommentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListComments(Guid jobId, Guid runId, int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());
        if (!await db.Shots.AsNoTracking().AnyAsync(s => s.Id == shotId && s.RunId == runId, ct))
            return this.NotFoundProblem("Shot", shotId.ToString());

        var rows = await db.Comments
            .AsNoTracking()
            .Where(c => c.ShotId == shotId)
            .OrderByDescending(c => c.Timestamp)
            .Select(c => new CommentDto(
                c.Id, c.ShotId, c.Text, c.User, c.Identity, c.Timestamp))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPost("{shotId:int}/comments")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(
        Guid jobId, Guid runId, int shotId,
        [FromBody] CreateCommentDto dto, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());
        if (!await db.Shots.AsNoTracking().AnyAsync(s => s.Id == shotId && s.RunId == runId, ct))
            return this.NotFoundProblem("Shot", shotId.ToString());

        var user = User?.Identity?.Name ?? "system";
        var comment = new Comment(shotId, dto.Text, user);
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(ListComments),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId, shotId },
            new CommentDto(comment.Id, comment.ShotId, comment.Text, comment.User, comment.Identity, comment.Timestamp));
    }

    // ---------- helpers ----------

    /// <summary>Confirms (jobId, runId) belongs together. Same shape as LogsController.RunExistsAsync.</summary>
    private static Task<bool> RunExistsAsync(
        TenantDbContext db, Guid jobId, Guid runId, CancellationToken ct) =>
        db.Runs.AsNoTracking().AnyAsync(r => r.Id == runId && r.JobId == jobId, ct);

    /// <summary>Map a freshly-saved Shot to its detail DTO via a projection round-trip.</summary>
    private static async Task<ShotDetailDto> ToDetailAsync(TenantDbContext db, int shotId, CancellationToken ct)
    {
        var row = await db.Shots
            .AsNoTracking()
            .Where(s => s.Id == shotId)
            .Select(s => new
            {
                s.Id, s.RunId, s.ShotName, s.FileTime, s.CalibrationId,
                s.CreatedAt, s.CreatedBy, s.UpdatedAt, s.UpdatedBy,
                HasBinary = s.Binary != null,
                s.BinaryName, s.BinaryUploadedAt,
                s.ConfigJson, s.ConfigUpdatedAt,
                s.ResultJson, s.ResultComputedAt, s.ResultMardukVersion,
                s.ResultStatus, s.ResultError,
                HasGyroBinary = s.GyroBinary != null,
                s.GyroBinaryName, s.GyroBinaryUploadedAt,
                s.GyroConfigJson, s.GyroConfigUpdatedAt,
                s.GyroResultJson, s.GyroResultComputedAt, s.GyroResultMardukVersion,
                s.GyroResultStatus, s.GyroResultError,
                s.RowVersion,
            })
            .FirstAsync(ct);

        return new ShotDetailDto(
            row.Id, row.RunId, row.ShotName, row.FileTime, row.CalibrationId,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            row.HasBinary, row.BinaryName, row.BinaryUploadedAt,
            row.ConfigJson, row.ConfigUpdatedAt,
            row.ResultJson, row.ResultComputedAt, row.ResultMardukVersion,
            row.ResultStatus, row.ResultError,
            row.HasGyroBinary, row.GyroBinaryName, row.GyroBinaryUploadedAt,
            row.GyroConfigJson, row.GyroConfigUpdatedAt,
            row.GyroResultJson, row.GyroResultComputedAt, row.GyroResultMardukVersion,
            row.GyroResultStatus, row.GyroResultError,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion));
    }
}

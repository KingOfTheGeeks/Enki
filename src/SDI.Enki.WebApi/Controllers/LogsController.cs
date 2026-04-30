using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
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
/// Logs under a Run. Phase 2 reshape: the legacy 10-entity Log
/// family (LogSample / LogTimeWindow / etc. — all pre-Marduk
/// MATLAB-era artifacts) collapsed into a slim Log entity carrying
/// a captured Binary + JSON config + a 1:N collection of result
/// files (LAS or similar). Marduk consumes (Binary, Config,
/// Calibration) and produces the result files server-side.
///
/// <para>
/// Routes:
/// <c>/tenants/{tenantCode}/jobs/{jobId:guid}/runs/{runId:guid}/logs</c>
/// — same parent-pair guard as Shots / Surveys.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/runs/{runId:guid}/logs")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class LogsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    /// <summary>≤ 250 KB binary capture file. Same cap as Shot.Binary.</summary>
    public const long MaxBinaryBytes = 250 * 1024;

    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<LogSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var rows = await db.Logs
            .AsNoTracking()
            .Where(l => l.RunId == runId)
            .OrderByDescending(l => l.FileTime)
            .Select(l => new
            {
                l.Id, l.RunId, l.ShotName, l.FileTime, l.CalibrationId,
                HasBinary       = l.Binary != null,
                ResultFileCount = l.ResultFiles.Count,
                l.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(l => new LogSummaryDto(
            l.Id, l.RunId, l.ShotName, l.FileTime, l.CalibrationId,
            l.HasBinary, l.ResultFileCount,
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
                l.Id, l.RunId, l.ShotName, l.FileTime, l.CalibrationId,
                l.CreatedAt, l.CreatedBy, l.UpdatedAt, l.UpdatedBy,
                HasBinary = l.Binary != null,
                l.BinaryName, l.BinaryUploadedAt,
                l.ConfigJson, l.ConfigUpdatedAt,
                ResultFiles = l.ResultFiles
                    .Select(f => new LogResultFileDto(f.Id, f.LogId, f.FileName, f.ContentType, f.CreatedAt))
                    .ToList(),
                l.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Log", logId.ToString());

        return Ok(new LogDetailDto(
            row.Id, row.RunId, row.ShotName, row.FileTime, row.CalibrationId,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            row.HasBinary, row.BinaryName, row.BinaryUploadedAt,
            row.ConfigJson, row.ConfigUpdatedAt,
            row.ResultFiles,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<LogDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId, Guid runId,
        [FromBody] CreateLogDto dto, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var log = new Log(runId, dto.ShotName, dto.FileTime)
        {
            CalibrationId = dto.CalibrationId,
        };
        db.Logs.Add(log);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId, logId = log.Id },
            new LogDetailDto(
                log.Id, log.RunId, log.ShotName, log.FileTime, log.CalibrationId,
                log.CreatedAt, log.CreatedBy, log.UpdatedAt, log.UpdatedBy,
                HasBinary: false, BinaryName: null, BinaryUploadedAt: null,
                ConfigJson: null, ConfigUpdatedAt: null,
                ResultFiles: Array.Empty<LogResultFileDto>(),
                ConcurrencyHelper.EncodeRowVersion(log.RowVersion)));
    }

    // ---------- update (identity columns only) ----------

    [HttpPut("{logId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId, Guid runId, int logId,
        [FromBody] UpdateLogDto dto, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var log = await db.Logs.FirstOrDefaultAsync(l => l.Id == logId && l.RunId == runId, ct);
        if (log is null) return this.NotFoundProblem("Log", logId.ToString());

        if (this.ApplyClientRowVersion(db, log, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        log.ShotName      = dto.ShotName;
        log.FileTime      = dto.FileTime;
        log.CalibrationId = dto.CalibrationId;

        if (await db.SaveOrConflictAsync(this, "Log", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // ---------- binary upload / download ----------

    [HttpPost("{logId:int}/binary")]
    [RequestTimeout("LongRunning")]
    [RequestSizeLimit(MaxBinaryBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadBinary(
        Guid jobId, Guid runId, int logId,
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

        var log = await db.Logs.FirstOrDefaultAsync(l => l.Id == logId && l.RunId == runId, ct);
        if (log is null) return this.NotFoundProblem("Log", logId.ToString());

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        log.Binary = ms.ToArray();
        log.BinaryName = file.FileName;
        log.BinaryUploadedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{logId:int}/binary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadBinary(Guid jobId, Guid runId, int logId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var row = await db.Logs
            .AsNoTracking()
            .Where(l => l.Id == logId && l.RunId == runId)
            .Select(l => new { l.Binary, l.BinaryName })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.Binary is null)
            return this.NotFoundProblem("Log binary", logId.ToString());

        return File(row.Binary, "application/octet-stream", row.BinaryName ?? $"log-{logId}.bin");
    }

    // ---------- config ----------

    [HttpPut("{logId:int}/config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetConfig(
        Guid jobId, Guid runId, int logId,
        [FromBody] string configJson, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var log = await db.Logs.FirstOrDefaultAsync(l => l.Id == logId && l.RunId == runId, ct);
        if (log is null) return this.NotFoundProblem("Log", logId.ToString());

        log.ConfigJson = configJson;
        log.ConfigUpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- result files ----------

    /// <summary>
    /// Upload a Marduk-produced output file (typically a LAS) to a
    /// Log. Multi-file: a Log can have many result files. The future
    /// calc service will append these once it lands; this endpoint
    /// is exposed up-front so the calc seam has a write target.
    /// </summary>
    [HttpPost("{logId:int}/result-files")]
    [RequestSizeLimit(MaxBinaryBytes)]
    [ProducesResponseType<LogResultFileDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadResultFile(
        Guid jobId, Guid runId, int logId,
        IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A non-empty result file is required."],
            });

        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());
        if (!await db.Logs.AsNoTracking().AnyAsync(l => l.Id == logId && l.RunId == runId, ct))
            return this.NotFoundProblem("Log", logId.ToString());

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var rf = new LogResultFile
        {
            LogId = logId,
            FileName = file.FileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Bytes = ms.ToArray(),
        };
        db.LogResultFiles.Add(rf);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(DownloadResultFile),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId, logId, fileId = rf.Id },
            new LogResultFileDto(rf.Id, rf.LogId, rf.FileName, rf.ContentType, rf.CreatedAt));
    }

    [HttpGet("{logId:int}/result-files/{fileId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadResultFile(
        Guid jobId, Guid runId, int logId, int fileId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var row = await db.LogResultFiles
            .AsNoTracking()
            .Where(f => f.Id == fileId && f.LogId == logId)
            .Select(f => new { f.Bytes, f.FileName, f.ContentType })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.Bytes is null)
            return this.NotFoundProblem("Log result file", fileId.ToString());

        return File(row.Bytes, row.ContentType, row.FileName);
    }

    [HttpDelete("{logId:int}/result-files/{fileId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteResultFile(
        Guid jobId, Guid runId, int logId, int fileId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await RunExistsAsync(db, jobId, runId, ct))
            return this.NotFoundProblem("Run", runId.ToString());

        var rf = await db.LogResultFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.LogId == logId, ct);
        if (rf is null) return this.NotFoundProblem("Log result file", fileId.ToString());

        db.LogResultFiles.Remove(rf);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- delete ----------

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

    private static Task<bool> RunExistsAsync(
        TenantDbContext db, Guid jobId, Guid runId, CancellationToken ct) =>
        db.Runs.AsNoTracking().AnyAsync(r => r.Id == runId && r.JobId == jobId, ct);
}

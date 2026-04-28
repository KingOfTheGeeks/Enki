using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations;
using SDI.Enki.Shared.Tools;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Master-level Tool registry endpoints. Tools are fleet-wide — a tool
/// serves many tenants across its lifetime, so this controller is not
/// tenant-scoped. Anyone with a valid enki-scope token can read; mutating
/// endpoints (Phase 2) will tighten to <c>EnkiAdminOnly</c>.
///
/// Routes use the tool's <c>SerialNumber</c> rather than the GUID Id —
/// operators know the serial, that's what they'll type in the URL.
/// </summary>
[ApiController]
[Route("tools")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class ToolsController(EnkiMasterDbContext master) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<ToolSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<ToolSummaryDto>> List(
        [FromQuery] string? status,
        [FromQuery] string? generation,
        CancellationToken ct)
    {
        var rows = await master.Tools
            .AsNoTracking()
            .OrderBy(t => t.SerialNumber)
            .Select(t => new
            {
                t.Id,
                t.SerialNumber,
                t.FirmwareVersion,
                GenerationName = t.Generation.Name,
                StatusName = t.Status.Name,
                t.MagnetometerCount,
                t.AccelerometerCount,
                CalibrationCount = t.Calibrations.Count,
                LatestCalibrationDate = (DateTimeOffset?)t.Calibrations
                    .OrderByDescending(c => c.CalibrationDate)
                    .Select(c => c.CalibrationDate)
                    .FirstOrDefault(),
                t.CreatedAt,
                t.RowVersion,
            })
            .ToListAsync(ct);

        var filtered = rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status))
            filtered = filtered.Where(r => string.Equals(r.StatusName, status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(generation))
            filtered = filtered.Where(r => string.Equals(r.GenerationName, generation, StringComparison.OrdinalIgnoreCase));

        return filtered.Select(t => new ToolSummaryDto(
            t.Id,
            t.SerialNumber,
            ToolDisplay.Name(t.GenerationName, t.SerialNumber),
            t.FirmwareVersion,
            t.GenerationName,
            t.StatusName,
            t.MagnetometerCount,
            t.AccelerometerCount,
            t.CalibrationCount,
            t.LatestCalibrationDate == default ? null : t.LatestCalibrationDate,
            t.CreatedAt,
            ConcurrencyHelper.EncodeRowVersion(t.RowVersion)));
    }

    // ---------- detail ----------

    [HttpGet("{serial:int}")]
    [ProducesResponseType<ToolDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int serial, CancellationToken ct)
    {
        var row = await master.Tools
            .AsNoTracking()
            .Where(t => t.SerialNumber == serial)
            .Select(t => new
            {
                t.Id,
                t.SerialNumber,
                t.FirmwareVersion,
                GenerationName = t.Generation.Name,
                StatusName = t.Status.Name,
                t.Configuration,
                t.Size,
                t.MagnetometerCount,
                t.AccelerometerCount,
                t.Notes,
                CalibrationCount = t.Calibrations.Count,
                LatestCalibrationDate = (DateTimeOffset?)t.Calibrations
                    .OrderByDescending(c => c.CalibrationDate)
                    .Select(c => c.CalibrationDate)
                    .FirstOrDefault(),
                t.CreatedAt,
                t.UpdatedAt,
                t.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Tool", serial.ToString());

        return Ok(new ToolDetailDto(
            row.Id,
            row.SerialNumber,
            ToolDisplay.Name(row.GenerationName, row.SerialNumber),
            row.FirmwareVersion,
            row.GenerationName,
            row.StatusName,
            row.Configuration,
            row.Size,
            row.MagnetometerCount,
            row.AccelerometerCount,
            row.Notes,
            row.CalibrationCount,
            row.LatestCalibrationDate == default ? null : row.LatestCalibrationDate,
            row.CreatedAt,
            row.UpdatedAt,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<ToolDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateToolDto dto,
        CancellationToken ct)
    {
        if (await master.Tools.AnyAsync(t => t.SerialNumber == dto.SerialNumber, ct))
            return this.ConflictProblem(
                $"A tool with serial {dto.SerialNumber} already exists.");

        var generation = ResolveGeneration(dto.Generation, dto.FirmwareVersion, dto.Configuration, dto.Size);
        if (generation is null)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateToolDto.Generation)] = [GenerationErrorMessage(dto.Generation)],
            });

        var tool = new Tool(dto.SerialNumber, dto.FirmwareVersion, dto.MagnetometerCount, dto.AccelerometerCount)
        {
            Configuration = dto.Configuration,
            Size          = dto.Size,
            Generation    = generation,
            Status        = ToolStatus.Active,
            Notes         = dto.Notes,
        };

        master.Tools.Add(tool);
        await master.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { serial = tool.SerialNumber },
            new ToolDetailDto(
                tool.Id, tool.SerialNumber,
                ToolDisplay.Name(tool.Generation.Name, tool.SerialNumber),
                tool.FirmwareVersion, tool.Generation.Name, tool.Status.Name,
                tool.Configuration, tool.Size, tool.MagnetometerCount, tool.AccelerometerCount,
                tool.Notes, CalibrationCount: 0, LatestCalibrationDate: null,
                tool.CreatedAt, tool.UpdatedAt,
                ConcurrencyHelper.EncodeRowVersion(tool.RowVersion)));
    }

    // ---------- update ----------

    [HttpPut("{serial:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int serial,
        [FromBody] UpdateToolDto dto,
        CancellationToken ct)
    {
        var tool = await master.Tools.FirstOrDefaultAsync(t => t.SerialNumber == serial, ct);
        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        if (this.ApplyClientRowVersion(tool, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        if (!ToolGeneration.TryFromName(dto.Generation, ignoreCase: true, out var generation))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateToolDto.Generation)] = [GenerationErrorMessage(dto.Generation)],
            });

        // Serial rename: refurb sometimes re-issues the serial. Validate the
        // unique-index collision up front so we return a clean 409 instead
        // of an opaque DbUpdateException from SaveChangesAsync.
        if (dto.SerialNumber != tool.SerialNumber)
        {
            var collision = await master.Tools.AnyAsync(
                t => t.SerialNumber == dto.SerialNumber && t.Id != tool.Id, ct);
            if (collision)
                return this.ConflictProblem(
                    $"Cannot rename to serial {dto.SerialNumber}; another tool already uses it.");
        }

        tool.SerialNumber       = dto.SerialNumber;
        tool.FirmwareVersion    = dto.FirmwareVersion;
        tool.Generation         = generation;
        tool.Configuration      = dto.Configuration;
        tool.Size               = dto.Size;
        tool.MagnetometerCount  = dto.MagnetometerCount;
        tool.AccelerometerCount = dto.AccelerometerCount;
        tool.Notes              = dto.Notes;
        // UpdatedAt + UpdatedBy are stamped by the DbContext audit
        // interceptor — don't set them manually.

        if (await master.SaveOrConflictAsync(this, "Tool", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // Resolves a Generation: explicit name if provided & valid, else infer
    // from firmware + config + size. Returns null only when an explicit name
    // was supplied AND it doesn't match any SmartEnum value — the caller
    // turns that into a 400 ValidationProblem.
    private static ToolGeneration? ResolveGeneration(string? explicitName, string firmware, int configuration, int size)
    {
        if (string.IsNullOrWhiteSpace(explicitName))
            return Tool.InferGeneration(firmware, configuration, size);

        return ToolGeneration.TryFromName(explicitName, ignoreCase: true, out var g) ? g : null;
    }

    private static string GenerationErrorMessage(string? supplied) =>
        $"Unknown generation '{supplied}'. Allowed: {string.Join(", ", ToolGeneration.List.Select(g => g.Name))}.";

    // ---------- retire ----------

    [HttpPost("{serial:int}/retire")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retire(
        int serial,
        [FromBody] RetireToolDto? dto,
        CancellationToken ct)
    {
        var tool = await master.Tools.FirstOrDefaultAsync(t => t.SerialNumber == serial, ct);
        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        if (tool.Status == ToolStatus.Retired)
            return NoContent();   // Idempotent — re-retiring a retired tool is a no-op.

        if (tool.Status == ToolStatus.Lost)
            return this.ConflictProblem(
                "Lost tools cannot be retired through this endpoint; flip back to Active first.");

        tool.Status = ToolStatus.Retired;
        if (!string.IsNullOrWhiteSpace(dto?.Reason))
            tool.Notes = AppendStamped(tool.Notes, $"Retired: {dto.Reason}");

        await master.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- reactivate ----------

    [HttpPost("{serial:int}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(int serial, CancellationToken ct)
    {
        var tool = await master.Tools.FirstOrDefaultAsync(t => t.SerialNumber == serial, ct);
        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        if (tool.Status == ToolStatus.Active)
            return NoContent();   // Idempotent.

        tool.Status = ToolStatus.Active;
        tool.Notes  = AppendStamped(tool.Notes, "Reactivated");

        await master.SaveChangesAsync(ct);
        return NoContent();
    }

    // Notes is the audit trail for status transitions — append a timestamped
    // line rather than overwriting so the lifecycle history reads top-down.
    private static string AppendStamped(string? existing, string entry)
    {
        var stamped = $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC] {entry}";
        return string.IsNullOrWhiteSpace(existing) ? stamped : $"{existing}\n{stamped}";
    }

    // ---------- nested calibrations ----------

    [HttpGet("{serial:int}/calibrations")]
    [ProducesResponseType<IEnumerable<CalibrationSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCalibrations(int serial, CancellationToken ct)
    {
        var tool = await master.Tools
            .AsNoTracking()
            .Where(t => t.SerialNumber == serial)
            .Select(t => new { t.Id, GenerationName = t.Generation.Name })
            .FirstOrDefaultAsync(ct);

        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        var displayName = ToolDisplay.Name(tool.GenerationName, serial);

        var rows = await master.Calibrations
            .AsNoTracking()
            .Where(c => c.ToolId == tool.Id)
            .OrderByDescending(c => c.CalibrationDate)
            .Select(c => new CalibrationSummaryDto(
                c.Id,
                c.ToolId,
                c.SerialNumber,
                displayName,
                c.CalibrationDate,
                c.CalibratedBy,
                c.MagnetometerCount,
                c.IsNominal,
                c.IsSuperseded,
                c.Source.Name))
            .ToListAsync(ct);

        return Ok(rows);
    }
}

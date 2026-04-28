using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Settings;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations;
using SDI.Enki.Shared.Calibrations.Processing;
using SDI.Enki.Shared.Tools;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Calibration detail endpoint. Calibrations are intrinsic to a Tool — the
/// list view lives nested under <see cref="ToolsController.ListCalibrations"/>
/// at <c>GET /tools/{serial}/calibrations</c>. This controller exists only
/// because cal rows have their own GUID and the detail view (with the full
/// Marduk payload) is convenient to bookmark and link to directly.
/// </summary>
[ApiController]
[Route("calibrations")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class CalibrationsController(EnkiMasterDbContext master) : ControllerBase
{
    // ---------- processing defaults (read-only) ----------

    /// <summary>
    /// Reference-field defaults for the ToolCalibrate wizard. Read from
    /// the SystemSetting rows seeded under
    /// <c>SystemSettingKeys.CalibrationDefault*</c>. Exposed at
    /// <c>EnkiApiScope</c> (not admin-only) because the operators using
    /// the wizard aren't necessarily admins.
    /// </summary>
    [HttpGet("processing-defaults")]
    [ProducesResponseType<ProcessingDefaultsDto>(StatusCodes.Status200OK)]
    public async Task<ProcessingDefaultsDto> GetProcessingDefaults(CancellationToken ct)
    {
        var rows = await master.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith("Calibration:Default:"))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new ProcessingDefaultsDto(
            GTotal:             ParseDouble(rows, SystemSettingKeys.CalibrationDefaultGTotal,             1000.01),
            BTotal:             ParseDouble(rows, SystemSettingKeys.CalibrationDefaultBTotal,             46895.0),
            DipDegrees:         ParseDouble(rows, SystemSettingKeys.CalibrationDefaultDipDegrees,         59.867),
            DeclinationDegrees: ParseDouble(rows, SystemSettingKeys.CalibrationDefaultDeclinationDegrees, 12.313),
            CoilConstant:       ParseDouble(rows, SystemSettingKeys.CalibrationDefaultCoilConstant,       360.0),
            ActiveBDipDegrees:  ParseDouble(rows, SystemSettingKeys.CalibrationDefaultActiveBDipDegrees,  89.44),
            SampleRateHz:       ParseDouble(rows, SystemSettingKeys.CalibrationDefaultSampleRateHz,       100.0),
            ManualSign:         ParseDouble(rows, SystemSettingKeys.CalibrationDefaultManualSign,         1.0),
            DefaultCurrent:     ParseDouble(rows, SystemSettingKeys.CalibrationDefaultCurrent,            6.01),
            MagSource:          rows.TryGetValue(SystemSettingKeys.CalibrationDefaultMagSource, out var ms) ? ms : "static",
            IncludeDeclination: ParseBool(rows,   SystemSettingKeys.CalibrationDefaultIncludeDeclination, true));
    }

    private static double ParseDouble(IReadOnlyDictionary<string, string> rows, string key, double fallback) =>
        rows.TryGetValue(key, out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : fallback;

    private static bool ParseBool(IReadOnlyDictionary<string, string> rows, string key, bool fallback) =>
        rows.TryGetValue(key, out var raw) && bool.TryParse(raw, out var v) ? v : fallback;

    // ---------- detail ----------

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CalibrationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await master.Calibrations
            .AsNoTracking()
            .Include(c => c.Tool)
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.ToolId,
                c.SerialNumber,
                GenerationName = c.Tool!.Generation.Name,
                c.CalibrationDate,
                c.CalibratedBy,
                c.MagnetometerCount,
                c.IsNominal,
                c.IsSuperseded,
                SourceName = c.Source.Name,
                c.Notes,
                c.PayloadJson,
                c.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Calibration", id.ToString());

        return Ok(new CalibrationDetailDto(
            row.Id,
            row.ToolId,
            row.SerialNumber,
            ToolDisplay.Name(row.GenerationName, row.SerialNumber),
            row.CalibrationDate,
            row.CalibratedBy,
            row.MagnetometerCount,
            row.IsNominal,
            row.IsSuperseded,
            row.SourceName,
            row.Notes,
            row.PayloadJson,
            row.CreatedAt));
    }
}

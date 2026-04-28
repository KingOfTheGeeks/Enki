using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations;
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

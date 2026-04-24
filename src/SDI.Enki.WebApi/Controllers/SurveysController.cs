using AMR.Core.Survey.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.WebApi.Multitenancy;

using MardukSurveyStation = AMR.Core.Survey.Models.SurveyStation;
using MardukTieOn = AMR.Core.Survey.Models.TieOn;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Surveys under a Well. <c>POST .../calculate</c> is the first real Marduk
/// integration in Enki — it hands the persisted Survey rows to
/// <see cref="ISurveyCalculator"/> (minimum-curvature), reads back the
/// computed trajectory, and updates the rows in place.
///
/// Enki persists; Marduk computes. No survey math is reimplemented here.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:int}/wells/{wellId:int}/surveys")]
public sealed class SurveysController(
    ITenantDbContextFactory dbFactory,
    ISurveyCalculator surveyCalculator) : ControllerBase
{
    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate(
        int jobId, int wellId,
        [FromBody] SurveyCalculationRequestDto request,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        // TieOn selection: explicit id if supplied, else the lowest-Id TieOn on the well.
        var tieOnQuery = db.TieOns.Where(t => t.WellId == wellId);
        if (request.TieOnId is int tid) tieOnQuery = tieOnQuery.Where(t => t.Id == tid);

        var tieOn = await tieOnQuery.OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
        if (tieOn is null)
            return BadRequest(new { error = $"Well {wellId} has no TieOn — supply one before calculating surveys." });

        // Load surveys ordered by depth (Marduk expects monotonic depth).
        var surveys = await db.Surveys
            .Where(s => s.WellId == wellId)
            .OrderBy(s => s.Depth)
            .ToListAsync(ct);

        if (surveys.Count == 0)
            return Ok(new SurveyCalculationResponseDto(wellId, 0, request.Precision, DateTimeOffset.UtcNow));

        // Map Enki → Marduk.
        var mardukTieOn = new MardukTieOn(
            depth: tieOn.Depth,
            inclination: tieOn.Inclination,
            azimuth: tieOn.Azimuth,
            northing: tieOn.Northing,
            easting: tieOn.Easting,
            verticalReference: tieOn.VerticalReference,
            subSeaReference: tieOn.SubSeaReference,
            verticalSectionDirection: tieOn.VerticalSectionDirection);

        var stations = surveys
            .Select(s => new MardukSurveyStation(s.Depth, s.Inclination, s.Azimuth))
            .ToArray();

        // Run Marduk's minimum-curvature engine.
        var computed = surveyCalculator.Process(
            mardukTieOn,
            stations,
            metersToCalculateDegreesOver: request.MetersToCalculateDegreesOver,
            precision: request.Precision);

        // Write results back. Index-aligned: surveys[i] ↔ computed[i] (both sorted by depth).
        for (int i = 0; i < surveys.Count; i++)
        {
            var src = computed[i];
            var dst = surveys[i];
            dst.VerticalDepth   = src.VerticalDepth;
            dst.SubSea          = src.SubSea;
            dst.North           = src.North;
            dst.East            = src.East;
            dst.DoglegSeverity  = src.DoglegSeverity;
            dst.VerticalSection = src.VerticalSection;
            dst.Northing        = src.Northing;
            dst.Easting         = src.Easting;
            dst.Build           = src.Build;
            dst.Turn            = src.Turn;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new SurveyCalculationResponseDto(
            WellId: wellId,
            SurveysProcessed: surveys.Count,
            Precision: request.Precision,
            CalculatedAt: DateTimeOffset.UtcNow));
    }
}

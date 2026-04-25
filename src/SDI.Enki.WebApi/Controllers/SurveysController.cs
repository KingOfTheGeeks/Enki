using AMR.Core.Survey.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

using MardukSurveyStation = AMR.Core.Survey.Models.SurveyStation;
using MardukTieOn = AMR.Core.Survey.Models.TieOn;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Surveys under a Well, which lives under a Job. Routes nest fully:
/// <c>/tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/surveys</c>.
/// Every action confirms the parent (jobId, wellId) pair belongs
/// together via <see cref="WellLookup.WellExistsAsync"/> — an unknown
/// pair surfaces as a 404 NotFoundProblem("Well", id) rather than a
/// silent empty list or wrong-tenant data leak.
///
/// <para>
/// CRUD shape: list / get / create-single / create-bulk / update /
/// delete. Bulk create is atomic with a depth-monotonicity precondition
/// guard. Computed trajectory fields (VerticalDepth, DoglegSeverity,
/// …) are owned by Calculate and overwritten when it runs.
/// </para>
///
/// <para>
/// <c>POST .../calculate</c> is the Marduk integration: hands every
/// Survey on the Well to <see cref="ISurveyCalculator"/>
/// (minimum-curvature) using the lowest-Id (or explicitly supplied)
/// TieOn, reads back the computed trajectory, writes the results onto
/// the survey rows in place. Enki persists; Marduk computes.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/surveys")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class SurveysController(
    ITenantDbContextFactory dbFactory,
    ISurveyCalculator surveyCalculator) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    public async Task<IActionResult> List(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var rows = await db.Surveys
            .AsNoTracking()
            .Where(s => s.WellId == wellId)
            .OrderBy(s => s.Depth)
            .Select(s => new SurveySummaryDto(
                s.Id, s.WellId,
                s.Depth, s.Inclination, s.Azimuth,
                s.VerticalDepth, s.SubSea, s.North, s.East,
                s.DoglegSeverity, s.VerticalSection,
                s.Northing, s.Easting, s.Build, s.Turn))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ---------- detail ----------

    [HttpGet("{surveyId:int}")]
    public async Task<IActionResult> Get(Guid jobId, int wellId, int surveyId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var dto = await db.Surveys
            .AsNoTracking()
            .Where(s => s.Id == surveyId && s.WellId == wellId)
            .Select(s => new SurveyDetailDto(
                s.Id, s.WellId,
                s.Depth, s.Inclination, s.Azimuth,
                s.VerticalDepth, s.SubSea, s.North, s.East,
                s.DoglegSeverity, s.VerticalSection,
                s.Northing, s.Easting, s.Build, s.Turn,
                s.CreatedAt, s.CreatedBy, s.UpdatedAt, s.UpdatedBy))
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? this.NotFoundProblem("Survey", surveyId.ToString())
            : Ok(dto);
    }

    // ---------- create one ----------

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateSurveyDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var survey = new Survey(wellId, dto.Depth, dto.Inclination, dto.Azimuth);
        db.Surveys.Add(survey);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                tenantCode = RouteData.Values["tenantCode"],
                jobId,
                wellId,
                surveyId = survey.Id,
            },
            new SurveySummaryDto(
                survey.Id, survey.WellId,
                survey.Depth, survey.Inclination, survey.Azimuth,
                VerticalDepth: 0, SubSea: 0, North: 0, East: 0,
                DoglegSeverity: 0, VerticalSection: 0,
                Northing: 0, Easting: 0, Build: 0, Turn: 0));
    }

    // ---------- create bulk ----------

    [HttpPost("bulk")]
    public async Task<IActionResult> CreateBulk(
        Guid jobId,
        int wellId,
        [FromBody] CreateSurveysDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Depth-monotonicity precondition. Marduk's min-curvature engine
        // assumes strictly increasing depth; a duplicate or out-of-order
        // row would silently corrupt the trajectory. Catch it here, not
        // inside the calculator.
        var depths = dto.Stations.Select(s => s.Depth).ToArray();
        for (var i = 1; i < depths.Length; i++)
        {
            if (depths[i] <= depths[i - 1])
                return this.ValidationProblem(new Dictionary<string, string[]>
                {
                    [$"Stations[{i}].Depth"] =
                        [$"Depth must strictly increase row over row " +
                         $"(row {i - 1} = {depths[i - 1]}, row {i} = {depths[i]})."],
                });
        }

        // One SaveChanges → atomic; any row-level failure rolls the lot back.
        var rows = dto.Stations
            .Select(s => new Survey(wellId, s.Depth, s.Inclination, s.Azimuth))
            .ToList();
        db.Surveys.AddRange(rows);
        await db.SaveChangesAsync(ct);

        var summaries = rows.Select(r => new SurveySummaryDto(
                r.Id, r.WellId,
                r.Depth, r.Inclination, r.Azimuth,
                VerticalDepth: 0, SubSea: 0, North: 0, East: 0,
                DoglegSeverity: 0, VerticalSection: 0,
                Northing: 0, Easting: 0, Build: 0, Turn: 0))
            .ToList();

        return Ok(summaries);
    }

    // ---------- update ----------

    [HttpPut("{surveyId:int}")]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        int surveyId,
        [FromBody] UpdateSurveyDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var survey = await db.Surveys
            .FirstOrDefaultAsync(s => s.Id == surveyId && s.WellId == wellId, ct);
        if (survey is null)
            return this.NotFoundProblem("Survey", surveyId.ToString());

        // Only observed fields are accepted from the wire. Computed
        // fields (VerticalDepth, DoglegSeverity, …) are owned by
        // Calculate and rewritten the next time it runs.
        survey.Depth       = dto.Depth;
        survey.Inclination = dto.Inclination;
        survey.Azimuth     = dto.Azimuth;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ---------- delete ----------

    [HttpDelete("{surveyId:int}")]
    public async Task<IActionResult> Delete(Guid jobId, int wellId, int surveyId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var survey = await db.Surveys
            .FirstOrDefaultAsync(s => s.Id == surveyId && s.WellId == wellId, ct);
        if (survey is null)
            return this.NotFoundProblem("Survey", surveyId.ToString());

        db.Surveys.Remove(survey);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ---------- calculate ----------

    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate(
        Guid jobId,
        int wellId,
        [FromBody] SurveyCalculationRequestDto request,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // TieOn selection: explicit id if supplied, else the lowest-Id
        // TieOn on the well.
        var tieOnQuery = db.TieOns.Where(t => t.WellId == wellId);
        if (request.TieOnId is int tid) tieOnQuery = tieOnQuery.Where(t => t.Id == tid);

        var tieOn = await tieOnQuery.OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
        if (tieOn is null)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tieOn"] = [$"Well {wellId} has no TieOn — supply one before calculating surveys."],
            });

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

        // Length contract — Marduk should return one computed station per
        // input. If the count drifts (partial result, edge-case drop, bug)
        // the index-aligned writeback below would silently corrupt or
        // throw IndexOutOfRangeException. Fail loud, fail early.
        if (computed.Length != surveys.Count)
            throw new InvalidOperationException(
                $"Survey calculator returned {computed.Length} computed stations " +
                $"for {surveys.Count} input surveys on Well {wellId}. Refusing to " +
                $"write back a partial result.");

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

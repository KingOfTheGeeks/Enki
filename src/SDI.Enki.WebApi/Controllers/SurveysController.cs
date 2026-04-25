using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Infrastructure.Surveys;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

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
/// Trajectory calculation is automatic: every Create / BulkCreate /
/// Update / Delete on this controller (and on
/// <see cref="TieOnsController"/>) calls
/// <see cref="ISurveyAutoCalculator.RecalculateAsync"/> before
/// returning, so the very next read gets a fully-computed grid.
/// Clients never see uncalculated rows. The
/// <c>POST .../calculate</c> endpoint remains as a force-recalculate
/// admin action with optional parameter overrides (averaging window,
/// precision, explicit tie-on Id).
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/surveys")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class SurveysController(
    ITenantDbContextFactory dbFactory,
    ISurveyAutoCalculator surveyAutoCalculator) : ControllerBase
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

        // Recompute the well's trajectory before returning so the
        // response (and the next GET) carries already-calculated
        // columns. The tracked entity is mutated in-place by the
        // auto-calc, so the SurveySummaryDto below reads the
        // populated values.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

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
                survey.VerticalDepth, survey.SubSea, survey.North, survey.East,
                survey.DoglegSeverity, survey.VerticalSection,
                survey.Northing, survey.Easting, survey.Build, survey.Turn));
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

        // Recompute trajectory across the full set (including the
        // existing rows) before returning. Tracked entities pick up the
        // computed columns in-place.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

        var summaries = rows.Select(r => new SurveySummaryDto(
                r.Id, r.WellId,
                r.Depth, r.Inclination, r.Azimuth,
                r.VerticalDepth, r.SubSea, r.North, r.East,
                r.DoglegSeverity, r.VerticalSection,
                r.Northing, r.Easting, r.Build, r.Turn))
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
        // fields (VerticalDepth, DoglegSeverity, …) are rewritten by
        // the auto-calc immediately after this save.
        survey.Depth       = dto.Depth;
        survey.Inclination = dto.Inclination;
        survey.Azimuth     = dto.Azimuth;
        await db.SaveChangesAsync(ct);

        // Changing one station's observed values shifts every downstream
        // computed value, so always recompute the whole well.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

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

        // Removing a station re-aligns the trajectory of every later
        // station; recompute the remainder before returning.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

        return NoContent();
    }

    // ---------- calculate ----------
    //
    // Force-recalculate endpoint. Every Survey/TieOn mutation already
    // recomputes the trajectory before returning, so this is now an
    // admin tool — useful after a manual DB edit, a Marduk version
    // change, or to confirm the rule from outside the controllers.
    // The request DTO is preserved on the wire for compatibility but
    // its parameters (averaging window / precision / explicit tie-on)
    // are currently ignored — the auto-calc uses defaults that match
    // what this endpoint historically used. If overrides are needed,
    // expand ISurveyAutoCalculator.RecalculateAsync to accept them.

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

        // Surface the no-tie-on case as 400 here even though the auto-calc
        // would silently no-op — explicit requests should fail loudly so
        // the caller knows nothing was computed.
        if (!await db.TieOns.AnyAsync(t => t.WellId == wellId, ct))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tieOn"] = [$"Well {wellId} has no TieOn — supply one before calculating surveys."],
            });

        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

        var processed = await db.Surveys.CountAsync(s => s.WellId == wellId, ct);
        return Ok(new SurveyCalculationResponseDto(
            WellId: wellId,
            SurveysProcessed: processed,
            Precision: request.Precision,
            CalculatedAt: DateTimeOffset.UtcNow));
    }
}

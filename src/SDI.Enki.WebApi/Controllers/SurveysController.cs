using AMR.Core.IO;
using AMR.Core.IO.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class SurveysController(
    ITenantDbContextFactory dbFactory,
    ISurveyAutoCalculator surveyAutoCalculator,
    ISurveyImporter surveyImporter) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<SurveySummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType<SurveyDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType<SurveySummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType<IEnumerable<SurveySummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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

    // ---------- delete-all (Clear) ----------
    //
    // Wipes every Survey row on the well. Tie-ons are deliberately
    // left alone — they're reference data the user usually wants to
    // keep across re-imports / re-runs. If a caller needs to clear
    // tie-ons they hit the per-tie-on DELETE on the TieOns controller.
    //
    // No-op when the well already has zero surveys (returns 204
    // anyway — REST convention for idempotent DELETE on a collection).

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAll(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var rows = await db.Surveys.Where(s => s.WellId == wellId).ToListAsync(ct);
        if (rows.Count == 0) return NoContent();

        db.Surveys.RemoveRange(rows);
        await db.SaveChangesAsync(ct);

        // Auto-calc no-ops when there are no surveys, but call it
        // anyway for symmetry with the other mutation paths — keeps
        // the "every mutation triggers a recalc" invariant audit-clean.
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
    [RequestTimeout("LongRunning")]
    [ProducesResponseType<SurveyCalculationResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status408RequestTimeout)]
    public async Task<IActionResult> Calculate(
        Guid jobId,
        int wellId,
        [FromBody] SurveyCalculationRequestDto request,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Defence-in-depth: every Well auto-gets a tie-on on creation
        // (see WellsController.Create), so this gate shouldn't fire
        // in normal flow. Kept against direct DB edits / pre-invariant
        // rows — surface as 400 rather than letting the auto-calc
        // silently no-op so the caller knows nothing was computed.
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

    // ---------- import ----------
    //
    // Accepts a multipart/form-data upload with a single "file" field
    // and runs the AMR.Core.IO importer over it. Behaviour:
    //   * Survey rows on the well are replaced wholesale with the
    //     file's stations — "load this file" matches user intent
    //     better than incremental append.
    //   * If the file's first row sits at depth 0, the importer
    //     promotes it to a tie-on (with the row's actual inclination
    //     / azimuth, not zeros) and removes it from the stations list.
    //     LAS files also surface a tie-on via the STRT mnemonic when
    //     no depth-0 row exists.
    //   * Tie-on replacement is gated on the keepExistingTieOn query
    //     param — by default the imported tie-on overwrites whatever
    //     was on the well; pass ?keepExistingTieOn=true to preserve a
    //     curated tie-on (e.g. one with grid coordinates already
    //     filled in) when re-importing surveys.
    //   * Auto-calc fires before returning, same as every other
    //     mutation path on this controller.
    // The importer's warnings (default-unit fallback, normalised
    // azimuth, tie-on-from-first-row, dropped NaN rows, etc.) ride
    // along on the response so the UI can show them inline.

    [HttpPost("import")]
    [RequestTimeout("LongRunning")]
    [EnableRateLimiting("Expensive")]
    [RequestSizeLimit(20_000_000)]                         // 20 MB cap — survey files are small
    [ProducesResponseType<SurveyImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Import(
        Guid jobId,
        int wellId,
        [FromForm] IFormFile file,
        [FromQuery] bool? keepExistingTieOn,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["Upload a non-empty survey file in the 'file' multipart field."],
            });

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Run the importer. Any hard error (unknown format, missing
        // required column, length-mismatched output) lands as an Error
        // note — surface those as a 400 with the notes attached so the
        // user sees exactly what the parser couldn't do.
        ImportedSurveyData imported;
        await using (var stream = file.OpenReadStream())
        {
            imported = surveyImporter.Import(stream, new SurveyImportOptions
            {
                SourceFileName = file.FileName,
            });
        }

        if (!imported.Success)
        {
            var errorMessages = imported.Notes
                .Where(n => n.Severity == NoteSeverity.Error)
                .Select(n => $"[{n.Code}] {n.Message}")
                .ToArray();

            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = errorMessages.Length > 0
                    ? errorMessages
                    : ["Importer returned no stations."],
            });
        }

        // Tie-on overwrite gate: if the import would replace an
        // existing tie-on that has any non-default value (Northing,
        // VerticalReference, etc.), require the caller to opt in to
        // the overwrite explicitly. Otherwise we'd silently destroy
        // grid coordinates the user set up manually. The keepExistingTieOn
        // query param can be set to false (overwrite) or true (preserve)
        // to skip this gate; the dialog in the Blazor import button
        // wires up that retry.
        var existingTieOnsAll = await db.TieOns.Where(t => t.WellId == wellId).ToListAsync(ct);
        if (imported.TieOn is not null
            && keepExistingTieOn is null
            && existingTieOnsAll.Any(IsTieOnNonDefault))
        {
            var existing = existingTieOnsAll.First(IsTieOnNonDefault);
            return this.ConflictProblem(
                "Well has an existing tie-on with non-default values; the imported file " +
                "would overwrite it. Re-submit with ?keepExistingTieOn=false to overwrite, " +
                "or =true to keep the existing tie-on and import only the survey stations.",
                new Dictionary<string, object?>
                {
                    ["conflictKind"] = "tieOnOverwrite",
                    ["existingTieOn"] = new
                    {
                        existing.Id,
                        existing.Depth,
                        existing.Inclination,
                        existing.Azimuth,
                        existing.Northing,
                        existing.Easting,
                        existing.VerticalReference,
                        existing.SubSeaReference,
                        existing.VerticalSectionDirection,
                    },
                    ["importedTieOn"] = new
                    {
                        imported.TieOn.Depth,
                        imported.TieOn.Inclination,
                        imported.TieOn.Azimuth,
                        imported.TieOn.Northing,
                        imported.TieOn.Easting,
                        imported.TieOn.VerticalReference,
                        imported.TieOn.SubSeaReference,
                        imported.TieOn.VerticalSectionDirection,
                    },
                });
        }

        // Replace existing survey rows with the file's stations. EF
        // tracks the deletes; SaveChanges commits them before we
        // attach the new rows so we never collide on an in-flight
        // duplicate-key insert.
        var existingSurveys = await db.Surveys.Where(s => s.WellId == wellId).ToListAsync(ct);
        db.Surveys.RemoveRange(existingSurveys);

        var tieOnsCreated = 0;
        // True when caller explicitly chose to keep, OR when the gate
        // above didn't fire because there's no non-default existing.
        var shouldReplaceTieOn = imported.TieOn is not null && keepExistingTieOn != true;
        if (shouldReplaceTieOn)
        {
            db.TieOns.RemoveRange(existingTieOnsAll);

            db.TieOns.Add(new TieOn(
                wellId,
                imported.TieOn!.Depth,
                imported.TieOn.Inclination,
                imported.TieOn.Azimuth)
            {
                Northing                 = imported.TieOn.Northing,
                Easting                  = imported.TieOn.Easting,
                VerticalReference        = imported.TieOn.VerticalReference,
                SubSeaReference          = imported.TieOn.SubSeaReference,
                VerticalSectionDirection = imported.TieOn.VerticalSectionDirection,
            });
            tieOnsCreated = 1;
        }

        foreach (var s in imported.Stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inclination, s.Azimuth));

        await db.SaveChangesAsync(ct);

        // Same auto-calc trigger as Create / CreateBulk / Update / Delete.
        // The validator already pre-sorted + normalised; no extra guard here.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

        return Ok(new SurveyImportResultDto(
            WellId:               wellId,
            DetectedFormat:       imported.Metadata.DetectedFormat.ToString(),
            DetectedDepthUnit:    imported.Metadata.DetectedDepthUnit.ToString(),
            DepthUnitWasDetected: imported.Metadata.DepthUnitWasDetected,
            WellNameFromFile:     imported.Metadata.WellName,
            TieOnsCreated:        tieOnsCreated,
            SurveysImported:      imported.Stations.Count,
            ImportedAt:           DateTimeOffset.UtcNow,
            Notes:                imported.Notes
                .Select(n => new SurveyImportNoteDto(
                    n.Severity.ToString(), n.Code, n.Message, n.LineNumber))
                .ToArray()));
    }

    /// <summary>
    /// True when a tie-on has at least one non-zero value across its
    /// observed and reference fields — i.e. somebody curated it, so an
    /// import should not silently overwrite it. A freshly-seeded
    /// all-zero tie-on is "default" and may be replaced without prompt.
    /// </summary>
    private static bool IsTieOnNonDefault(TieOn t) =>
        t.Depth                    != 0 ||
        t.Inclination              != 0 ||
        t.Azimuth                  != 0 ||
        t.North                    != 0 ||
        t.East                     != 0 ||
        t.Northing                 != 0 ||
        t.Easting                  != 0 ||
        t.VerticalReference        != 0 ||
        t.SubSeaReference          != 0 ||
        t.VerticalSectionDirection != 0;
}

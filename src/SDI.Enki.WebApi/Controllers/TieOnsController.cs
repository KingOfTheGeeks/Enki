using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Infrastructure.Surveys;
using SDI.Enki.Shared.Wells.TieOns;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Tie-on stations under a Well. A tie-on is the reference point from
/// which a Well's trajectory is calculated — depth + inclination +
/// azimuth at a known surface or top-of-survey location, plus the
/// derived grid coordinates that anchor the survey set.
///
/// <para>
/// Invariant: every Well always has at least one tie-on. The first
/// row is created automatically at zero values when the Well is
/// created (see <c>WellsController.Create</c>); this controller's
/// <c>Delete</c> resets the row to zero rather than removing it.
/// Additional rows can be added for historical references (re-tied
/// surveys after a sidetrack, alternative reference frames).
/// Trajectory calc — both the auto-calc on every Survey/TieOn
/// mutation and the explicit <c>SurveysController.Calculate</c>
/// endpoint — consumes the <strong>lowest-Id</strong> tie-on as the
/// anchor; later rows are reference-only.
/// </para>
///
/// <para>
/// Routes nest under <c>/jobs/{jobId:guid}/wells/{wellId:int}</c>.
/// Every action probes that the parent Well exists under that Job
/// via <see cref="WellLookup.WellExistsAsync"/>; an unknown
/// (jobId, wellId) pair returns <c>404 NotFoundProblem("Well", id)</c>
/// so the shape matches every other child-entity controller in this
/// surface.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/tieons")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class TieOnsController(
    ITenantDbContextFactory dbFactory,
    ISurveyAutoCalculator surveyAutoCalculator) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<TieOnSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Two-stage projection so RowVersion can be base64-encoded —
        // see SurveysController for rationale.
        var rows = await db.TieOns
            .AsNoTracking()
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Depth)
            .Select(t => new
            {
                t.Id, t.WellId,
                t.Depth, t.Inclination, t.Azimuth,
                t.North, t.East, t.Northing, t.Easting,
                t.VerticalReference, t.SubSeaReference, t.VerticalSectionDirection,
                t.CreatedAt,
                t.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(t => new TieOnSummaryDto(
            t.Id, t.WellId,
            t.Depth, t.Inclination, t.Azimuth,
            t.North, t.East, t.Northing, t.Easting,
            t.VerticalReference, t.SubSeaReference, t.VerticalSectionDirection,
            t.CreatedAt,
            ConcurrencyHelper.EncodeRowVersion(t.RowVersion))));
    }

    // ---------- detail ----------

    [HttpGet("{tieOnId:int}")]
    [ProducesResponseType<TieOnDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, int wellId, int tieOnId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var row = await db.TieOns
            .AsNoTracking()
            .Where(t => t.Id == tieOnId && t.WellId == wellId)
            .Select(t => new
            {
                t.Id, t.WellId,
                t.Depth, t.Inclination, t.Azimuth,
                t.North, t.East, t.Northing, t.Easting,
                t.VerticalReference, t.SubSeaReference, t.VerticalSectionDirection,
                t.CreatedAt, t.CreatedBy, t.UpdatedAt, t.UpdatedBy,
                t.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("TieOn", tieOnId.ToString());

        return Ok(new TieOnDetailDto(
            row.Id, row.WellId,
            row.Depth, row.Inclination, row.Azimuth,
            row.North, row.East, row.Northing, row.Easting,
            row.VerticalReference, row.SubSeaReference, row.VerticalSectionDirection,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<TieOnSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateTieOnDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tieOn = new TieOn(wellId, dto.Depth, dto.Inclination, dto.Azimuth)
        {
            North                    = dto.North,
            East                     = dto.East,
            Northing                 = dto.Northing,
            Easting                  = dto.Easting,
            VerticalReference        = dto.VerticalReference,
            SubSeaReference          = dto.SubSeaReference,
            VerticalSectionDirection = dto.VerticalSectionDirection,
        };
        db.TieOns.Add(tieOn);
        await db.SaveChangesAsync(ct);

        // Adding the well's first tie-on enables trajectory calculation;
        // adding a later tie-on doesn't change which one anchors the
        // calc (lowest-Id wins) but recalc is cheap. Always re-run so
        // the next GET returns calculated rows.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                tenantCode = RouteData.Values["tenantCode"],
                jobId,
                wellId,
                tieOnId = tieOn.Id,
            },
            new TieOnSummaryDto(
                tieOn.Id, tieOn.WellId,
                tieOn.Depth, tieOn.Inclination, tieOn.Azimuth,
                tieOn.North, tieOn.East, tieOn.Northing, tieOn.Easting,
                tieOn.VerticalReference, tieOn.SubSeaReference, tieOn.VerticalSectionDirection,
                tieOn.CreatedAt,
                tieOn.EncodeRowVersion()));
    }

    // ---------- update ----------

    [HttpPut("{tieOnId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        int tieOnId,
        [FromBody] UpdateTieOnDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tieOn = await db.TieOns
            .FirstOrDefaultAsync(t => t.Id == tieOnId && t.WellId == wellId, ct);
        if (tieOn is null)
            return this.NotFoundProblem("TieOn", tieOnId.ToString());

        if (this.ApplyClientRowVersion(tieOn, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        tieOn.Depth                    = dto.Depth;
        tieOn.Inclination              = dto.Inclination;
        tieOn.Azimuth                  = dto.Azimuth;
        tieOn.North                    = dto.North;
        tieOn.East                     = dto.East;
        tieOn.Northing                 = dto.Northing;
        tieOn.Easting                  = dto.Easting;
        tieOn.VerticalReference        = dto.VerticalReference;
        tieOn.SubSeaReference          = dto.SubSeaReference;
        tieOn.VerticalSectionDirection = dto.VerticalSectionDirection;

        // Tie-on is the shallowest station on the well; surveys must
        // sit strictly below it. Moving the tie-on down can collide
        // with — or overrun — existing surveys: a survey at exactly
        // the new depth duplicates the tie-on (auto-calc divides by
        // zero on the first deltaMd → NaN → SaveChanges-of-results
        // crash); a survey shallower than the new tie-on is "above"
        // it and meaningless for the trajectory.
        //
        // Per the behaviour spec on issue #11: drop every survey
        // whose depth is <= the new tie-on depth, then recompute. The
        // delete + tie-on update commit in the same SaveChanges so
        // the recalc runs against a consistent snapshot.
        var prunedSurveys = await db.Surveys
            .Where(s => s.WellId == wellId && s.Depth <= dto.Depth)
            .ToListAsync(ct);
        if (prunedSurveys.Count > 0)
            db.Surveys.RemoveRange(prunedSurveys);

        if (await db.SaveOrConflictAsync(this, "TieOn", ct) is { } conflict)
            return conflict;

        // Tie-on edits move the anchor — every remaining survey on
        // the well depends on it, so recompute before returning.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

        return NoContent();
    }

    // ---------- delete (reset-to-zero) ----------

    [HttpDelete("{tieOnId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid jobId, int wellId, int tieOnId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tieOn = await db.TieOns
            .FirstOrDefaultAsync(t => t.Id == tieOnId && t.WellId == wellId, ct);
        if (tieOn is null)
            return this.NotFoundProblem("TieOn", tieOnId.ToString());

        // Every Well must keep a tie-on on file (Marduk's calc requires
        // an anchor — without one, recalc no-ops). "Delete" here is
        // really "reset to zero": every observed + reference field on
        // the row goes to 0 but the row itself stays, so subsequent
        // surveys still compute against an anchor (an all-zero one)
        // rather than silently losing their trajectory. The endpoint
        // route + 204 contract are unchanged so existing UI wiring
        // keeps working.
        tieOn.Depth                    = 0;
        tieOn.Inclination              = 0;
        tieOn.Azimuth                  = 0;
        tieOn.North                    = 0;
        tieOn.East                     = 0;
        tieOn.Northing                 = 0;
        tieOn.Easting                  = 0;
        tieOn.VerticalReference        = 0;
        tieOn.SubSeaReference          = 0;
        tieOn.VerticalSectionDirection = 0;
        await db.SaveChangesAsync(ct);

        // Recompute every survey on the well against the zero-anchor.
        await surveyAutoCalculator.RecalculateAsync(db, wellId, ct);

        return NoContent();
    }
}

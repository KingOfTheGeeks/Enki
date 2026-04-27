using AMR.Core.Survey.Models;
using AMR.Core.Uncertainty.Implementations;
using AMR.Core.Uncertainty.Models;
using AMR.Core.Uncertainty.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Wells;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Wells under a Job. A Well is the physical wellbore that surveys
/// describe; in this domain a Well belongs to exactly one Job. The
/// rare cross-job-shared-well case is deferred to a future Project
/// concept that would layer above Job — until that ships, treat the
/// (Job, Well) hierarchy as 1:N strict.
///
/// <para>
/// CRUD shape: list / get / create / update / delete. Delete refuses
/// (409 ConflictProblem) when the Well has any child rows (Surveys,
/// TieOns, Tubulars, Formations, CommonMeasures). Cascade delete is
/// configured at the DB but the controller refuses to trigger it
/// silently — destroying a 10K-row survey set should be an explicit
/// decision, not a side-effect.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class WellsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<WellSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.JobExistsAsync(jobId, ct))
            return this.NotFoundProblem("Job", jobId.ToString());

        var rows = await db.Wells
            .AsNoTracking()
            .Where(w => w.JobId == jobId)
            .OrderBy(w => w.Name)
            .Select(w => new WellSummaryDto(
                w.Id, w.Name, w.Type.Name,
                w.Surveys.Count, w.TieOns.Count,
                w.CreatedAt))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ---------- trajectories ----------

    /// <summary>
    /// Aggregate trajectory projection for the whole Job: every
    /// well's tie-on (as a depth-0 anchor) + every survey station,
    /// ordered by measured depth. Backs the multi-well plan view +
    /// vertical-section + travelling-cylinder pages on Blazor.
    ///
    /// <para>
    /// Returns wells in alphabetical order so the chart legend reads
    /// stably. Wells with no surveys + no tie-on still appear in the
    /// response — the rendering side decides whether to skip empty
    /// curves; not the API's business.
    /// </para>
    /// </summary>
    [HttpGet("trajectories")]
    [ProducesResponseType<IEnumerable<WellTrajectoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Trajectories(Guid jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.JobExistsAsync(jobId, ct))
            return this.NotFoundProblem("Job", jobId.ToString());

        // Materialise to memory then project in-memory rather than
        // EF-translate a multi-source ordered union — the survey +
        // tie-on shapes are different enough that a SQL-side concat
        // would force me to fight EF's type unification rules. Read
        // surface is small (a Job's worth of wells; bounded by the
        // tenant's data) so the trade-off is cheap.
        //
        // Survey.VerticalSection is now relayed straight through.
        // Earlier this controller computed V-sect on the fly from
        // (North, East, VSD) because Marduk's MinimumCurvature was
        // bugged — it projected against absolute Northing / Easting
        // rather than relative North / East, yielding ~−17 M ft for
        // a Bakken well sited at Northing ≈ 17.4 M ft. Marduk has
        // been fixed (see the bug-fix comment on
        // MinimumCurvature.cs:64 and the regression test
        // VerticalSection_IsMeasuredFromTieOnOutward in
        // MinimumCurvatureTests). One operational note: rows
        // persisted before the Marduk fix still carry stale absolute
        // values until they're regenerated — any survey/tie-on edit
        // re-runs the calc, or a `start-dev.ps1 -Reset` re-seeds
        // every tenant database from scratch.
        var wells = await db.Wells
            .AsNoTracking()
            .Where(w => w.JobId == jobId)
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                w.Id,
                w.Name,
                Type = w.Type.Name,
                // Tie-on is the origin of the V-sect projection so
                // its VerticalSection is 0 by definition; the TieOn
                // entity doesn't carry the field.
                TieOnPoint = w.TieOns
                    .OrderBy(t => t.Id)
                    .Select(t => new TrajectoryPointDto(
                        t.Depth, t.Northing, t.Easting, t.VerticalReference,
                        VerticalSection: 0))
                    .FirstOrDefault(),
                SurveyPoints = w.Surveys
                    .OrderBy(s => s.Depth)
                    .Select(s => new TrajectoryPointDto(
                        s.Depth, s.Northing, s.Easting, s.VerticalDepth,
                        s.VerticalSection))
                    .ToList(),
            })
            .ToListAsync(ct);

        var result = wells
            .Select(w =>
            {
                var points = new List<TrajectoryPointDto>(
                    capacity: w.SurveyPoints.Count + (w.TieOnPoint is null ? 0 : 1));
                if (w.TieOnPoint is not null) points.Add(w.TieOnPoint);
                points.AddRange(w.SurveyPoints);
                return new WellTrajectoryDto(w.Id, w.Name, w.Type, points);
            })
            .ToList();

        return Ok(result);
    }

    // ---------- anti-collision ----------

    /// <summary>
    /// Travelling-cylinder anti-collision scan: <paramref name="wellId"/>
    /// is the target, every other well under the same Job is an
    /// offset. For every target station the response carries the
    /// closest-approach distance + clock-position to each offset's
    /// trajectory.
    ///
    /// <para>
    /// Math owner is Marduk's
    /// <see cref="IAntiCollisionScanner"/> — this endpoint loads the
    /// pre-computed Northing / Easting / TVD off Survey + TieOn rows
    /// (Marduk's auto-recalc owns those) and rehydrates a
    /// <see cref="SurveyStation"/> per row so the scanner can do the
    /// 3-D segment-projection geometry. No survey calculation runs
    /// here. The tie-on is included as the depth-zero anchor so a
    /// fresh well with only a tie-on still gets one sample row.
    /// </para>
    ///
    /// <para>
    /// Self-comparison is excluded (a well's distance to itself is
    /// always 0 and would dominate the chart). Sibling wells with
    /// no surveys + no tie-on are also skipped — there's nothing to
    /// project against. Empty results (no usable offsets) return an
    /// empty list, not a 404; the caller renders "no offsets to
    /// compare against" in that case.
    /// </para>
    /// </summary>
    [HttpGet("{wellId:int}/anti-collision")]
    [RequestTimeout("LongRunning")]
    [ProducesResponseType<IEnumerable<AntiCollisionScanDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status408RequestTimeout)]
    public async Task<IActionResult> AntiCollision(
        Guid jobId,
        int wellId,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Load the target's stations (tie-on + surveys), in MD order.
        // Tie-on goes first as the depth-0 anchor — same convention
        // the Trajectories endpoint uses; keeps the math symmetric
        // between the plan/vsection plots and this view.
        var target = await LoadStationsAsync(db, wellId, ct);
        if (target.Count == 0)
        {
            // Target has nothing to scan FROM. The caller still gets
            // an empty list rather than a 404 — the well exists, it
            // just hasn't been surveyed yet, which the UI can render
            // as "load surveys first" without an error path.
            return Ok(Array.Empty<AntiCollisionScanDto>());
        }

        // Load every other well under the Job + its trajectory in one
        // round-trip. Includes Type so the per-curve colour stays
        // consistent with the trajectory plot. Filter on JobId here
        // so a Well lurking in another Job under the same tenant
        // doesn't bleed in.
        var offsetWells = await db.Wells
            .AsNoTracking()
            .Where(w => w.JobId == jobId && w.Id != wellId)
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                w.Id,
                w.Name,
                Type = w.Type.Name,
            })
            .ToListAsync(ct);

        if (offsetWells.Count == 0)
            return Ok(Array.Empty<AntiCollisionScanDto>());

        // Map id → metadata so we can enrich the scanner output back
        // to the wire DTO. NamedTrajectory only carries the display
        // name; the Id + Type come from this side.
        var meta = offsetWells.ToDictionary(
            w => w.Name,
            w => (w.Id, w.Type));

        var named = new List<NamedTrajectory>(offsetWells.Count);
        foreach (var ow in offsetWells)
        {
            var stations = await LoadStationsAsync(db, ow.Id, ct);
            // Empty trajectories are dropped by the scanner anyway,
            // but skip them here so we don't allocate a zero-length
            // scan result + an empty legend entry on the chart.
            if (stations.Count == 0) continue;
            named.Add(new NamedTrajectory(ow.Name, stations));
        }

        if (named.Count == 0)
            return Ok(Array.Empty<AntiCollisionScanDto>());

        IAntiCollisionScanner scanner = new AntiCollisionScanner();
        var scans = scanner.ScanAll(target, named);

        var result = scans
            .Select(s =>
            {
                var (offsetId, offsetType) = meta[s.OffsetName];
                return new AntiCollisionScanDto(
                    OffsetWellId:   offsetId,
                    OffsetWellName: s.OffsetName,
                    OffsetWellType: offsetType,
                    Samples: s.Samples
                        .Select(x => new AntiCollisionSampleDto(
                            x.TargetMd, x.TargetTvd, x.Distance, x.ClockPositionDegrees))
                        .ToList());
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Lift one well's persisted minimum-curvature trajectory back
    /// into a list of Marduk <see cref="SurveyStation"/> objects so
    /// the anti-collision scanner can project against it. Tie-on
    /// goes first as the depth-0 anchor, then survey stations in MD
    /// order. Calls <see cref="SurveyStation.SetMinimumCurvature"/>
    /// to populate the computed fields from the cached database
    /// values — no math runs here, the scanner just reads N/E/TVD.
    /// </summary>
    private static async Task<IReadOnlyList<SurveyStation>> LoadStationsAsync(
        TenantDbContext db,
        int wellId,
        CancellationToken ct)
    {
        var tieOn = await db.TieOns
            .AsNoTracking()
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        var surveys = await db.Surveys
            .AsNoTracking()
            .Where(s => s.WellId == wellId)
            .OrderBy(s => s.Depth)
            .ToListAsync(ct);

        var stations = new List<SurveyStation>(
            capacity: surveys.Count + (tieOn is null ? 0 : 1));

        if (tieOn is not null)
        {
            // Tie-on is the trajectory's depth-0 anchor. North/East
            // (relative) are 0 by definition; Northing / Easting are
            // the surface grid coords; VerticalDepth = the tie-on's
            // VerticalReference. DLS / V-sect / Build / Turn don't
            // exist for the anchor station — pass 0 so the scanner's
            // tangent math (which only reads N/E/TVD + inc/az) sees
            // a well-formed origin.
            var anchor = new SurveyStation(tieOn.Depth, tieOn.Inclination, tieOn.Azimuth);
            anchor.SetMinimumCurvature(
                north:           tieOn.North,
                east:            tieOn.East,
                verticalDepth:   tieOn.VerticalReference,
                subSea:          tieOn.SubSeaReference,
                doglegSeverity:  0,
                verticalSection: 0,
                northing:        tieOn.Northing,
                easting:         tieOn.Easting,
                build:           0,
                turn:            0);
            stations.Add(anchor);
        }

        foreach (var s in surveys)
        {
            var st = new SurveyStation(s.Depth, s.Inclination, s.Azimuth);
            st.SetMinimumCurvature(
                north:           s.North,
                east:            s.East,
                verticalDepth:   s.VerticalDepth,
                subSea:          s.SubSea,
                doglegSeverity:  s.DoglegSeverity,
                verticalSection: s.VerticalSection,
                northing:        s.Northing,
                easting:         s.Easting,
                build:           s.Build,
                turn:            s.Turn);
            stations.Add(st);
        }

        return stations;
    }

    // ---------- detail ----------

    [HttpGet("{wellId:int}")]
    [ProducesResponseType<WellDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var well = await db.Wells
            .AsNoTracking()
            .Where(w => w.Id == wellId && w.JobId == jobId)
            .Select(w => new WellDetailDto(
                w.Id,
                w.Name,
                w.Type.Name,
                w.Surveys.Count,
                w.TieOns.Count,
                w.Tubulars.Count,
                w.Formations.Count,
                w.CommonMeasures.Count,
                w.CreatedAt,
                w.CreatedBy,
                w.UpdatedAt,
                w.UpdatedBy))
            .FirstOrDefaultAsync(ct);

        return well is null
            ? this.NotFoundProblem("Well", wellId.ToString())
            : Ok(well);
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<WellSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId,
        [FromBody] CreateWellDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<WellType>(dto.Type, out var wellType))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateWellDto.Type)] = [SmartEnumExtensions.UnknownNameMessage<WellType>(dto.Type)],
            });

        await using var db = dbFactory.CreateActive();
        if (!await db.JobExistsAsync(jobId, ct))
            return this.NotFoundProblem("Job", jobId.ToString());

        var well = new Well(jobId, dto.Name, wellType);
        db.Wells.Add(well);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                tenantCode = RouteData.Values["tenantCode"],
                jobId,
                wellId = well.Id,
            },
            new WellSummaryDto(
                well.Id, well.Name, well.Type.Name,
                SurveyCount: 0, TieOnCount: 0,
                well.CreatedAt));
    }

    // ---------- update ----------

    [HttpPut("{wellId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        [FromBody] UpdateWellDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<WellType>(dto.Type, out var wellType))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateWellDto.Type)] = [SmartEnumExtensions.UnknownNameMessage<WellType>(dto.Type)],
            });

        await using var db = dbFactory.CreateActive();
        var well = await db.Wells
            .FirstOrDefaultAsync(w => w.Id == wellId && w.JobId == jobId, ct);
        if (well is null) return this.NotFoundProblem("Well", wellId.ToString());

        well.Name = dto.Name;
        well.Type = wellType;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ---------- delete ----------

    [HttpDelete("{wellId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var well = await db.Wells
            .FirstOrDefaultAsync(w => w.Id == wellId && w.JobId == jobId, ct);
        if (well is null) return this.NotFoundProblem("Well", wellId.ToString());

        var hasChildren =
            await db.Surveys.AsNoTracking().AnyAsync(s => s.WellId == wellId, ct) ||
            await db.TieOns.AsNoTracking().AnyAsync(t => t.WellId == wellId, ct) ||
            await db.Tubulars.AsNoTracking().AnyAsync(t => t.WellId == wellId, ct) ||
            await db.Formations.AsNoTracking().AnyAsync(f => f.WellId == wellId, ct) ||
            await db.CommonMeasures.AsNoTracking().AnyAsync(c => c.WellId == wellId, ct);

        if (hasChildren)
            return this.ConflictProblem(
                "Well has child rows (Surveys, TieOns, Tubulars, Formations, " +
                "or CommonMeasures); delete or reparent them first.");

        db.Wells.Remove(well);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

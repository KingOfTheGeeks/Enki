using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
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
public sealed class WellsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
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

    // ---------- detail ----------

    [HttpGet("{wellId:int}")]
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

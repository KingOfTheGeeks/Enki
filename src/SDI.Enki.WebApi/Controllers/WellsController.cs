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

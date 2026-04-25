using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Wells under a tenant. In drilling-survey terms a Well is the physical
/// borehole, not a business record — they live at tenant level rather than
/// under a Job (a single Well can be referenced by multiple Jobs).
///
/// <para>
/// CRUD shape is the standard one: list / get / create / update / delete.
/// Delete is guarded — a Well with any child rows (Surveys, TieOns,
/// Tubulars, Formations, CommonMeasures) returns 409 ConflictProblem so
/// an accidental delete can't quietly drop a survey set the user spent
/// hours editing. Cascade-delete the children first, or add an explicit
/// "force delete" admin path later.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/wells")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class WellsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    public async Task<IEnumerable<WellSummaryDto>> List(CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        return await db.Wells
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new WellSummaryDto(
                w.Id, w.Name, w.Type.Name,
                w.Surveys.Count, w.TieOns.Count,
                w.CreatedAt))
            .ToListAsync(ct);
    }

    // ---------- detail ----------

    [HttpGet("{wellId:int}")]
    public async Task<IActionResult> Get(int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var well = await db.Wells
            .AsNoTracking()
            .Where(w => w.Id == wellId)
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
    public async Task<IActionResult> Create([FromBody] CreateWellDto dto, CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<WellType>(dto.Type, out var wellType))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateWellDto.Type)] = [SmartEnumExtensions.UnknownNameMessage<WellType>(dto.Type)],
            });

        await using var db = dbFactory.CreateActive();
        var well = new Well(dto.Name, wellType);
        db.Wells.Add(well);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], wellId = well.Id },
            new WellSummaryDto(
                well.Id, well.Name, well.Type.Name,
                SurveyCount: 0, TieOnCount: 0,
                well.CreatedAt));
    }

    // ---------- update ----------

    [HttpPut("{wellId:int}")]
    public async Task<IActionResult> Update(
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
        var well = await db.Wells.FirstOrDefaultAsync(w => w.Id == wellId, ct);
        if (well is null) return this.NotFoundProblem("Well", wellId.ToString());

        well.Name = dto.Name;
        well.Type = wellType;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ---------- delete ----------

    [HttpDelete("{wellId:int}")]
    public async Task<IActionResult> Delete(int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var well = await db.Wells.FirstOrDefaultAsync(w => w.Id == wellId, ct);
        if (well is null) return this.NotFoundProblem("Well", wellId.ToString());

        // Block delete when any child rows exist. Cascade is configured at
        // the DB level (each child entity has OnDelete(Cascade)), so a raw
        // DELETE WOULD remove them — but silently. Refuse and let the user
        // make an explicit decision.
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

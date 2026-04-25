using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Shared.Wells.Formations;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Geological formations intersected by a Well. Resistance values feed
/// into ranging calculations where surrounding rock conductivity matters.
/// List is ordered by <see cref="Formation.FromVertical"/> so the layout
/// reads surface-down. Domain rule:
/// <c>FromVertical &lt;= ToVertical</c> — enforced inline as 400
/// ValidationProblem.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/wells/{wellId:int}/formations")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class FormationsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var rows = await db.Formations
            .AsNoTracking()
            .Where(f => f.WellId == wellId)
            .OrderBy(f => f.FromVertical)
            .Select(f => new FormationSummaryDto(
                f.Id, f.WellId, f.Name,
                f.FromVertical, f.ToVertical, f.Resistance))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("{formationId:int}")]
    public async Task<IActionResult> Get(int wellId, int formationId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var dto = await db.Formations
            .AsNoTracking()
            .Where(f => f.Id == formationId && f.WellId == wellId)
            .Select(f => new FormationDetailDto(
                f.Id, f.WellId, f.Name, f.Description,
                f.FromVertical, f.ToVertical, f.Resistance,
                f.CreatedAt, f.CreatedBy, f.UpdatedAt, f.UpdatedBy))
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? this.NotFoundProblem("Formation", formationId.ToString())
            : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        int wellId,
        [FromBody] CreateFormationDto dto,
        CancellationToken ct)
    {
        if (dto.FromVertical > dto.ToVertical)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateFormationDto.FromVertical)] =
                    [$"FromVertical ({dto.FromVertical}) must be less than or equal to ToVertical ({dto.ToVertical})."],
            });

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var formation = new Formation(wellId, dto.Name, dto.FromVertical, dto.ToVertical, dto.Resistance)
        {
            Description = dto.Description,
        };
        db.Formations.Add(formation);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                tenantCode  = RouteData.Values["tenantCode"],
                wellId,
                formationId = formation.Id,
            },
            new FormationSummaryDto(
                formation.Id, formation.WellId, formation.Name,
                formation.FromVertical, formation.ToVertical, formation.Resistance));
    }

    [HttpPut("{formationId:int}")]
    public async Task<IActionResult> Update(
        int wellId,
        int formationId,
        [FromBody] UpdateFormationDto dto,
        CancellationToken ct)
    {
        if (dto.FromVertical > dto.ToVertical)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateFormationDto.FromVertical)] =
                    [$"FromVertical ({dto.FromVertical}) must be less than or equal to ToVertical ({dto.ToVertical})."],
            });

        await using var db = dbFactory.CreateActive();

        var formation = await db.Formations
            .FirstOrDefaultAsync(f => f.Id == formationId && f.WellId == wellId, ct);
        if (formation is null)
            return this.NotFoundProblem("Formation", formationId.ToString());

        formation.Name         = dto.Name;
        formation.Description  = dto.Description;
        formation.FromVertical = dto.FromVertical;
        formation.ToVertical   = dto.ToVertical;
        formation.Resistance   = dto.Resistance;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{formationId:int}")]
    public async Task<IActionResult> Delete(int wellId, int formationId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var formation = await db.Formations
            .FirstOrDefaultAsync(f => f.Id == formationId && f.WellId == wellId, ct);
        if (formation is null)
            return this.NotFoundProblem("Formation", formationId.ToString());

        db.Formations.Remove(formation);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

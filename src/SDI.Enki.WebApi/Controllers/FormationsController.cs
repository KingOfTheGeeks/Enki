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
/// Geological formations under a Well that lives under a Job. List
/// ordered by <see cref="Formation.FromVertical"/>. Domain rule:
/// <c>FromVertical &lt;= ToVertical</c>, enforced inline as 400
/// ValidationProblem on Create / Update.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/formations")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class FormationsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
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
    public async Task<IActionResult> Get(Guid jobId, int wellId, int formationId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

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
        Guid jobId,
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
        if (!await db.WellExistsAsync(jobId, wellId, ct))
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
                jobId,
                wellId,
                formationId = formation.Id,
            },
            new FormationSummaryDto(
                formation.Id, formation.WellId, formation.Name,
                formation.FromVertical, formation.ToVertical, formation.Resistance));
    }

    [HttpPut("{formationId:int}")]
    public async Task<IActionResult> Update(
        Guid jobId,
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
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

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
    public async Task<IActionResult> Delete(Guid jobId, int wellId, int formationId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var formation = await db.Formations
            .FirstOrDefaultAsync(f => f.Id == formationId && f.WellId == wellId, ct);
        if (formation is null)
            return this.NotFoundProblem("Formation", formationId.ToString());

        db.Formations.Remove(formation);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

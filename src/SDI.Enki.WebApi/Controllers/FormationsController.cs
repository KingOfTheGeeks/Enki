using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Shared.Wells.Formations;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
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
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class FormationsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<FormationSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var rows = await db.Formations
            .AsNoTracking()
            .Where(f => f.WellId == wellId)
            .OrderBy(f => f.FromVertical)
            .Select(f => new
            {
                f.Id, f.WellId, f.Name,
                f.FromVertical, f.ToVertical, f.Resistance,
                f.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(f => new FormationSummaryDto(
            f.Id, f.WellId, f.Name,
            f.FromVertical, f.ToVertical, f.Resistance,
            ConcurrencyHelper.EncodeRowVersion(f.RowVersion))));
    }

    [HttpGet("{formationId:int}")]
    [ProducesResponseType<FormationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, int wellId, int formationId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var row = await db.Formations
            .AsNoTracking()
            .Where(f => f.Id == formationId && f.WellId == wellId)
            .Select(f => new
            {
                f.Id, f.WellId, f.Name, f.Description,
                f.FromVertical, f.ToVertical, f.Resistance,
                f.CreatedAt, f.CreatedBy, f.UpdatedAt, f.UpdatedBy,
                f.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Formation", formationId.ToString());

        return Ok(new FormationDetailDto(
            row.Id, row.WellId, row.Name, row.Description,
            row.FromVertical, row.ToVertical, row.Resistance,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    [Authorize(Policy = EnkiPolicies.CanWriteTenantContent)]
    [HttpPost]
    [ProducesResponseType<FormationSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateFormationDto dto,
        CancellationToken ct)
    {
        if (this.ValidateDepthRange(
                dto.FromVertical, nameof(CreateFormationDto.FromVertical),
                dto.ToVertical,   nameof(CreateFormationDto.ToVertical)) is { } badRange)
            return badRange;

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
                formation.FromVertical, formation.ToVertical, formation.Resistance,
                formation.EncodeRowVersion()));
    }

    [Authorize(Policy = EnkiPolicies.CanWriteTenantContent)]
    [HttpPut("{formationId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        int formationId,
        [FromBody] UpdateFormationDto dto,
        CancellationToken ct)
    {
        if (this.ValidateDepthRange(
                dto.FromVertical, nameof(UpdateFormationDto.FromVertical),
                dto.ToVertical,   nameof(UpdateFormationDto.ToVertical)) is { } badRange)
            return badRange;

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var formation = await db.Formations
            .FirstOrDefaultAsync(f => f.Id == formationId && f.WellId == wellId, ct);
        if (formation is null)
            return this.NotFoundProblem("Formation", formationId.ToString());

        if (this.ApplyClientRowVersion(db, formation, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        formation.Name         = dto.Name;
        formation.Description  = dto.Description;
        formation.FromVertical = dto.FromVertical;
        formation.ToVertical   = dto.ToVertical;
        formation.Resistance   = dto.Resistance;

        if (await db.SaveOrConflictAsync(this, "Formation", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    [Authorize(Policy = EnkiPolicies.CanDeleteTenantContent)]
    [HttpDelete("{formationId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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

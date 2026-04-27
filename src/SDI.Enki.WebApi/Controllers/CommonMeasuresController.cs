using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Shared.Wells.CommonMeasures;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Common-measure depth-ranged scalars under a Well that lives under a
/// Job. List ordered by <see cref="CommonMeasure.FromVertical"/>.
/// Domain rule: <c>FromVertical &lt;= ToVertical</c>.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/common-measures")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class CommonMeasuresController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<CommonMeasureSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var rows = await db.CommonMeasures
            .AsNoTracking()
            .Where(c => c.WellId == wellId)
            .OrderBy(c => c.FromVertical)
            .Select(c => new CommonMeasureSummaryDto(
                c.Id, c.WellId, c.FromVertical, c.ToVertical, c.Value))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("{measureId:int}")]
    [ProducesResponseType<CommonMeasureDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, int wellId, int measureId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var dto = await db.CommonMeasures
            .AsNoTracking()
            .Where(c => c.Id == measureId && c.WellId == wellId)
            .Select(c => new CommonMeasureDetailDto(
                c.Id, c.WellId, c.FromVertical, c.ToVertical, c.Value,
                c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy))
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? this.NotFoundProblem("CommonMeasure", measureId.ToString())
            : Ok(dto);
    }

    [HttpPost]
    [ProducesResponseType<CommonMeasureSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateCommonMeasureDto dto,
        CancellationToken ct)
    {
        if (dto.FromVertical > dto.ToVertical)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateCommonMeasureDto.FromVertical)] =
                    [$"FromVertical ({dto.FromVertical}) must be less than or equal to ToVertical ({dto.ToVertical})."],
            });

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var measure = new CommonMeasure(wellId, dto.FromVertical, dto.ToVertical, dto.Value);
        db.CommonMeasures.Add(measure);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                tenantCode = RouteData.Values["tenantCode"],
                jobId,
                wellId,
                measureId = measure.Id,
            },
            new CommonMeasureSummaryDto(
                measure.Id, measure.WellId,
                measure.FromVertical, measure.ToVertical, measure.Value));
    }

    [HttpPut("{measureId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        int measureId,
        [FromBody] UpdateCommonMeasureDto dto,
        CancellationToken ct)
    {
        if (dto.FromVertical > dto.ToVertical)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateCommonMeasureDto.FromVertical)] =
                    [$"FromVertical ({dto.FromVertical}) must be less than or equal to ToVertical ({dto.ToVertical})."],
            });

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var measure = await db.CommonMeasures
            .FirstOrDefaultAsync(c => c.Id == measureId && c.WellId == wellId, ct);
        if (measure is null)
            return this.NotFoundProblem("CommonMeasure", measureId.ToString());

        measure.FromVertical = dto.FromVertical;
        measure.ToVertical   = dto.ToVertical;
        measure.Value        = dto.Value;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{measureId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid jobId, int wellId, int measureId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var measure = await db.CommonMeasures
            .FirstOrDefaultAsync(c => c.Id == measureId && c.WellId == wellId, ct);
        if (measure is null)
            return this.NotFoundProblem("CommonMeasure", measureId.ToString());

        db.CommonMeasures.Remove(measure);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

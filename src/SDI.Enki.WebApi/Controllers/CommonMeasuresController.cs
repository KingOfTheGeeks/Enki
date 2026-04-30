using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Shared.Wells.CommonMeasures;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
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
            .Select(c => new
            {
                c.Id, c.WellId, c.FromVertical, c.ToVertical, c.Value,
                c.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(c => new CommonMeasureSummaryDto(
            c.Id, c.WellId, c.FromVertical, c.ToVertical, c.Value,
            ConcurrencyHelper.EncodeRowVersion(c.RowVersion))));
    }

    [HttpGet("{measureId:int}")]
    [ProducesResponseType<CommonMeasureDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, int wellId, int measureId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var row = await db.CommonMeasures
            .AsNoTracking()
            .Where(c => c.Id == measureId && c.WellId == wellId)
            .Select(c => new
            {
                c.Id, c.WellId, c.FromVertical, c.ToVertical, c.Value,
                c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy,
                c.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("CommonMeasure", measureId.ToString());

        return Ok(new CommonMeasureDetailDto(
            row.Id, row.WellId, row.FromVertical, row.ToVertical, row.Value,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
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
        if (this.ValidateDepthRange(
                dto.FromVertical, nameof(CreateCommonMeasureDto.FromVertical),
                dto.ToVertical,   nameof(CreateCommonMeasureDto.ToVertical)) is { } badRange)
            return badRange;

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
                measure.FromVertical, measure.ToVertical, measure.Value,
                measure.EncodeRowVersion()));
    }

    [HttpPut("{measureId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        int measureId,
        [FromBody] UpdateCommonMeasureDto dto,
        CancellationToken ct)
    {
        if (this.ValidateDepthRange(
                dto.FromVertical, nameof(UpdateCommonMeasureDto.FromVertical),
                dto.ToVertical,   nameof(UpdateCommonMeasureDto.ToVertical)) is { } badRange)
            return badRange;

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var measure = await db.CommonMeasures
            .FirstOrDefaultAsync(c => c.Id == measureId && c.WellId == wellId, ct);
        if (measure is null)
            return this.NotFoundProblem("CommonMeasure", measureId.ToString());

        if (this.ApplyClientRowVersion(db, measure, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        measure.FromVertical = dto.FromVertical;
        measure.ToVertical   = dto.ToVertical;
        measure.Value        = dto.Value;

        if (await db.SaveOrConflictAsync(this, "CommonMeasure", ct) is { } conflict)
            return conflict;

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

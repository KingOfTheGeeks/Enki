using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Infrastructure.Surveys;
using SDI.Enki.Shared.Wells.CommonMeasures;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Common-measure depth-ranged scalars under a Well that lives under a
/// Job. List ordered by <see cref="CommonMeasure.FromMeasured"/>. Same
/// depth-model rules as Formation: MD is canonical (entered + stored),
/// TVD is derived on read via
/// <see cref="SurveyTvdResolver"/> (Marduk minimum-curvature
/// interpolation against the well's Surveys), and the MD interval must
/// fall inside the Survey envelope (≥ 2 surveys required).
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/common-measures")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class CommonMeasuresController(
    ITenantDbContextFactory dbFactory,
    SurveyTvdResolver tvdResolver) : ControllerBase
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
            .OrderBy(c => c.FromMeasured)
            .Select(c => new
            {
                c.Id, c.WellId, c.FromMeasured, c.ToMeasured, c.Value,
                c.RowVersion,
            })
            .ToListAsync(ct);

        var stations = await tvdResolver.LoadStationsAsync(db, wellId, ct);

        return Ok(rows.Select(c =>
        {
            var (fromTvd, toTvd) = tvdResolver.ResolvePair(stations, c.FromMeasured, c.ToMeasured);
            return new CommonMeasureSummaryDto(
                c.Id, c.WellId, c.FromMeasured, c.ToMeasured,
                fromTvd, toTvd,
                c.Value,
                ConcurrencyHelper.EncodeRowVersion(c.RowVersion));
        }));
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
                c.Id, c.WellId, c.FromMeasured, c.ToMeasured, c.Value,
                c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy,
                c.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("CommonMeasure", measureId.ToString());

        var (fromTvd, toTvd) = await tvdResolver.ResolveAsync(db, wellId, row.FromMeasured, row.ToMeasured, ct);

        return Ok(new CommonMeasureDetailDto(
            row.Id, row.WellId, row.FromMeasured, row.ToMeasured,
            fromTvd, toTvd,
            row.Value,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    [Authorize(Policy = EnkiPolicies.CanWriteTenantContent)]
    [HttpPost]
    [ProducesResponseType<CommonMeasureSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateCommonMeasureDto dto,
        CancellationToken ct)
    {
        if (this.ValidateDepthRange(
                dto.FromMeasured, nameof(CreateCommonMeasureDto.FromMeasured),
                dto.ToMeasured,   nameof(CreateCommonMeasureDto.ToMeasured)) is { } badRange)
            return badRange;

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        if (await this.ValidateAgainstSurveyRangeAsync(
                db, wellId,
                dto.FromMeasured, nameof(CreateCommonMeasureDto.FromMeasured),
                dto.ToMeasured,   nameof(CreateCommonMeasureDto.ToMeasured),
                ct) is { } outOfEnvelope)
            return outOfEnvelope;

        var measure = new CommonMeasure(wellId, dto.FromMeasured, dto.ToMeasured, dto.Value);
        db.CommonMeasures.Add(measure);
        await db.SaveChangesAsync(ct);

        var (fromTvd, toTvd) = await tvdResolver.ResolveAsync(db, wellId, measure.FromMeasured, measure.ToMeasured, ct);

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
                measure.FromMeasured, measure.ToMeasured,
                fromTvd, toTvd,
                measure.Value,
                measure.EncodeRowVersion()));
    }

    [Authorize(Policy = EnkiPolicies.CanWriteTenantContent)]
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
                dto.FromMeasured, nameof(UpdateCommonMeasureDto.FromMeasured),
                dto.ToMeasured,   nameof(UpdateCommonMeasureDto.ToMeasured)) is { } badRange)
            return badRange;

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Existence check before survey-range check — REST: 404 wins
        // over 409 when the requested resource doesn't exist.
        var measure = await db.CommonMeasures
            .FirstOrDefaultAsync(c => c.Id == measureId && c.WellId == wellId, ct);
        if (measure is null)
            return this.NotFoundProblem("CommonMeasure", measureId.ToString());

        if (await this.ValidateAgainstSurveyRangeAsync(
                db, wellId,
                dto.FromMeasured, nameof(UpdateCommonMeasureDto.FromMeasured),
                dto.ToMeasured,   nameof(UpdateCommonMeasureDto.ToMeasured),
                ct) is { } outOfEnvelope)
            return outOfEnvelope;

        if (this.ApplyClientRowVersion(db, measure, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        measure.FromMeasured = dto.FromMeasured;
        measure.ToMeasured   = dto.ToMeasured;
        measure.Value        = dto.Value;

        if (await db.SaveOrConflictAsync(this, "CommonMeasure", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    [Authorize(Policy = EnkiPolicies.CanDeleteTenantContent)]
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

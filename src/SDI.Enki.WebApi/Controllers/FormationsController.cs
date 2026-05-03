using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Infrastructure.Surveys;
using SDI.Enki.Shared.Wells.Formations;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Geological formations under a Well that lives under a Job. List
/// ordered by <see cref="Formation.FromMeasured"/>. Domain rules:
/// <list type="bullet">
///   <item><c>FromMeasured &lt;= ToMeasured</c>, enforced inline as
///   400 ValidationProblem on Create / Update.</item>
///   <item>The MD interval must fall inside the well's Survey MD
///   envelope (the well must already have ≥ 2 surveys, and both ends
///   must bracket within their range). Surfaces as 409 Conflict
///   ("insufficientSurveys") or 400 ValidationProblem on a
///   per-field range violation.</item>
/// </list>
/// TVD on a Formation is <i>not</i> stored — read responses derive
/// it from the well's Surveys via
/// <see cref="SurveyTvdResolver"/> (Marduk minimum-curvature
/// interpolation), so the same math that computed each Survey's
/// <c>VerticalDepth</c> column is what produces the Formation's
/// <c>FromTvd</c> / <c>ToTvd</c>.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/formations")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class FormationsController(
    ITenantDbContextFactory dbFactory,
    SurveyTvdResolver tvdResolver) : ControllerBase
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
            .OrderBy(f => f.FromMeasured)
            .Select(f => new
            {
                f.Id, f.WellId, f.Name,
                f.FromMeasured, f.ToMeasured, f.Resistance,
                f.RowVersion,
            })
            .ToListAsync(ct);

        // Load survey stations once and resolve TVD for every row in
        // the same pass — one DB round-trip + one interpolation pass
        // beats N round-trips through ResolveAsync.
        var stations = await tvdResolver.LoadStationsAsync(db, wellId, ct);

        return Ok(rows.Select(f =>
        {
            var (fromTvd, toTvd) = tvdResolver.ResolvePair(stations, f.FromMeasured, f.ToMeasured);
            return new FormationSummaryDto(
                f.Id, f.WellId, f.Name,
                f.FromMeasured, f.ToMeasured,
                fromTvd, toTvd,
                f.Resistance,
                ConcurrencyHelper.EncodeRowVersion(f.RowVersion));
        }));
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
                f.FromMeasured, f.ToMeasured, f.Resistance,
                f.CreatedAt, f.CreatedBy, f.UpdatedAt, f.UpdatedBy,
                f.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Formation", formationId.ToString());

        var (fromTvd, toTvd) = await tvdResolver.ResolveAsync(db, wellId, row.FromMeasured, row.ToMeasured, ct);

        return Ok(new FormationDetailDto(
            row.Id, row.WellId, row.Name, row.Description,
            row.FromMeasured, row.ToMeasured,
            fromTvd, toTvd,
            row.Resistance,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    [Authorize(Policy = EnkiPolicies.CanWriteTenantContent)]
    [HttpPost]
    [ProducesResponseType<FormationSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateFormationDto dto,
        CancellationToken ct)
    {
        if (this.ValidateDepthRange(
                dto.FromMeasured, nameof(CreateFormationDto.FromMeasured),
                dto.ToMeasured,   nameof(CreateFormationDto.ToMeasured)) is { } badRange)
            return badRange;

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        if (await this.ValidateAgainstSurveyRangeAsync(
                db, wellId,
                dto.FromMeasured, nameof(CreateFormationDto.FromMeasured),
                dto.ToMeasured,   nameof(CreateFormationDto.ToMeasured),
                ct) is { } outOfEnvelope)
            return outOfEnvelope;

        var formation = new Formation(wellId, dto.Name, dto.FromMeasured, dto.ToMeasured, dto.Resistance)
        {
            Description = dto.Description,
        };
        db.Formations.Add(formation);
        await db.SaveChangesAsync(ct);

        var (fromTvd, toTvd) = await tvdResolver.ResolveAsync(db, wellId, formation.FromMeasured, formation.ToMeasured, ct);

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
                formation.FromMeasured, formation.ToMeasured,
                fromTvd, toTvd,
                formation.Resistance,
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
                dto.FromMeasured, nameof(UpdateFormationDto.FromMeasured),
                dto.ToMeasured,   nameof(UpdateFormationDto.ToMeasured)) is { } badRange)
            return badRange;

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Existence check before survey-range check so a missing
        // entity surfaces as 404 rather than 409 — REST: a request
        // for a resource that doesn't exist always wins over policy
        // checks against the parent.
        var formation = await db.Formations
            .FirstOrDefaultAsync(f => f.Id == formationId && f.WellId == wellId, ct);
        if (formation is null)
            return this.NotFoundProblem("Formation", formationId.ToString());

        if (await this.ValidateAgainstSurveyRangeAsync(
                db, wellId,
                dto.FromMeasured, nameof(UpdateFormationDto.FromMeasured),
                dto.ToMeasured,   nameof(UpdateFormationDto.ToMeasured),
                ct) is { } outOfEnvelope)
            return outOfEnvelope;

        if (this.ApplyClientRowVersion(db, formation, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        formation.Name         = dto.Name;
        formation.Description  = dto.Description;
        formation.FromMeasured = dto.FromMeasured;
        formation.ToMeasured   = dto.ToMeasured;
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

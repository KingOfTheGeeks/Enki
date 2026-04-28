using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells.Tubulars;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Tubular segments under a Well that lives under a Job. List ordered
/// by <see cref="Tubular.Order"/> (surface = 0, increasing downward).
/// Type (Casing / Liner / Tubing / DrillPipe / OpenHole) is a
/// SmartEnum; controller parses it via
/// <see cref="SmartEnumExtensions.TryFromName{TEnum}"/>.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/tubulars")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class TubularsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<TubularSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Two-stage projection so RowVersion can be base64-encoded.
        var rows = await db.Tubulars
            .AsNoTracking()
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Order)
            .Select(t => new
            {
                t.Id, t.WellId, t.Name, t.Order, TypeName = t.Type.Name,
                t.FromMeasured, t.ToMeasured, t.Diameter, t.Weight,
                t.RowVersion,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(t => new TubularSummaryDto(
            t.Id, t.WellId, t.Name, t.Order, t.TypeName,
            t.FromMeasured, t.ToMeasured, t.Diameter, t.Weight,
            ConcurrencyHelper.EncodeRowVersion(t.RowVersion))));
    }

    [HttpGet("{tubularId:int}")]
    [ProducesResponseType<TubularDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, int wellId, int tubularId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var row = await db.Tubulars
            .AsNoTracking()
            .Where(t => t.Id == tubularId && t.WellId == wellId)
            .Select(t => new
            {
                t.Id, t.WellId, t.Name, t.Order, TypeName = t.Type.Name,
                t.FromMeasured, t.ToMeasured, t.Diameter, t.Weight,
                t.CreatedAt, t.CreatedBy, t.UpdatedAt, t.UpdatedBy,
                t.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Tubular", tubularId.ToString());

        return Ok(new TubularDetailDto(
            row.Id, row.WellId, row.Name, row.Order, row.TypeName,
            row.FromMeasured, row.ToMeasured, row.Diameter, row.Weight,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    [HttpPost]
    [ProducesResponseType<TubularSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateTubularDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<TubularType>(dto.Type, out var type))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateTubularDto.Type)] = [SmartEnumExtensions.UnknownNameMessage<TubularType>(dto.Type)],
            });

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tubular = new Tubular(
            wellId, dto.Order, type,
            dto.FromMeasured, dto.ToMeasured,
            dto.Diameter, dto.Weight)
        {
            Name = dto.Name,
        };
        db.Tubulars.Add(tubular);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                tenantCode = RouteData.Values["tenantCode"],
                jobId,
                wellId,
                tubularId = tubular.Id,
            },
            new TubularSummaryDto(
                tubular.Id, tubular.WellId, tubular.Name, tubular.Order, tubular.Type.Name,
                tubular.FromMeasured, tubular.ToMeasured, tubular.Diameter, tubular.Weight,
                tubular.EncodeRowVersion()));
    }

    [HttpPut("{tubularId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        int tubularId,
        [FromBody] UpdateTubularDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<TubularType>(dto.Type, out var type))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateTubularDto.Type)] = [SmartEnumExtensions.UnknownNameMessage<TubularType>(dto.Type)],
            });

        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tubular = await db.Tubulars
            .FirstOrDefaultAsync(t => t.Id == tubularId && t.WellId == wellId, ct);
        if (tubular is null)
            return this.NotFoundProblem("Tubular", tubularId.ToString());

        if (this.ApplyClientRowVersion(tubular, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        tubular.Name         = dto.Name;
        tubular.Order        = dto.Order;
        tubular.Type         = type;
        tubular.FromMeasured = dto.FromMeasured;
        tubular.ToMeasured   = dto.ToMeasured;
        tubular.Diameter     = dto.Diameter;
        tubular.Weight       = dto.Weight;

        if (await db.SaveOrConflictAsync(this, "Tubular", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    [HttpDelete("{tubularId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid jobId, int wellId, int tubularId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tubular = await db.Tubulars
            .FirstOrDefaultAsync(t => t.Id == tubularId && t.WellId == wellId, ct);
        if (tubular is null)
            return this.NotFoundProblem("Tubular", tubularId.ToString());

        db.Tubulars.Remove(tubular);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

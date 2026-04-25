using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells.Tubulars;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Tubular segments under a Well — the drillstring composition. List
/// is ordered by <see cref="Tubular.Order"/> (surface = 0, increasing
/// downward) so the grid reads top-down. Type (Casing / Liner /
/// Tubing / DrillPipe / OpenHole) is a SmartEnum; the controller
/// parses it via <see cref="SmartEnumExtensions.TryFromName{TEnum}"/>
/// and 400s on an unknown value.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/wells/{wellId:int}/tubulars")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class TubularsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var rows = await db.Tubulars
            .AsNoTracking()
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Order)
            .Select(t => new TubularSummaryDto(
                t.Id, t.WellId, t.Name, t.Order, t.Type.Name,
                t.FromMeasured, t.ToMeasured, t.Diameter, t.Weight))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("{tubularId:int}")]
    public async Task<IActionResult> Get(int wellId, int tubularId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var dto = await db.Tubulars
            .AsNoTracking()
            .Where(t => t.Id == tubularId && t.WellId == wellId)
            .Select(t => new TubularDetailDto(
                t.Id, t.WellId, t.Name, t.Order, t.Type.Name,
                t.FromMeasured, t.ToMeasured, t.Diameter, t.Weight,
                t.CreatedAt, t.CreatedBy, t.UpdatedAt, t.UpdatedBy))
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? this.NotFoundProblem("Tubular", tubularId.ToString())
            : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
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
        if (!await db.WellExistsAsync(wellId, ct))
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
                wellId,
                tubularId  = tubular.Id,
            },
            new TubularSummaryDto(
                tubular.Id, tubular.WellId, tubular.Name, tubular.Order, tubular.Type.Name,
                tubular.FromMeasured, tubular.ToMeasured, tubular.Diameter, tubular.Weight));
    }

    [HttpPut("{tubularId:int}")]
    public async Task<IActionResult> Update(
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

        var tubular = await db.Tubulars
            .FirstOrDefaultAsync(t => t.Id == tubularId && t.WellId == wellId, ct);
        if (tubular is null)
            return this.NotFoundProblem("Tubular", tubularId.ToString());

        tubular.Name         = dto.Name;
        tubular.Order        = dto.Order;
        tubular.Type         = type;
        tubular.FromMeasured = dto.FromMeasured;
        tubular.ToMeasured   = dto.ToMeasured;
        tubular.Diameter     = dto.Diameter;
        tubular.Weight       = dto.Weight;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{tubularId:int}")]
    public async Task<IActionResult> Delete(int wellId, int tubularId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var tubular = await db.Tubulars
            .FirstOrDefaultAsync(t => t.Id == tubularId && t.WellId == wellId, ct);
        if (tubular is null)
            return this.NotFoundProblem("Tubular", tubularId.ToString());

        db.Tubulars.Remove(tubular);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

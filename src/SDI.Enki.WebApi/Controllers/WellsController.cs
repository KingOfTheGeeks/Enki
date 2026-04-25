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
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/wells")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class WellsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<WellSummaryDto>> List(CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        return await db.Wells
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new WellSummaryDto(w.Id, w.Name, w.Type.Name))
            .ToListAsync(ct);
    }

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
                w.CommonMeasures.Count))
            .FirstOrDefaultAsync(ct);

        return well is null
            ? this.NotFoundProblem("Well", wellId.ToString())
            : Ok(well);
    }

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
            new WellSummaryDto(well.Id, well.Name, well.Type.Name));
    }
}

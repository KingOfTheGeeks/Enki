using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Jobs under a specific tenant. The <c>tenantCode</c> route value triggers
/// <see cref="TenantRoutingMiddleware"/> which resolves to the right
/// tenant DB connection — the controller just asks the factory for a context.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "EnkiApiScope")]
public sealed class JobsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<JobSummaryDto>> List(CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        return await db.Jobs
            .AsNoTracking()
            .OrderByDescending(j => j.EntityCreated)
            .Select(j => new JobSummaryDto(
                j.Id, j.Name, j.WellName, j.Description,
                j.Status.Name, j.Units.Name,
                j.StartTimestamp, j.EndTimestamp))
            .ToListAsync(ct);
    }

    [HttpGet("{jobId:int}")]
    public async Task<IActionResult> Get(int jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        return job is null ? NotFound() : Ok(ToDto(job));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJobDto dto, CancellationToken ct)
    {
        if (!TryParseUnits(dto.Units, out var units))
            return BadRequest(new { error = $"Unknown Units value '{dto.Units}'. Expected Imperial or Metric." });

        await using var db = dbFactory.CreateActive();
        var job = new Job(dto.Name, dto.Description, units)
        {
            WellName       = dto.WellName,
            StartTimestamp = dto.StartTimestamp ?? DateTimeOffset.UtcNow,
            EndTimestamp   = dto.EndTimestamp   ?? DateTimeOffset.UtcNow,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        // tenantCode is part of the GET route template so CreatedAtAction
        // must have it in the route values to build the Location header.
        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId = job.Id },
            ToDto(job));
    }

    private static bool TryParseUnits(string name, out Units units)
    {
        var match = Units.List.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) { units = null!; return false; }
        units = match;
        return true;
    }

    private static JobSummaryDto ToDto(Job j) => new(
        j.Id, j.Name, j.WellName, j.Description,
        j.Status.Name, j.Units.Name,
        j.StartTimestamp, j.EndTimestamp);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Jobs under a specific tenant. The <c>tenantCode</c> route value triggers
/// <c>TenantRoutingMiddleware</c> which resolves to the right tenant DB
/// connection — the controller just asks the factory for a context.
///
/// Authorization: <see cref="EnkiPolicies.CanAccessTenant"/> requires the
/// caller to be a member of the tenant (TenantUser row) or hold the
/// <c>enki-admin</c> role. Master-registry endpoints on
/// <c>TenantsController</c> stay on the simpler
/// <see cref="EnkiPolicies.EnkiApiScope"/> — any authenticated caller
/// with the <c>enki</c> scope can list tenants, but drilling into a
/// tenant's jobs requires membership.
///
/// Error surface matches <c>TenantsController</c>: expected failures
/// (unknown job id → 404, bad status transition → 409, bad unit name →
/// 400) return ProblemDetails via <see cref="EnkiResults"/>. Validation
/// failures from DataAnnotations on the DTOs are caught by
/// <c>[ApiController]</c>'s automatic ModelState check and emerge as
/// ASP.NET Core's default 400 <c>ValidationProblemDetails</c>.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class JobsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    public async Task<IEnumerable<JobSummaryDto>> List(CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        return await db.Jobs
            .AsNoTracking()
            .OrderByDescending(j => j.EntityCreated)
            .Select(j => new JobSummaryDto(
                j.Id, j.Name, j.WellName, j.Region, j.Description,
                j.Status.Name, j.Units.Name,
                j.StartTimestamp, j.EndTimestamp))
            .ToListAsync(ct);
    }

    // ---------- detail ----------

    [HttpGet("{jobId:int}")]
    public async Task<IActionResult> Get(int jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return this.NotFoundProblem("Job", jobId.ToString());
        return Ok(ToDetail(job));
    }

    // ---------- create ----------

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJobDto dto, CancellationToken ct)
    {
        if (!TryParseUnits(dto.Units, out var units))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateJobDto.Units)] = [$"Unknown Units value '{dto.Units}'. Expected Imperial or Metric."],
            });

        await using var db = dbFactory.CreateActive();
        var job = new Job(dto.Name, dto.Description, units)
        {
            WellName       = dto.WellName,
            Region         = dto.Region,
            StartTimestamp = dto.StartTimestamp ?? DateTimeOffset.UtcNow,
            EndTimestamp   = dto.EndTimestamp   ?? DateTimeOffset.UtcNow,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        // tenantCode is part of the GET route template so CreatedAtAction
        // must include it in the route values to build the Location header.
        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId = job.Id },
            ToDetail(job));
    }

    // ---------- update ----------

    [HttpPut("{jobId:int}")]
    public async Task<IActionResult> Update(
        int jobId,
        [FromBody] UpdateJobDto dto,
        CancellationToken ct)
    {
        if (!TryParseUnits(dto.Units, out var units))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateJobDto.Units)] = [$"Unknown Units value '{dto.Units}'. Expected Imperial or Metric."],
            });

        await using var db = dbFactory.CreateActive();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return this.NotFoundProblem("Job", jobId.ToString());

        if (job.Status == JobStatus.Archived)
            return this.ConflictProblem(
                "Archived jobs are read-only. Restore to Active before editing.");

        job.Name        = dto.Name;
        job.Description = dto.Description;
        job.Units       = units;
        job.WellName    = dto.WellName;
        job.Region      = dto.Region;
        if (dto.StartTimestamp.HasValue) job.StartTimestamp = dto.StartTimestamp.Value;
        if (dto.EndTimestamp.HasValue)   job.EndTimestamp   = dto.EndTimestamp.Value;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- archive ----------

    [HttpPost("{jobId:int}/archive")]
    public async Task<IActionResult> Archive(int jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return this.NotFoundProblem("Job", jobId.ToString());

        // Archived is a terminal state — no-op if already there, rather
        // than 409. Matches the idempotent-archive pattern we use on
        // Tenant deactivation.
        if (job.Status == JobStatus.Archived)
            return NoContent();

        job.Status = JobStatus.Archived;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- helpers ----------

    private static bool TryParseUnits(string name, out Units units)
    {
        var match = Units.List.FirstOrDefault(
            u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) { units = null!; return false; }
        units = match;
        return true;
    }

    private static JobDetailDto ToDetail(Job j) => new(
        j.Id, j.Name, j.WellName, j.Region, j.Description,
        j.Status.Name, j.Units.Name,
        j.EntityCreated, j.StartTimestamp, j.EndTimestamp,
        j.LogoName);
}

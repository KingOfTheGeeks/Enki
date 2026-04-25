using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.Units;
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
/// <see cref="EnkiPolicies.EnkiApiScope"/>.
///
/// <para>
/// Error surface matches <c>TenantsController</c>: expected failures
/// (unknown job id → 404, illegal transition → 409, bad unit-system name
/// → 400) return ProblemDetails via <see cref="EnkiResults"/>.
/// DataAnnotations failures emerge as ASP.NET's default 400
/// <c>ValidationProblemDetails</c> via the <c>[ApiController]</c>
/// auto-ModelState check.
/// </para>
///
/// <para>
/// Lifecycle endpoints (<c>activate</c>, <c>archive</c>) all funnel
/// through <see cref="TransitionAsync"/> which consults
/// <see cref="JobLifecycle.CanTransition"/>. Adding a new endpoint is
/// two lines — see the class-level comment on <see cref="JobLifecycle"/>.
/// </para>
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
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobSummaryDto(
                j.Id, j.Name, j.WellName, j.Region, j.Description,
                j.Status.Name, j.UnitSystem.Name,
                j.StartTimestamp, j.EndTimestamp))
            .ToListAsync(ct);
    }

    // ---------- detail ----------

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        // Inline projection so j.Wells.Count compiles to a correlated
        // COUNT(*) subquery and we don't need to load the entity +
        // count children separately. Same shape as the Wells →
        // children-count projection on WellsController.Get.
        var dto = await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new JobDetailDto(
                j.Id, j.Name, j.WellName, j.Region, j.Description,
                j.Status.Name, j.UnitSystem.Name,
                j.CreatedAt, j.CreatedBy, j.UpdatedAt, j.UpdatedBy,
                j.StartTimestamp, j.EndTimestamp,
                j.LogoName,
                j.Wells.Count))
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? this.NotFoundProblem("Job", jobId.ToString())
            : Ok(dto);
    }

    // ---------- create ----------

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJobDto dto, CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<UnitSystem>(dto.UnitSystem, out var unitSystem, UnitSystem.Custom))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateJobDto.UnitSystem)] = [SmartEnumExtensions.UnknownNameMessage<UnitSystem>(dto.UnitSystem, UnitSystem.Custom)],
            });

        await using var db = dbFactory.CreateActive();
        var job = new Job(dto.Name, dto.Description, unitSystem)
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

    [HttpPut("{jobId:guid}")]
    public async Task<IActionResult> Update(
        Guid jobId,
        [FromBody] UpdateJobDto dto,
        CancellationToken ct)
    {
        if (!SmartEnumExtensions.TryFromName<UnitSystem>(dto.UnitSystem, out var unitSystem, UnitSystem.Custom))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateJobDto.UnitSystem)] = [SmartEnumExtensions.UnknownNameMessage<UnitSystem>(dto.UnitSystem, UnitSystem.Custom)],
            });

        await using var db = dbFactory.CreateActive();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return this.NotFoundProblem("Job", jobId.ToString());

        if (job.Status == JobStatus.Archived)
            return this.ConflictProblem(
                "Archived jobs are read-only. Restore to Active before editing.");

        job.Name        = dto.Name;
        job.Description = dto.Description;
        job.UnitSystem  = unitSystem;
        job.WellName    = dto.WellName;
        job.Region      = dto.Region;
        if (dto.StartTimestamp.HasValue) job.StartTimestamp = dto.StartTimestamp.Value;
        if (dto.EndTimestamp.HasValue)   job.EndTimestamp   = dto.EndTimestamp.Value;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- lifecycle transitions ----------
    // Each endpoint is a thin delegate to TransitionAsync. Adding a new
    // transition (e.g. "/complete") means: update JobLifecycle, copy one
    // of these methods, point it at the new target. Nothing else changes.

    [HttpPost("{jobId:guid}/activate")]
    public Task<IActionResult> Activate(Guid jobId, CancellationToken ct) =>
        TransitionAsync(jobId, JobStatus.Active, ct);

    [HttpPost("{jobId:guid}/archive")]
    public Task<IActionResult> Archive(Guid jobId, CancellationToken ct) =>
        TransitionAsync(jobId, JobStatus.Archived, ct);

    // ---------- helpers ----------

    /// <summary>
    /// Core of the lifecycle: look up the job, check the transition is
    /// allowed per <see cref="JobLifecycle"/>, apply, save. Same-status
    /// is a no-op returning 204 — matches the idempotent pattern we use
    /// for Tenant deactivate/reactivate.
    /// </summary>
    private async Task<IActionResult> TransitionAsync(
        Guid jobId, JobStatus target, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return this.NotFoundProblem("Job", jobId.ToString());

        if (job.Status == target) return NoContent();

        if (!JobLifecycle.CanTransition(job.Status, target))
            return this.ConflictProblem(
                $"Cannot transition job from {job.Status.Name} to {target.Name}.");

        job.Status = target;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Maps an in-memory <see cref="Job"/> to its detail DTO. Used by
    /// the Create response (where the entity is freshly inserted and
    /// the well count is known to be zero) — Get's projection-based
    /// path avoids this helper so EF can translate the well-count
    /// subquery to SQL.
    /// </summary>
    private static JobDetailDto ToDetail(Job j, int wellCount = 0) => new(
        j.Id, j.Name, j.WellName, j.Region, j.Description,
        j.Status.Name, j.UnitSystem.Name,
        j.CreatedAt, j.CreatedBy, j.UpdatedAt, j.UpdatedBy,
        j.StartTimestamp, j.EndTimestamp,
        j.LogoName,
        wellCount);
}

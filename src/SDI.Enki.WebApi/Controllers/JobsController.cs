using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
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
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class JobsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<JobSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<JobSummaryDto>> List(CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        var rows = await db.Jobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new
            {
                j.Id, j.Name, j.WellName, j.Region, j.Description,
                StatusName = j.Status.Name, UnitSystemName = j.UnitSystem.Name,
                j.StartTimestamp, j.EndTimestamp,
                j.RowVersion,
            })
            .ToListAsync(ct);

        return rows.Select(j => new JobSummaryDto(
            j.Id, j.Name, j.WellName, j.Region, j.Description,
            j.StatusName, j.UnitSystemName,
            j.StartTimestamp, j.EndTimestamp,
            ConcurrencyHelper.EncodeRowVersion(j.RowVersion)));
    }

    // ---------- detail ----------

    [HttpGet("{jobId:guid}")]
    [ProducesResponseType<JobDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        // Inline projection so j.Wells.Count compiles to a correlated
        // COUNT(*) subquery and we don't need to load the entity +
        // count children separately. Same shape as the Wells →
        // children-count projection on WellsController.Get.
        var row = await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                j.Id, j.Name, j.WellName, j.Region, j.Description,
                StatusName = j.Status.Name, UnitSystemName = j.UnitSystem.Name,
                j.CreatedAt, j.CreatedBy, j.UpdatedAt, j.UpdatedBy,
                j.StartTimestamp, j.EndTimestamp,
                j.LogoName,
                WellCount = j.Wells.Count,
                j.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Job", jobId.ToString());

        return Ok(new JobDetailDto(
            row.Id, row.Name, row.WellName, row.Region, row.Description,
            row.StatusName, row.UnitSystemName,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            row.StartTimestamp, row.EndTimestamp,
            row.LogoName,
            row.WellCount,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    // ---------- create ----------

    [HttpPost]
    [ProducesResponseType<JobDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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

        if (this.ApplyClientRowVersion(job, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        job.Name        = dto.Name;
        job.Description = dto.Description;
        job.UnitSystem  = unitSystem;
        job.WellName    = dto.WellName;
        job.Region      = dto.Region;
        if (dto.StartTimestamp.HasValue) job.StartTimestamp = dto.StartTimestamp.Value;
        if (dto.EndTimestamp.HasValue)   job.EndTimestamp   = dto.EndTimestamp.Value;

        if (await db.SaveOrConflictAsync(this, "Job", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // ---------- lifecycle transitions ----------
    // Each endpoint is a thin delegate to TransitionAsync. Adding a new
    // transition (e.g. "/complete") means: update JobLifecycle, copy one
    // of these methods, point it at the new target. Nothing else changes.

    [HttpPost("{jobId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Activate(Guid jobId, CancellationToken ct) =>
        TransitionAsync(jobId, JobStatus.Active, ct);

    [HttpPost("{jobId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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
        wellCount,
        j.EncodeRowVersion());
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Shared.Runs;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Runs under a Job. Gradient runs are expected to carry BridleLength +
/// CurrentInjection; Rotary and Passive runs do not. Client-supplied values
/// for those fields on non-Gradient runs are ignored, not rejected.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/runs")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class RunsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid jobId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        // Guard: Job must exist (otherwise a made-up jobId silently returns empty list).
        var jobExists = await db.Jobs.AnyAsync(j => j.Id == jobId, ct);
        if (!jobExists) return this.NotFoundProblem("Job", jobId.ToString());

        var runs = await db.Runs
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RunSummaryDto(
                r.Id, r.Name, r.Description,
                r.Type.Name, r.Status.Name,
                r.StartDepth, r.EndDepth,
                r.StartTimestamp, r.EndTimestamp))
            .ToListAsync(ct);

        return Ok(runs);
    }

    [HttpGet("{runId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var run = await db.Runs
            .AsNoTracking()
            .Include(r => r.Operators)
            .FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);

        if (run is null) return this.NotFoundProblem("Run", runId.ToString());

        return Ok(new RunDetailDto(
            run.Id, run.JobId, run.Name, run.Description,
            run.Type.Name, run.Status.Name,
            run.StartDepth, run.EndDepth,
            run.StartTimestamp, run.EndTimestamp, run.CreatedAt,
            run.BridleLength, run.CurrentInjection,
            run.Operators.Select(o => o.Name).ToList()));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid jobId, [FromBody] CreateRunDto dto, CancellationToken ct)
    {
        if (!TryParseRunType(dto.Type, out var runType))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateRunDto.Type)] = [$"Unknown Run Type '{dto.Type}'. Expected Gradient, Rotary, or Passive."],
            });

        await using var db = dbFactory.CreateActive();

        var jobExists = await db.Jobs.AnyAsync(j => j.Id == jobId, ct);
        if (!jobExists) return this.NotFoundProblem("Job", jobId.ToString());

        var run = new Run(dto.Name, dto.Description, dto.StartDepth, dto.EndDepth, runType)
        {
            JobId           = jobId,
            StartTimestamp  = dto.StartTimestamp,
            EndTimestamp    = dto.EndTimestamp,
            BridleLength    = runType == RunType.Gradient ? dto.BridleLength     : null,
            CurrentInjection = runType == RunType.Gradient ? dto.CurrentInjection : null,
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId = run.Id },
            new RunSummaryDto(
                run.Id, run.Name, run.Description,
                run.Type.Name, run.Status.Name,
                run.StartDepth, run.EndDepth,
                run.StartTimestamp, run.EndTimestamp));
    }

    private static bool TryParseRunType(string name, out RunType runType)
    {
        var match = RunType.List.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) { runType = null!; return false; }
        runType = match;
        return true;
    }
}

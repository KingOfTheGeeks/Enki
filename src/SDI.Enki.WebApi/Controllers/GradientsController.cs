using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Shared.Gradients;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Gradients under a Run. Gradients are logical groupings of Gradient shots
/// within a gradient-type run — analysts split measurements into primary /
/// retake clusters via the parent/child hierarchy on <see cref="Gradient"/>.
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:int}/runs/{runId:guid}/gradients")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "EnkiApiScope")]
public sealed class GradientsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int jobId, Guid runId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        // Guard: Run must exist, belong to the given Job, and be a Gradient run.
        var run = await db.Runs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return NotFound(new { error = $"Run {runId} not found in Job {jobId}." });
        if (run.Type != RunType.Gradient)
            return BadRequest(new { error = $"Run {runId} is type '{run.Type.Name}', not Gradient." });

        var items = await db.Gradients
            .AsNoTracking()
            .Where(g => g.RunId == runId)
            .OrderBy(g => g.Order)
            .Select(g => new GradientSummaryDto(
                g.Id, g.Name, g.Order, g.IsValid, g.RunId, g.ParentId, g.Timestamp,
                g.Shots.Count))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{gradientId:int}")]
    public async Task<IActionResult> Get(int jobId, Guid runId, int gradientId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var dto = await db.Gradients
            .AsNoTracking()
            .Where(g => g.Id == gradientId && g.RunId == runId)
            .Select(g => new GradientDetailDto(
                g.Id, g.Name, g.Order, g.IsValid, g.RunId,
                g.ParentId, g.Timestamp, g.Voltage, g.Frequency, g.Frame,
                g.Shots.Count, g.Solutions.Count, g.Files.Count, g.Comments.Count))
            .FirstOrDefaultAsync(ct);

        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int jobId, Guid runId, [FromBody] CreateGradientDto dto, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == runId && r.JobId == jobId, ct);
        if (run is null) return NotFound(new { error = $"Run {runId} not found in Job {jobId}." });
        if (run.Type != RunType.Gradient)
            return BadRequest(new { error = $"Run {runId} is type '{run.Type.Name}', not Gradient." });

        // If a parent is specified, make sure it belongs to the same Run.
        if (dto.ParentId is int parentId)
        {
            var parentInSameRun = await db.Gradients
                .AnyAsync(g => g.Id == parentId && g.RunId == runId, ct);
            if (!parentInSameRun)
                return BadRequest(new { error = $"Parent Gradient {parentId} is not in Run {runId}." });
        }

        var g = new Gradient(dto.Name, dto.Order, runId)
        {
            ParentId = dto.ParentId,
            Timestamp = dto.Timestamp ?? new DateTime(1900, 1, 1),
            Voltage = dto.Voltage,
            Frequency = dto.Frequency,
            Frame = dto.Frame,
        };
        db.Gradients.Add(g);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], jobId, runId, gradientId = g.Id },
            new GradientSummaryDto(g.Id, g.Name, g.Order, g.IsValid, g.RunId, g.ParentId, g.Timestamp, ShotCount: 0));
    }

    [HttpDelete("{gradientId:int}")]
    public async Task<IActionResult> Delete(int jobId, Guid runId, int gradientId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var g = await db.Gradients.FirstOrDefaultAsync(x => x.Id == gradientId && x.RunId == runId, ct);
        if (g is null) return NotFound();

        // Shots use DeleteBehavior.Restrict to this parent — must be empty to delete.
        var hasShots = await db.Shots.AnyAsync(s => s.GradientId == gradientId, ct);
        if (hasShots)
            return Conflict(new { error = "Gradient has child Shots; delete or reparent them first." });

        db.Gradients.Remove(g);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}

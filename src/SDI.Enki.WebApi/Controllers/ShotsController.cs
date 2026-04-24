using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Infrastructure.Data.Lookups;
using SDI.Enki.Shared.Shots;
using SDI.Enki.WebApi.Multitenancy;
// Extension methods on TenantDbContext: db.FindOrCreateAsync(...)

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Shots under a Gradient. Demonstrates the <c>FindOrCreateAsync</c>
/// pattern: inline Magnetics / Calibration payloads on creation collapse
/// to single lookup rows rather than duplicating per-shot — replaces the
/// legacy AFTER-INSERT dedup triggers at the repository layer.
///
/// Read endpoints also live here for shot-by-id lookups that don't need the
/// Gradient context (Get by ShotId works regardless of whether the parent
/// is a Gradient or a Rotary).
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}")]
public sealed class ShotsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet("shots/{shotId:int}")]
    public async Task<IActionResult> Get(int shotId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var shot = await db.Shots
            .AsNoTracking()
            .Include(s => s.Magnetics)
            .Include(s => s.Calibration)
            .FirstOrDefaultAsync(s => s.Id == shotId, ct);

        if (shot is null) return NotFound();

        return Ok(new ShotDetailDto(
            shot.Id, shot.ShotName, shot.FileTime,
            shot.ToolUptime, shot.ShotTime, shot.TimeStart, shot.TimeEnd,
            shot.NumberOfMags, shot.Frequency, shot.Bandwidth,
            shot.SampleFrequency, shot.SampleCount,
            shot.GradientId, shot.RotaryId,
            shot.Magnetics is null ? null :
                new MagneticsDto(shot.Magnetics.Id, shot.Magnetics.BTotal, shot.Magnetics.Dip, shot.Magnetics.Declination),
            shot.Calibration is null ? null :
                new CalibrationDto(shot.Calibration.Id, shot.Calibration.Name)));
    }

    [HttpGet("gradients/{gradientId:int}/shots")]
    public async Task<IEnumerable<ShotSummaryDto>> ListForGradient(int gradientId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        return await db.Shots
            .AsNoTracking()
            .Where(s => s.GradientId == gradientId)
            .OrderBy(s => s.FileTime)
            .Select(s => new ShotSummaryDto(s.Id, s.ShotName, s.FileTime, s.Frequency, s.GradientId, s.RotaryId))
            .ToListAsync(ct);
    }

    [HttpPost("gradients/{gradientId:int}/shots")]
    public async Task<IActionResult> CreateUnderGradient(
        int gradientId,
        [FromBody] CreateGradientShotDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();

        var gradientExists = await db.Gradients.AnyAsync(g => g.Id == gradientId, ct);
        if (!gradientExists)
            return NotFound(new { error = $"Gradient {gradientId} not found." });

        // Resolve Magnetics via find-or-create on (BTotal, Dip, Declination)
        // — this is the replacement for the legacy trg_ValidateMagnetics trigger.
        int? magneticsId = null;
        if (dto.Magnetics is { } m)
        {
            magneticsId = await db.FindOrCreateAsync(
                new Magnetics(m.BTotal, m.Dip, m.Declination),
                row => row.BTotal == m.BTotal && row.Dip == m.Dip && row.Declination == m.Declination,
                row => row.Id,
                ct);
        }

        // Resolve Calibration via find-or-create on (Name, CalibrationString).
        int? calibrationId = null;
        if (dto.Calibration is { } c)
        {
            calibrationId = await db.FindOrCreateAsync(
                new Calibration(c.Name, c.CalibrationString),
                row => row.Name == c.Name && row.CalibrationString == c.CalibrationString,
                row => row.Id,
                ct);
        }

        var shot = new Shot
        {
            ShotName = dto.ShotName,
            FileTime = dto.FileTime,
            ToolUptime = dto.ToolUptime,
            ShotTime = dto.ShotTime,
            TimeStart = dto.TimeStart,
            TimeEnd = dto.TimeEnd,
            NumberOfMags = dto.NumberOfMags,
            Frequency = dto.Frequency,
            Bandwidth = dto.Bandwidth,
            SampleFrequency = dto.SampleFrequency,
            SampleCount = dto.SampleCount,
            MagneticsId = magneticsId,
            CalibrationsId = calibrationId,
            GradientId = gradientId,
            RotaryId = null, // CK_Shots_ExactlyOneParent satisfied
        };
        db.Shots.Add(shot);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantCode = RouteData.Values["tenantCode"], shotId = shot.Id },
            new ShotSummaryDto(shot.Id, shot.ShotName, shot.FileTime, shot.Frequency, shot.GradientId, shot.RotaryId));
    }
}

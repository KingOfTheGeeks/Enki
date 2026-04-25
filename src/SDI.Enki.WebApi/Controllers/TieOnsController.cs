using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Shared.Wells.TieOns;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Tie-on stations under a Well. A tie-on is the reference point from
/// which a Well's trajectory is calculated — depth + inclination +
/// azimuth at a known surface or top-of-survey location, plus the
/// derived grid coordinates that anchor the survey set.
///
/// <para>
/// A Well typically has one active tie-on. Additional rows are kept for
/// historical references (re-tied surveys after a sidetrack, alternative
/// reference frames). The Calculate endpoint on
/// <c>SurveysController</c> consumes the most recent (highest <c>Id</c>)
/// tie-on; this controller doesn't pick a "current" — it just stores
/// every tie-on the user records.
/// </para>
///
/// <para>
/// Routes nest under <c>/jobs/{jobId:guid}/wells/{wellId:int}</c>.
/// Every action probes that the parent Well exists under that Job
/// via <see cref="WellLookup.WellExistsAsync"/>; an unknown
/// (jobId, wellId) pair returns <c>404 NotFoundProblem("Well", id)</c>
/// so the shape matches every other child-entity controller in this
/// surface.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/tieons")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
public sealed class TieOnsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    public async Task<IActionResult> List(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var rows = await db.TieOns
            .AsNoTracking()
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Depth)
            .Select(t => new TieOnSummaryDto(
                t.Id, t.WellId, t.Depth, t.Inclination, t.Azimuth, t.CreatedAt))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // ---------- detail ----------

    [HttpGet("{tieOnId:int}")]
    public async Task<IActionResult> Get(Guid jobId, int wellId, int tieOnId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var dto = await db.TieOns
            .AsNoTracking()
            .Where(t => t.Id == tieOnId && t.WellId == wellId)
            .Select(t => new TieOnDetailDto(
                t.Id, t.WellId,
                t.Depth, t.Inclination, t.Azimuth,
                t.North, t.East, t.Northing, t.Easting,
                t.VerticalReference, t.SubSeaReference, t.VerticalSectionDirection,
                t.CreatedAt, t.CreatedBy, t.UpdatedAt, t.UpdatedBy))
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? this.NotFoundProblem("TieOn", tieOnId.ToString())
            : Ok(dto);
    }

    // ---------- create ----------

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid jobId,
        int wellId,
        [FromBody] CreateTieOnDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tieOn = new TieOn(wellId, dto.Depth, dto.Inclination, dto.Azimuth)
        {
            North                    = dto.North,
            East                     = dto.East,
            Northing                 = dto.Northing,
            Easting                  = dto.Easting,
            VerticalReference        = dto.VerticalReference,
            SubSeaReference          = dto.SubSeaReference,
            VerticalSectionDirection = dto.VerticalSectionDirection,
        };
        db.TieOns.Add(tieOn);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                tenantCode = RouteData.Values["tenantCode"],
                jobId,
                wellId,
                tieOnId = tieOn.Id,
            },
            new TieOnSummaryDto(
                tieOn.Id, tieOn.WellId,
                tieOn.Depth, tieOn.Inclination, tieOn.Azimuth,
                tieOn.CreatedAt));
    }

    // ---------- update ----------

    [HttpPut("{tieOnId:int}")]
    public async Task<IActionResult> Update(
        Guid jobId,
        int wellId,
        int tieOnId,
        [FromBody] UpdateTieOnDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tieOn = await db.TieOns
            .FirstOrDefaultAsync(t => t.Id == tieOnId && t.WellId == wellId, ct);
        if (tieOn is null)
            return this.NotFoundProblem("TieOn", tieOnId.ToString());

        tieOn.Depth                    = dto.Depth;
        tieOn.Inclination              = dto.Inclination;
        tieOn.Azimuth                  = dto.Azimuth;
        tieOn.North                    = dto.North;
        tieOn.East                     = dto.East;
        tieOn.Northing                 = dto.Northing;
        tieOn.Easting                  = dto.Easting;
        tieOn.VerticalReference        = dto.VerticalReference;
        tieOn.SubSeaReference          = dto.SubSeaReference;
        tieOn.VerticalSectionDirection = dto.VerticalSectionDirection;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- delete ----------

    [HttpDelete("{tieOnId:int}")]
    public async Task<IActionResult> Delete(Guid jobId, int wellId, int tieOnId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var tieOn = await db.TieOns
            .FirstOrDefaultAsync(t => t.Id == tieOnId && t.WellId == wellId, ct);
        if (tieOn is null)
            return this.NotFoundProblem("TieOn", tieOnId.ToString());

        db.TieOns.Remove(tieOn);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

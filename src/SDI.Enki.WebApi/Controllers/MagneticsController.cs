using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Shared.Wells;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.Controllers.Wells;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// The well's canonical magnetic reference — at most one
/// <see cref="Magnetics"/> row per <see cref="Core.TenantDb.Wells.Well"/>
/// (enforced by the filtered unique index on
/// <c>Magnetics.WellId</c>). Backs the magnetic-settings card +
/// edit page on <c>WellDetail</c>.
///
/// <para>
/// PUT is upsert — same payload whether the well already has a
/// reference or not — so the front end doesn't have to branch
/// between Create / Update flows. DELETE is idempotent: if the
/// well has no reference, returns 204 anyway. Distinct from the
/// per-shot lookup pool (rows where <c>WellId IS NULL</c>); this
/// controller only ever touches per-well rows.
/// </para>
/// </summary>
[ApiController]
[Route("tenants/{tenantCode}/jobs/{jobId:guid}/wells/{wellId:int}/magnetics")]
[Authorize(Policy = EnkiPolicies.CanAccessTenant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class MagneticsController(ITenantDbContextFactory dbFactory) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MagneticsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, int wellId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        var row = await db.Magnetics
            .AsNoTracking()
            .Where(m => m.WellId == wellId)
            .Select(m => new
            {
                m.Id, WellId = m.WellId!.Value,
                m.BTotal, m.Dip, m.Declination,
                m.CreatedAt, m.CreatedBy, m.UpdatedAt, m.UpdatedBy,
                m.RowVersion,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Magnetics", $"well {wellId}");

        return Ok(new MagneticsDto(
            row.Id, row.WellId,
            row.BTotal, row.Dip, row.Declination,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion)));
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Set(
        Guid jobId,
        int wellId,
        [FromBody] SetMagneticsDto dto,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Upsert: pull the existing per-well row if any, otherwise
        // create one with WellId set so the filtered unique index
        // owns it. Optimistic-concurrency only applies on the
        // update branch — a fresh create has nothing to conflict
        // against, so RowVersion is ignored when creating.
        var existing = await db.Magnetics
            .FirstOrDefaultAsync(m => m.WellId == wellId, ct);

        if (existing is null)
        {
            db.Magnetics.Add(new Magnetics(dto.BTotal, dto.Dip, dto.Declination)
            {
                WellId = wellId,
            });
        }
        else
        {
            if (this.ApplyClientRowVersion(existing, dto.RowVersion) is { } badRowVersion)
                return badRowVersion;

            existing.BTotal      = dto.BTotal;
            existing.Dip         = dto.Dip;
            existing.Declination = dto.Declination;
        }

        if (await db.SaveOrConflictAsync(this, "Magnetics", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid jobId,
        int wellId,
        CancellationToken ct)
    {
        await using var db = dbFactory.CreateActive();
        if (!await db.WellExistsAsync(jobId, wellId, ct))
            return this.NotFoundProblem("Well", wellId.ToString());

        // Idempotent: clearing a non-existent reference returns 204
        // so the UI doesn't have to branch on "was it set?". Only
        // touches per-well rows (filter on WellId == this well);
        // legacy lookup rows (WellId IS NULL) are untouched.
        var existing = await db.Magnetics
            .FirstOrDefaultAsync(m => m.WellId == wellId, ct);

        if (existing is not null)
        {
            db.Magnetics.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}

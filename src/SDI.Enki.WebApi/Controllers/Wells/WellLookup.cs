using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Controllers.Wells;

/// <summary>
/// Shared "does this Well exist in the current tenant?" probe used by
/// every child-entity controller (Surveys, TieOns, Tubulars, Formations,
/// CommonMeasures). Lifts the duplicate <c>FirstOrDefaultAsync(...)</c>
/// out of each controller so the unknown-well 404 has one definition.
///
/// <para>
/// Returns the Well (without tracking) when found, or <c>null</c> when
/// not. Callers map <c>null</c> to <c>NotFoundProblem("Well", id)</c>
/// via the <c>EnkiResults</c> helper so every endpoint surfaces the
/// same ProblemDetails shape.
/// </para>
/// </summary>
public static class WellLookup
{
    public static Task<Well?> FindWellOrNullAsync(
        this TenantDbContext db,
        int wellId,
        CancellationToken ct = default)
        => db.Wells
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == wellId, ct);

    /// <summary>
    /// Lighter probe — just checks existence without materialising the
    /// row. Use when the caller only needs the 404/200 fork.
    /// </summary>
    public static Task<bool> WellExistsAsync(
        this TenantDbContext db,
        int wellId,
        CancellationToken ct = default)
        => db.Wells.AsNoTracking().AnyAsync(w => w.Id == wellId, ct);
}

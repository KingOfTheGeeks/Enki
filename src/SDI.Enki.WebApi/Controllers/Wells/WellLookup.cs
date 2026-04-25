using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Controllers.Wells;

/// <summary>
/// Shared parent-existence probes for tenant-scoped routes. Wells
/// belong to a Job — every Well-rooted route therefore needs to
/// confirm both the parent Job and the Well belong together so a
/// URL like <c>/jobs/{wrong-job}/wells/{some-well}</c> returns 404
/// rather than silently surfacing the row.
///
/// <para>
/// Used by every child-entity controller (Surveys, TieOns, Tubulars,
/// Formations, CommonMeasures) so the unknown-well / wrong-parent
/// 404 has one definition.
/// </para>
/// </summary>
public static class WellLookup
{
    /// <summary>
    /// Does <paramref name="wellId"/> exist under <paramref name="jobId"/>
    /// in the active tenant DB? Use when the caller only needs the
    /// 404/200 fork.
    /// </summary>
    public static Task<bool> WellExistsAsync(
        this TenantDbContext db,
        Guid jobId,
        int wellId,
        CancellationToken ct = default)
        => db.Wells
            .AsNoTracking()
            .AnyAsync(w => w.Id == wellId && w.JobId == jobId, ct);

    /// <summary>
    /// Returns the Well (without tracking) when it exists under the
    /// given Job, else <c>null</c>. Callers map <c>null</c> to
    /// <c>NotFoundProblem("Well", id)</c>.
    /// </summary>
    public static Task<Well?> FindWellOrNullAsync(
        this TenantDbContext db,
        Guid jobId,
        int wellId,
        CancellationToken ct = default)
        => db.Wells
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == wellId && w.JobId == jobId, ct);

    /// <summary>
    /// Does <paramref name="jobId"/> exist in the active tenant DB?
    /// Used by <see cref="SDI.Enki.WebApi.Controllers.WellsController"/>'s
    /// list / create paths to surface a 404 NotFoundProblem("Job", id)
    /// before the well query / insert runs.
    /// </summary>
    public static Task<bool> JobExistsAsync(
        this TenantDbContext db,
        Guid jobId,
        CancellationToken ct = default)
        => db.Jobs.AsNoTracking().AnyAsync(j => j.Id == jobId, ct);
}

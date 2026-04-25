using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.Surveys;

/// <summary>
/// Server-side guarantee that a Well's Survey rows are never returned
/// to clients with stale (uncalculated) trajectory columns. Called
/// after every Survey or TieOn mutation in the controllers and once
/// per well at the end of the dev seeder.
///
/// <para>
/// Implementations recompute the well's full trajectory by feeding
/// the lowest-Id <c>TieOn</c> + every <c>Survey</c> (ordered by
/// depth) to Marduk's minimum-curvature engine, then writing the
/// computed columns (TVD / SubSea / North / East / DLS /
/// VerticalSection / Northing / Easting / Build / Turn) back onto
/// the tracked entities. Saves changes before returning.
/// </para>
///
/// <para>
/// No-ops gracefully when the well has no tie-on or no surveys —
/// you can't anchor a calculation without both, and the page renders
/// zeros in that case.
/// </para>
/// </summary>
public interface ISurveyAutoCalculator
{
    Task RecalculateAsync(TenantDbContext db, int wellId, CancellationToken ct = default);
}

using AMR.Core.Survey.Services;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;

using MardukSurveyStation = AMR.Core.Survey.Models.SurveyStation;
using MardukTieOn = AMR.Core.Survey.Models.TieOn;

namespace SDI.Enki.Infrastructure.Surveys;

/// <summary>
/// <see cref="ISurveyAutoCalculator"/> implemented over Marduk's
/// <see cref="ISurveyCalculator"/> (minimum-curvature). Stateless —
/// safe as a singleton.
///
/// <para>
/// Inputs and outputs are SI throughout: depths in meters, angles in
/// degrees, coordinates in meters. The DB stores everything in SI
/// (rule: "always metric in the DB; convert at the GUI for display"),
/// so no conversion happens at this boundary.
/// </para>
///
/// <para>
/// Default calculation parameters mirror what
/// <c>SurveysController.Calculate</c> historically used: 30 m for the
/// dogleg-severity averaging window, 6 decimal places of precision —
/// enough for sub-millimeter resolution at any realistic depth.
/// </para>
/// </summary>
public sealed class MardukSurveyAutoCalculator(ISurveyCalculator calculator) : ISurveyAutoCalculator
{
    private const int DefaultMetersToCalculateDegreesOver = 30;
    private const int DefaultPrecision = 6;

    public async Task RecalculateAsync(
        TenantDbContext db,
        int wellId,
        CancellationToken ct = default)
    {
        // Every Well auto-gets a zero tie-on on creation (see
        // WellsController.Create) and TieOnsController.Delete resets
        // to zero rather than removing — so in normal flow this branch
        // never fires. Kept as defence-in-depth against direct DB
        // edits or pre-invariant rows: silently no-op rather than
        // throw on missing anchor.
        var tieOn = await db.TieOns
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (tieOn is null) return;

        // Marduk requires strictly-increasing depth. The bulk-create
        // endpoint guards against duplicates / out-of-order rows on
        // entry; OrderBy here matches what /calculate has always done.
        var surveys = await db.Surveys
            .Where(s => s.WellId == wellId)
            .OrderBy(s => s.Depth)
            .ToListAsync(ct);
        if (surveys.Count == 0) return;

        var mardukTieOn = new MardukTieOn(
            depth:                    tieOn.Depth,
            inclination:              tieOn.Inclination,
            azimuth:                  tieOn.Azimuth,
            northing:                 tieOn.Northing,
            easting:                  tieOn.Easting,
            verticalReference:        tieOn.VerticalReference,
            subSeaReference:          tieOn.SubSeaReference,
            verticalSectionDirection: tieOn.VerticalSectionDirection);

        // Marduk's MinimumCurvature uses stations[0] purely as the
        // starting tangent reference and prepends the supplied TieOn
        // (as a stub SurveyStation) to the output — stations[0] is
        // never written back to. So if we passed only our N surveys,
        // surveys[0] would silently be dropped from the calculation
        // and surveys[1..N-1] would land at output[1..N-1] giving an
        // off-by-one writeback. Fix: duplicate the tie-on as stations[0],
        // pushing every real survey into stations[1..N]. The output
        // shape becomes [tie-on-stub, survey0-comp, survey1-comp, …].
        var inputStations = new MardukSurveyStation[surveys.Count + 1];
        inputStations[0] = new MardukSurveyStation(tieOn.Depth, tieOn.Inclination, tieOn.Azimuth);
        for (var i = 0; i < surveys.Count; i++)
        {
            var s = surveys[i];
            inputStations[i + 1] = new MardukSurveyStation(s.Depth, s.Inclination, s.Azimuth);
        }

        var computed = calculator.Process(
            mardukTieOn,
            inputStations,
            metersToCalculateDegreesOver: DefaultMetersToCalculateDegreesOver,
            precision:                    DefaultPrecision);

        // Length contract: Marduk returns one output per input. With the
        // tie-on prepended we expect N+1 outputs (output[0] = tie-on stub,
        // output[1..N] = computed surveys). Anything else means the
        // engine dropped or duplicated a row — fail loud rather than
        // write back a misaligned result.
        if (computed.Length != surveys.Count + 1)
            throw new InvalidOperationException(
                $"Survey calculator returned {computed.Length} computed stations " +
                $"for {surveys.Count} input surveys (+ 1 tie-on prepended) on " +
                $"Well {wellId}. Refusing to write back a partial result.");

        // Skip output[0] (tie-on stub) and map output[i+1] → surveys[i].
        for (var i = 0; i < surveys.Count; i++)
        {
            var src = computed[i + 1];
            var dst = surveys[i];
            dst.VerticalDepth   = src.VerticalDepth;
            dst.SubSea          = src.SubSea;
            dst.North           = src.North;
            dst.East            = src.East;
            dst.DoglegSeverity  = src.DoglegSeverity;
            dst.VerticalSection = src.VerticalSection;
            dst.Northing        = src.Northing;
            dst.Easting         = src.Easting;
            dst.Build           = src.Build;
            dst.Turn            = src.Turn;
        }

        await db.SaveChangesAsync(ct);
    }
}

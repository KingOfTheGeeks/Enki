using AMR.Core.Survey.Models;
using AMR.Core.Survey.Services;

namespace SDI.Enki.WebApi.Tests.Fakes;

/// <summary>
/// Fake <see cref="ISurveyCalculator"/> that returns a length-matched
/// array of stations with deterministic computed values — enough for
/// the controller to verify it round-trips Marduk results back onto
/// EF rows. The real minimum-curvature math is tested inside the
/// Marduk repo; we only need to confirm Enki's wiring.
///
/// <para>
/// Set <see cref="ReturnShorter"/> to simulate the bug-shaped case
/// where Marduk returns a different count than was passed in — the
/// controller's length-contract guard should fail loud rather than
/// write back a partial result.
/// </para>
/// </summary>
internal sealed class FakeSurveyCalculator : ISurveyCalculator
{
    public bool ReturnShorter { get; set; }
    public int CallCount { get; private set; }
    public TieOn? LastTieOn { get; private set; }
    public SurveyStation[]? LastStations { get; private set; }

    public SurveyStation[] Process(
        TieOn tieOn,
        SurveyStation[] stations,
        int metersToCalculateDegreesOver,
        int precision)
    {
        CallCount++;
        LastTieOn = tieOn;
        LastStations = stations;

        if (ReturnShorter && stations.Length > 0)
            return new SurveyStation[stations.Length - 1];

        // Construct one output station per input. Set deterministic
        // computed values so tests can assert the writeback survived
        // the round-trip.
        var output = new SurveyStation[stations.Length];
        for (var i = 0; i < stations.Length; i++)
        {
            var input = stations[i];
            var copy = new SurveyStation(input.Depth, input.Inclination, input.Azimuth);
            copy.SetMinimumCurvature(
                north:           1 + i,
                east:            2 + i,
                verticalDepth:   input.Depth * 0.95,    // arbitrary; just non-zero
                subSea:          3 + i,
                doglegSeverity:  4 + i,
                verticalSection: 5 + i,
                northing:        6 + i,
                easting:         7 + i,
                build:           8 + i,
                turn:            9 + i);
            output[i] = copy;
        }
        return output;
    }
}

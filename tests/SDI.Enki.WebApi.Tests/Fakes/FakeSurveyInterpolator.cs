using AMR.Core.Survey.Models;
using AMR.Core.Survey.Services;

namespace SDI.Enki.WebApi.Tests.Fakes;

/// <summary>
/// Fake <see cref="ISurveyInterpolator"/> that returns a deterministic
/// SurveyStation for any target depth. Used by Formations / CommonMeasures
/// / Tubulars controller tests where we only care that the controller
/// wires the resolver call correctly — not whether the math is right.
///
/// <para>
/// <c>VerticalDepth = targetDepth * 0.95</c> matches
/// <see cref="FakeSurveyCalculator"/>'s convention so the round-trip
/// (calc populates VerticalDepth on a Survey row → resolver reads it →
/// interpolator passes target through) lines up. <c>InterpolateVerticalDepth</c>
/// throws — Enki only consumes <c>InterpolateDepth</c>.
/// </para>
/// </summary>
internal sealed class FakeSurveyInterpolator : ISurveyInterpolator
{
    public SurveyStation InterpolateDepth(
        List<SurveyStation> surveys, double targetDepth, int precision, double verticalSectionDirection = 0.0)
    {
        var s = new SurveyStation(targetDepth, 0, 0);
        s.SetMinimumCurvature(
            north:           0,
            east:            0,
            verticalDepth:   targetDepth * 0.95,
            subSea:          0,
            doglegSeverity:  0,
            verticalSection: 0,
            northing:        0,
            easting:         0,
            build:           0,
            turn:            0);
        return s;
    }

    public SurveyStation InterpolateVerticalDepth(
        List<SurveyStation> surveys, double targetTvd, int precision, double verticalSectionDirection = 0.0) =>
        throw new NotImplementedException("Enki only consumes InterpolateDepth.");
}

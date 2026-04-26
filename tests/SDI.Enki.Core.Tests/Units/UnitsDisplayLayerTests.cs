using SDI.Enki.Core.Units;
using UnitsNet.Units;

namespace SDI.Enki.Core.Tests.Units;

/// <summary>
/// Pin tests for the two <see cref="EnkiQuantity"/> values added when
/// the units display layer landed:
///   * <see cref="EnkiQuantity.LinearMassDensity"/> — backs the
///     Tubular grid's Weight column (lb/ft on Field, kg/m on Metric / SI).
///   * <see cref="EnkiQuantity.CurvatureRate"/> — backs DLS, Build,
///     and Turn (°/100 ft on Field, °/30 m on Metric / SI; stored as
///     °/30 m which is Marduk's MinimumCurvature averaging window).
///
/// These tests intentionally exercise the same surfaces
/// (<see cref="Measurement"/>, <see cref="UnitConverter"/>,
/// <see cref="UnitSystemPresets"/>) that <c>UnitFormatted</c> and
/// <c>UnitInput</c> route through, so any regression in the display
/// layer's math shows up here without needing a bUnit dependency.
/// </summary>
public class UnitsDisplayLayerTests
{
    private const double Tolerance = 1e-6;

    // =====================================================================
    // LinearMassDensity — drillpipe / casing weight
    // =====================================================================

    [Fact]
    public void LinearMassDensity_FromPoundPerFoot_ConvertsToKilogramPerMeterAtConstruction()
    {
        // 19.5 lb/ft is a standard 5" drillpipe weight.
        // 1 lb/ft = 1.488163943 kg/m → 19.5 lb/ft ≈ 29.019 kg/m
        var weight = Measurement.FromUnit(
            19.5, EnkiQuantity.LinearMassDensity, LinearDensityUnit.PoundPerFoot);

        Assert.Equal(EnkiQuantity.LinearMassDensity, weight.Quantity);
        Assert.Equal(19.5 * 1.488163943, weight.SiValue, 1e-3);
    }

    [Fact]
    public void LinearMassDensity_RoundTripsThroughFieldPreset()
    {
        // The chain UnitInput → SiValue → UnitFormatted hits exactly this
        // round trip; if it drifts, displayed values won't match what was
        // typed.
        var typed = Measurement.FromUnit(
            19.5, EnkiQuantity.LinearMassDensity, LinearDensityUnit.PoundPerFoot);
        var (back, abbrev) = typed.As(UnitSystem.Field);

        Assert.Equal(19.5, back, 1e-9);
        Assert.Equal("lb/ft", abbrev);
    }

    [Fact]
    public void LinearMassDensity_ProjectsAsKilogramPerMeterOnMetricAndSI()
    {
        // Stored value is already SI (kg/m), so Metric and SI both project
        // through identity.
        var weight = Measurement.FromSi(29.0, EnkiQuantity.LinearMassDensity);

        var metric = weight.As(UnitSystem.Metric);
        var si     = weight.As(UnitSystem.SI);

        Assert.Equal(29.0, metric.Value, 1e-9);
        Assert.Equal("kg/m", metric.Abbreviation);
        Assert.Equal(29.0, si.Value, 1e-9);
        Assert.Equal("kg/m", si.Abbreviation);
    }

    [Fact]
    public void LinearMassDensity_AppearsInPresetMatrixForAllRealSystems()
    {
        // Presets table is generic — make sure the new quantity is wired
        // for every real preset (Custom intentionally excluded).
        Assert.Equal("lb/ft", UnitSystemPresets.Abbreviation(UnitSystem.Field,  EnkiQuantity.LinearMassDensity));
        Assert.Equal("kg/m",  UnitSystemPresets.Abbreviation(UnitSystem.Metric, EnkiQuantity.LinearMassDensity));
        Assert.Equal("kg/m",  UnitSystemPresets.Abbreviation(UnitSystem.SI,     EnkiQuantity.LinearMassDensity));
    }

    // =====================================================================
    // CurvatureRate — DLS / Build / Turn
    // =====================================================================

    [Fact]
    public void CurvatureRate_FieldProjection_AppliesPer100FtFactor()
    {
        // Stored as °/30 m. A typical motor build of 2.0°/30 m projects
        // to 2.0 × (30.48 / 30) ≈ 2.032°/100 ft on Field.
        var dls = Measurement.FromSi(2.0, EnkiQuantity.CurvatureRate);
        var (value, abbrev) = dls.As(UnitSystem.Field);

        Assert.Equal(2.0 * (30.48 / 30.0), value, Tolerance);
        Assert.Equal("°/100ft", abbrev);
    }

    [Fact]
    public void CurvatureRate_MetricAndSI_AreIdentity()
    {
        // °/30 m is the storage convention AND the metric display
        // convention — no scaling.
        var dls = Measurement.FromSi(2.0, EnkiQuantity.CurvatureRate);

        Assert.Equal((2.0, "°/30m"), dls.As(UnitSystem.Metric));
        Assert.Equal((2.0, "°/30m"), dls.As(UnitSystem.SI));
    }

    [Fact]
    public void CurvatureRate_ConvertOilfield_FieldFactorIsExactly30_48Over30()
    {
        // The factor is exact (foot definition: 1 ft = 0.3048 m); guard
        // against accidentally substituting an approximate constant.
        Assert.Equal(
            30.48 / 30.0,
            UnitConverter.ConvertOilfield(1.0, EnkiQuantity.CurvatureRate, UnitSystem.Field),
            Tolerance);

        Assert.Equal(
            1.0,
            UnitConverter.ConvertOilfield(1.0, EnkiQuantity.CurvatureRate, UnitSystem.Metric),
            Tolerance);
    }

    [Fact]
    public void CurvatureRate_HasNoUnitsNetAnalog_AsQuantityThrows()
    {
        // <UnitInput> guards on choice.UnitEnum being null and throws a
        // friendly error; this test pins that ConvertOilfield is the only
        // path. If somebody later adds a UnitsNet binding for it, the
        // input-side guard needs to be reconsidered.
        Assert.Throws<InvalidOperationException>(() =>
            UnitConverter.ToSi(1.0, EnkiQuantity.CurvatureRate, AngleUnit.Degree));

        Assert.Throws<InvalidOperationException>(() =>
            UnitConverter.FromSi(1.0, EnkiQuantity.CurvatureRate, AngleUnit.Degree));
    }

    [Fact]
    public void CurvatureRate_AppearsInPresetMatrixForAllRealSystems()
    {
        Assert.Equal("°/100ft", UnitSystemPresets.Abbreviation(UnitSystem.Field,  EnkiQuantity.CurvatureRate));
        Assert.Equal("°/30m",   UnitSystemPresets.Abbreviation(UnitSystem.Metric, EnkiQuantity.CurvatureRate));
        Assert.Equal("°/30m",   UnitSystemPresets.Abbreviation(UnitSystem.SI,     EnkiQuantity.CurvatureRate));

        // CurvatureRate is in the oilfield-fallback bucket — no UnitsNet
        // enum should be attached.
        Assert.Null(UnitSystemPresets.Get(UnitSystem.Field,  EnkiQuantity.CurvatureRate).UnitEnum);
        Assert.Null(UnitSystemPresets.Get(UnitSystem.Metric, EnkiQuantity.CurvatureRate).UnitEnum);
        Assert.Null(UnitSystemPresets.Get(UnitSystem.SI,     EnkiQuantity.CurvatureRate).UnitEnum);
    }

    [Fact]
    public void CurvatureRate_FormatRendersValueAndUnit()
    {
        // 2.0°/30 m × 1.016 ≈ 2.032°/100 ft → default "N" rendering = "2.03 °/100ft"
        var dls = Measurement.FromSi(2.0, EnkiQuantity.CurvatureRate);
        Assert.Equal("2.03 °/100ft", dls.Format(UnitSystem.Field));
        Assert.Equal("2.00 °/30m",   dls.Format(UnitSystem.Metric));
    }
}

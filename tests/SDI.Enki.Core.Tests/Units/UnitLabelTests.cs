using SDI.Enki.Core.Units;
using UnitsNet.Units;

namespace SDI.Enki.Core.Tests.Units;

/// <summary>
/// Pin tests for <see cref="UnitLabel"/>. The helper is one-line per
/// path but the angle short-circuit and the override hook are easy to
/// regress when somebody refactors UnitSystemPresets — these tests are
/// the safety net.
/// </summary>
public class UnitLabelTests
{
    // ---------- standard preset path ----------

    [Theory]
    [InlineData("Depth", EnkiQuantity.Length,   /*Field*/  1, "Depth (ft)")]
    [InlineData("Depth", EnkiQuantity.Length,   /*Metric*/ 2, "Depth (m)")]
    [InlineData("Depth", EnkiQuantity.Length,   /*SI*/     3, "Depth (m)")]
    [InlineData("Mud weight", EnkiQuantity.Density, 1, "Mud weight (ppg)")]
    [InlineData("Mud weight", EnkiQuantity.Density, 2, "Mud weight (kg/m³)")]
    [InlineData("DLS", EnkiQuantity.CurvatureRate, 1, "DLS (°/100ft)")]
    [InlineData("DLS", EnkiQuantity.CurvatureRate, 2, "DLS (°/30m)")]
    [InlineData("Weight", EnkiQuantity.LinearMassDensity, 1, "Weight (lb/ft)")]
    [InlineData("Weight", EnkiQuantity.LinearMassDensity, 2, "Weight (kg/m)")]
    public void For_BuildsLabelWithPresetAbbreviation(
        string label, EnkiQuantity quantity, int systemValue, string expected)
    {
        var system = UnitSystem.FromValue(systemValue);
        Assert.Equal(expected, UnitLabel.For(label, quantity, system));
    }

    // ---------- angle short-circuit ----------

    [Theory]
    [InlineData(1 /*Field */)]
    [InlineData(2 /*Metric*/)]
    [InlineData(3 /*SI    */)]
    public void For_AngleAlwaysRendersDegrees_RegardlessOfPreset(int systemValue)
    {
        // Strict SI's preset says "rad", but DB and UX store/display
        // degrees universally. The label must agree with what
        // UnitFormatted actually paints; otherwise headers and cells
        // would disagree on Field versus radians on SI.
        var system = UnitSystem.FromValue(systemValue);
        Assert.Equal("Inc (°)", UnitLabel.For("Inc", EnkiQuantity.Angle, system));
        Assert.Equal("Az (°)",  UnitLabel.For("Az",  EnkiQuantity.Angle, system));
    }

    // ---------- override hook (tubular OD) ----------

    [Fact]
    public void For_OverrideUnit_UsesProvidedAbbreviationInsteadOfPreset()
    {
        // Tubular OD is the canonical override case: in on Field
        // (overrides ft) and mm on Metric (overrides m).
        Assert.Equal(
            "OD (in)",
            UnitLabel.For("OD", EnkiQuantity.Length, UnitSystem.Field,
                          overrideUnit: LengthUnit.Inch, overrideAbbreviation: "in"));

        Assert.Equal(
            "OD (mm)",
            UnitLabel.For("OD", EnkiQuantity.Length, UnitSystem.Metric,
                          overrideUnit: LengthUnit.Millimeter, overrideAbbreviation: "mm"));
    }

    [Fact]
    public void For_OverrideUnit_WithoutAbbreviation_FallsBackToBareLabel()
    {
        // Pathological: caller forgot to pair the override with an
        // abbreviation. Better to render the bare label than to
        // throw mid-render in a Syncfusion grid header.
        Assert.Equal(
            "OD",
            UnitLabel.For("OD", EnkiQuantity.Length, UnitSystem.Field,
                          overrideUnit: LengthUnit.Inch, overrideAbbreviation: null));
    }

    // ---------- empty-abbreviation quantity ----------

    [Fact]
    public void For_DimensionlessQuantity_RendersBareLabelWithoutEmptyParens()
    {
        // Dimensionless has "" for its abbreviation in every preset;
        // "Ratio ()" would look broken. The helper must collapse to
        // just "Ratio".
        Assert.Equal("Ratio", UnitLabel.For("Ratio", EnkiQuantity.Dimensionless, UnitSystem.Field));
        Assert.Equal("Ratio", UnitLabel.For("Ratio", EnkiQuantity.Dimensionless, UnitSystem.SI));
    }
}

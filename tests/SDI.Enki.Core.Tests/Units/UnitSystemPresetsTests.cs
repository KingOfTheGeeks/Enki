using SDI.Enki.Core.Units;

namespace SDI.Enki.Core.Tests.Units;

/// <summary>
/// Tests the preset matrix in <see cref="UnitSystemPresets"/>. Most of
/// the value is in <see cref="EveryPresetCoversEveryQuantity"/> — it
/// fails loudly the moment a new quantity or preset is added without
/// wiring every cell of the matrix, which is exactly the silent failure
/// mode this table is vulnerable to.
/// </summary>
public class UnitSystemPresetsTests
{
    private static readonly UnitSystem[] RealPresets =
    {
        // Custom is intentionally excluded — it's a "resolve via overrides"
        // marker and shouldn't have static entries.
        UnitSystem.Field,
        UnitSystem.Metric,
        UnitSystem.SI,
    };

    [Fact]
    public void EveryPresetCoversEveryQuantity()
    {
        var missing = new List<string>();

        foreach (var preset in RealPresets)
            foreach (EnkiQuantity q in Enum.GetValues<EnkiQuantity>())
            {
                try
                {
                    var choice = UnitSystemPresets.Get(preset, q);
                    Assert.False(
                        string.IsNullOrEmpty(choice.Abbreviation) && q != EnkiQuantity.Dimensionless,
                        $"{preset.Name}/{q} resolved but has an empty abbreviation.");
                }
                catch (InvalidOperationException)
                {
                    missing.Add($"{preset.Name}/{q}");
                }
            }

        Assert.True(missing.Count == 0,
            "Preset matrix has gaps:\n  " + string.Join("\n  ", missing));
    }

    [Theory]
    [InlineData(1 /* Field  */, EnkiQuantity.Length,   "ft")]
    [InlineData(1 /* Field  */, EnkiQuantity.Pressure, "psi")]
    [InlineData(1 /* Field  */, EnkiQuantity.Density,  "ppg")]
    [InlineData(2 /* Metric */, EnkiQuantity.Length,   "m")]
    [InlineData(2 /* Metric */, EnkiQuantity.Pressure, "bar")]
    [InlineData(3 /* SI     */, EnkiQuantity.Length,   "m")]
    [InlineData(3 /* SI     */, EnkiQuantity.Pressure, "Pa")]
    [InlineData(3 /* SI     */, EnkiQuantity.Angle,    "rad")]
    public void Abbreviation_ReturnsExpectedForKnownCells(
        int systemValue, EnkiQuantity quantity, string expected)
    {
        var system = UnitSystem.FromValue(systemValue);
        Assert.Equal(expected, UnitSystemPresets.Abbreviation(system, quantity));
    }

    [Fact]
    public void GammaRay_ResolvesWithoutUnitsNetEnum_AcrossAllPresets()
    {
        // GAPI has no UnitsNet analog — verify the "oilfield fallback"
        // path keeps a consistent abbreviation across presets so a
        // gamma-ray value doesn't change "unit" from Field to SI.
        foreach (var preset in RealPresets)
        {
            var choice = UnitSystemPresets.Get(preset, EnkiQuantity.GammaRay);
            Assert.Null(choice.UnitEnum);
            Assert.Equal("GAPI", choice.Abbreviation);
        }
    }

    [Fact]
    public void MagneticFluxDensity_IsNanoteslaInOperationalPresets_TeslaInStrictSI()
    {
        // Worth guarding: SDI ranging tooling speaks nT natively, so
        // "strict SI" is the only preset where we expect T. Flipping
        // this accidentally would make every mag reading display at
        // 5e-5 instead of 50000.
        Assert.Equal("nT", UnitSystemPresets.Abbreviation(UnitSystem.Field,  EnkiQuantity.MagneticFluxDensity));
        Assert.Equal("nT", UnitSystemPresets.Abbreviation(UnitSystem.Metric, EnkiQuantity.MagneticFluxDensity));
        Assert.Equal("T",  UnitSystemPresets.Abbreviation(UnitSystem.SI,     EnkiQuantity.MagneticFluxDensity));
    }
}

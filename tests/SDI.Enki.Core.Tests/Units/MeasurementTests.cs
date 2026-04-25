using SDI.Enki.Core.Units;
using UnitsNet.Units;

namespace SDI.Enki.Core.Tests.Units;

/// <summary>
/// Round-trips for <see cref="Measurement"/> + <see cref="UnitConverter"/>.
/// The point of these tests isn't to retest UnitsNet's conversion math
/// (UnitsNet has its own suite for that) — it's to pin Enki's bridge:
/// the right UnitsNet quantity is reached for each <see cref="EnkiQuantity"/>,
/// each preset projects through the right unit, and the oilfield-specific
/// formulas (sonic transit time) compute correctly.
/// </summary>
public class MeasurementTests
{
    private const double Tolerance = 1e-6;

    // ---------- construction ----------

    [Fact]
    public void FromUnit_ConvertsInputToSiAtConstruction()
    {
        // 10,000 ft → 3,048 m
        var depth = Measurement.FromUnit(10_000, EnkiQuantity.Length, LengthUnit.Foot);

        Assert.Equal(EnkiQuantity.Length, depth.Quantity);
        Assert.Equal(3048.0, depth.SiValue, Tolerance);
    }

    [Fact]
    public void FromSi_StoresValueVerbatim()
    {
        var pressure = Measurement.FromSi(101_325, EnkiQuantity.Pressure);
        Assert.Equal(101_325, pressure.SiValue, Tolerance);
    }

    // ---------- projection through presets ----------

    [Theory]
    [InlineData(3048.0,                EnkiQuantity.Length,   /*Field*/  1, 10_000.0, "ft")]
    [InlineData(3048.0,                EnkiQuantity.Length,   /*Metric*/ 2,  3_048.0, "m")]
    [InlineData(101_325.0,             EnkiQuantity.Pressure, /*Field*/  1,    14.69594878, "psi")]
    [InlineData(101_325.0,             EnkiQuantity.Pressure, /*Metric*/ 2,     1.01325, "bar")]
    public void As_ProjectsSiThroughPresetsCorrectly(
        double siValue, EnkiQuantity quantity, int systemValue,
        double expectedValue, string expectedAbbrev)
    {
        var system = UnitSystem.FromValue(systemValue);
        var m      = Measurement.FromSi(siValue, quantity);

        var (value, abbrev) = m.As(system);

        Assert.Equal(expectedValue, value, 1e-3);
        Assert.Equal(expectedAbbrev, abbrev);
    }

    // ---------- magnetic flux density (the SDI-ranging case) ----------

    [Fact]
    public void MagneticFluxDensity_FieldAndMetric_BothShowNanotesla()
    {
        // 50_000 nT = 5e-5 T (Earth's surface field magnitude is ~25-65 μT
        // i.e. 25,000–65,000 nT; this tests a realistic SDI ranging value).
        var b = Measurement.FromUnit(
            50_000, EnkiQuantity.MagneticFluxDensity, MagneticFieldUnit.Nanotesla);

        Assert.Equal(5e-5, b.SiValue, 1e-9);
        Assert.Equal((50_000.0, "nT"), b.As(UnitSystem.Field));
        Assert.Equal((50_000.0, "nT"), b.As(UnitSystem.Metric));
        Assert.Equal((5e-5,    "T"),  b.As(UnitSystem.SI));
    }

    // ---------- oilfield specials ----------

    [Fact]
    public void SonicTransitTime_StoredInSi_ProjectsToFieldAndMetricCorrectly()
    {
        // Typical shale ~120 μs/ft.
        // SI value: 120 μs/ft × (3.28084 ft/m) × 1e-6 s/μs ≈ 3.937e-4 s/m
        const double fieldInput  = 120.0;
        const double siExpected  = 120.0 / 3.048e5;   // inverse of ConvertOilfield's 3.048e5 factor

        var st = new Measurement(siExpected, EnkiQuantity.SonicTransitTime);

        var (fieldValue,  fieldAbbrev)  = st.As(UnitSystem.Field);
        var (metricValue, metricAbbrev) = st.As(UnitSystem.Metric);
        var (siValue,     siAbbrev)     = st.As(UnitSystem.SI);

        Assert.Equal(fieldInput, fieldValue, 1e-3);
        Assert.Equal("μs/ft",     fieldAbbrev);

        // 1 μs/ft × 3.28084 ft/m = 3.28084 μs/m → so 120 μs/ft ≈ 393.7 μs/m
        Assert.Equal(120.0 * 3.28084, metricValue, 1e-3);
        Assert.Equal("μs/m", metricAbbrev);

        Assert.Equal(siExpected, siValue, 1e-9);
        Assert.Equal("s/m",      siAbbrev);
    }

    [Theory]
    [InlineData(EnkiQuantity.GammaRay)]
    [InlineData(EnkiQuantity.PhotoelectricFactor)]
    [InlineData(EnkiQuantity.Porosity)]
    [InlineData(EnkiQuantity.Dimensionless)]
    public void OilfieldQuantities_AreIdentityAcrossPresets(EnkiQuantity quantity)
    {
        var m = new Measurement(0.42, quantity);
        Assert.Equal(0.42, m.As(UnitSystem.Field).Value,  Tolerance);
        Assert.Equal(0.42, m.As(UnitSystem.Metric).Value, Tolerance);
        Assert.Equal(0.42, m.As(UnitSystem.SI).Value,     Tolerance);
    }

    // ---------- formatting ----------

    [Fact]
    public void Format_RendersValuePlusAbbreviation()
    {
        var depth = Measurement.FromUnit(10_000, EnkiQuantity.Length, LengthUnit.Foot);

        // Default "N" = thousands separators + 2 decimals → "10,000.00 ft"
        Assert.Equal("10,000.00 ft", depth.Format(UnitSystem.Field));

        // Custom format
        Assert.Equal("10000 ft", depth.Format(UnitSystem.Field, "F0"));
    }

    // ---------- equality ----------

    [Fact]
    public void Equality_ConsidersBothValueAndQuantity()
    {
        var a = Measurement.FromSi(1.0, EnkiQuantity.Length);
        var b = Measurement.FromSi(1.0, EnkiQuantity.Length);
        var c = Measurement.FromSi(1.0, EnkiQuantity.Pressure);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}

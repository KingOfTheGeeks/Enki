using UnitsNet;
using UnitsNet.Units;

namespace SDI.Enki.Core.Units;

/// <summary>
/// Converts between canonical SI and a UnitsNet (or oilfield-specific)
/// representation, keyed on <see cref="EnkiQuantity"/>. Used by
/// <see cref="Measurement"/> at construction and rendering — the
/// rest of the codebase shouldn't need to call this directly.
///
/// <para>
/// The big switch on <c>EnkiQuantity</c> is the cost of bridging
/// strongly-typed UnitsNet enums (<c>LengthUnit</c>, <c>PressureUnit</c>,
/// …) into our open-ended <c>EnkiQuantity</c> dispatch. Adding a new
/// quantity backed by UnitsNet means: add it to <c>EnkiQuantity</c>,
/// add the (system, quantity) cells to <c>UnitSystemPresets</c>, then
/// add one case here for the conversion. No other file changes.
/// </para>
///
/// <para>
/// Oilfield-specific quantities (GAPI, Porosity, b/e, sonic transit
/// time) don't have UnitsNet quantities. <see cref="ConvertOilfield"/>
/// handles those via a small per-quantity rule set; see comments at
/// the call sites for the formulas.
/// </para>
/// </summary>
public static class UnitConverter
{
    /// <summary>
    /// Converts <paramref name="value"/> in <paramref name="unit"/> into
    /// canonical SI for <paramref name="quantity"/>. Throws if the unit
    /// type doesn't match the quantity (e.g. PressureUnit passed with
    /// EnkiQuantity.Length).
    /// </summary>
    public static double ToSi(double value, EnkiQuantity quantity, Enum unit) =>
        AsQuantity(value, quantity, unit).As(BaseUnitOf(quantity));

    /// <summary>
    /// Inverse of <see cref="ToSi"/>: takes an SI value and projects it
    /// into <paramref name="unit"/>.
    /// </summary>
    public static double FromSi(double siValue, EnkiQuantity quantity, Enum unit)
    {
        // Build the IQuantity in its base (SI) unit, then convert to the
        // target unit and read the value.
        var siQuantity = AsQuantity(siValue, quantity, BaseUnitOf(quantity));
        return siQuantity.As(unit);
    }

    /// <summary>
    /// Conversion path for quantities UnitsNet doesn't model
    /// (<see cref="EnkiQuantity.GammaRay"/>,
    /// <see cref="EnkiQuantity.PhotoelectricFactor"/>,
    /// <see cref="EnkiQuantity.SonicTransitTime"/>,
    /// <see cref="EnkiQuantity.Porosity"/>,
    /// <see cref="EnkiQuantity.Dimensionless"/>). Most are identity —
    /// the SI representation IS the display representation. Sonic
    /// transit time is the exception: it's stored as s/m (SI) but
    /// displayed as μs/ft (Field) or μs/m (Metric).
    /// </summary>
    public static double ConvertOilfield(double siValue, EnkiQuantity quantity, UnitSystem system) =>
        quantity switch
        {
            // s/m → μs/ft : 1 s/m = 1e6 μs/m = 1e6 / 3.28084 μs/ft ≈ 304_800 μs/ft
            // Equivalently, μs/ft = (s/m) × 3.048e5
            EnkiQuantity.SonicTransitTime when system == UnitSystem.Field   => siValue * 3.048e5,
            // s/m → μs/m : × 1e6
            EnkiQuantity.SonicTransitTime when system == UnitSystem.Metric  => siValue * 1e6,
            // SI: s/m as stored
            EnkiQuantity.SonicTransitTime                                   => siValue,

            // Everything else is identity — calibration-defined or pure
            // ratio so there's nothing to scale by preset.
            _ => siValue,
        };

    /// <summary>
    /// Bridge from (value, quantity, unit) to a UnitsNet IQuantity. The
    /// unit's enum type must match the quantity — type checks happen via
    /// the cast inside each switch arm.
    /// </summary>
    private static IQuantity AsQuantity(double value, EnkiQuantity quantity, Enum unit) =>
        quantity switch
        {
            EnkiQuantity.Length              => Length.From(value, (LengthUnit)unit),
            EnkiQuantity.Angle               => Angle.From(value, (AngleUnit)unit),
            EnkiQuantity.Velocity            => Speed.From(value, (SpeedUnit)unit),
            EnkiQuantity.Acceleration        => Acceleration.From(value, (AccelerationUnit)unit),
            EnkiQuantity.RotationRate        => RotationalSpeed.From(value, (RotationalSpeedUnit)unit),
            EnkiQuantity.Time                => Duration.From(value, (DurationUnit)unit),
            EnkiQuantity.Mass                => Mass.From(value, (MassUnit)unit),
            EnkiQuantity.Force               => Force.From(value, (ForceUnit)unit),
            EnkiQuantity.Torque              => Torque.From(value, (TorqueUnit)unit),
            EnkiQuantity.Pressure            => Pressure.From(value, (PressureUnit)unit),
            EnkiQuantity.Density             => Density.From(value, (DensityUnit)unit),
            EnkiQuantity.VolumetricFlowRate  => VolumeFlow.From(value, (VolumeFlowUnit)unit),
            EnkiQuantity.DynamicViscosity    => DynamicViscosity.From(value, (DynamicViscosityUnit)unit),
            EnkiQuantity.Volume              => Volume.From(value, (VolumeUnit)unit),
            EnkiQuantity.Temperature         => Temperature.From(value, (TemperatureUnit)unit),
            EnkiQuantity.MagneticFluxDensity => MagneticField.From(value, (MagneticFieldUnit)unit),
            EnkiQuantity.Resistivity         => ElectricResistivity.From(value, (ElectricResistivityUnit)unit),
            EnkiQuantity.Conductivity        => ElectricConductivity.From(value, (ElectricConductivityUnit)unit),
            EnkiQuantity.Voltage             => ElectricPotential.From(value, (ElectricPotentialUnit)unit),

            // Oilfield-specific: callers should be using ConvertOilfield
            // for these. Routing them here is a usage error (no UnitsNet
            // quantity exists to back the conversion).
            EnkiQuantity.GammaRay            or
            EnkiQuantity.PhotoelectricFactor or
            EnkiQuantity.SonicTransitTime    or
            EnkiQuantity.Porosity            or
            EnkiQuantity.Dimensionless       =>
                throw new InvalidOperationException(
                    $"{quantity} has no UnitsNet representation. " +
                    $"Use UnitConverter.ConvertOilfield instead."),

            _ => throw new NotSupportedException(
                $"Quantity {quantity} is not yet wired in UnitConverter."),
        };

    /// <summary>
    /// SI base unit per quantity. Used internally to build an IQuantity
    /// already in SI when reversing a conversion (FromSi). Mirror of the
    /// "SI" preset row in <see cref="UnitSystemPresets"/> but kept here
    /// because the presets table is for display while this is for math.
    /// </summary>
    private static Enum BaseUnitOf(EnkiQuantity quantity) =>
        quantity switch
        {
            EnkiQuantity.Length              => LengthUnit.Meter,
            EnkiQuantity.Angle               => AngleUnit.Radian,
            EnkiQuantity.Velocity            => SpeedUnit.MeterPerSecond,
            EnkiQuantity.Acceleration        => AccelerationUnit.MeterPerSecondSquared,
            EnkiQuantity.RotationRate        => RotationalSpeedUnit.RadianPerSecond,
            EnkiQuantity.Time                => DurationUnit.Second,
            EnkiQuantity.Mass                => MassUnit.Kilogram,
            EnkiQuantity.Force               => ForceUnit.Newton,
            EnkiQuantity.Torque              => TorqueUnit.NewtonMeter,
            EnkiQuantity.Pressure            => PressureUnit.Pascal,
            EnkiQuantity.Density             => DensityUnit.KilogramPerCubicMeter,
            EnkiQuantity.VolumetricFlowRate  => VolumeFlowUnit.CubicMeterPerSecond,
            EnkiQuantity.DynamicViscosity    => DynamicViscosityUnit.PascalSecond,
            EnkiQuantity.Volume              => VolumeUnit.CubicMeter,
            EnkiQuantity.Temperature         => TemperatureUnit.Kelvin,
            EnkiQuantity.MagneticFluxDensity => MagneticFieldUnit.Tesla,
            EnkiQuantity.Resistivity         => ElectricResistivityUnit.OhmMeter,
            EnkiQuantity.Conductivity        => ElectricConductivityUnit.SiemensPerMeter,
            EnkiQuantity.Voltage             => ElectricPotentialUnit.Volt,

            _ => throw new NotSupportedException(
                $"BaseUnitOf is not defined for {quantity} — likely an oilfield-specific quantity."),
        };
}

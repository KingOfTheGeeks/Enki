using UnitsNet.Units;

namespace SDI.Enki.Core.Units;

/// <summary>
/// Lookup of "under preset X, what unit is used for quantity Y?" — the
/// concrete mapping that <see cref="UnitSystem"/> labels resolve to.
///
/// <para>
/// Two shapes per entry because downstream code needs both:
/// <list type="bullet">
///   <item>A UnitsNet <see cref="Enum"/> (boxed — each UnitsNet quantity
///   has its own strongly-typed unit enum, so we can't hold them in one
///   generic slot without boxing). Use sites cast to the specific enum
///   when doing math via <c>LengthUnit.Foot</c>, <c>PressureUnit.Psi</c>, etc.</item>
///   <item>A short abbreviation string for UI display
///   (<c>"ft"</c>, <c>"psi"</c>, <c>"ppg"</c>). Avoids a second trip
///   through UnitsNet's formatter for every row we render.</item>
/// </list>
/// </para>
///
/// <para>
/// Oilfield-specific quantities (GAPI, PhotoelectricFactor) don't have
/// UnitsNet coverage today; their <c>UnitEnum</c> is null and they're
/// handled as simple strings. The <c>Measurement</c> struct in Phase 7b
/// will carry a fallback path for these.
/// </para>
///
/// <para>
/// Every entry is a deliberate oilfield-norm choice, not a round-trip
/// of SI. "Metric" picks bar rather than Pa and °C rather than K because
/// that's what appears on a metric-country driller's display — strict
/// SI lives under <see cref="UnitSystem.SI"/>.
/// </para>
/// </summary>
public static class UnitSystemPresets
{
    /// <summary>Unit choice plus display abbreviation for one cell of the preset matrix.</summary>
    public readonly record struct Choice(Enum? UnitEnum, string Abbreviation);

    // Keyed on (UnitSystem value, EnkiQuantity value) for cheap lookup.
    // Populated once at class-init; dictionaries are effectively readonly
    // from the outside.
    private static readonly Dictionary<(int UnitSystem, EnkiQuantity Quantity), Choice> _map =
        BuildMap();

    /// <summary>
    /// Resolve the display unit for the given preset + quantity. Throws
    /// if the preset doesn't have an entry for this quantity — the
    /// compile-time contract is "every preset covers every quantity";
    /// a gap is a bug in this table, not a runtime condition to swallow.
    /// </summary>
    public static Choice Get(UnitSystem system, EnkiQuantity quantity)
    {
        if (_map.TryGetValue((system.Value, quantity), out var choice))
            return choice;

        throw new InvalidOperationException(
            $"No unit preset for {system.Name} / {quantity}. " +
            $"Add an entry in {nameof(UnitSystemPresets)}.{nameof(BuildMap)}.");
    }

    /// <summary>Short form convenience — the abbreviation only.</summary>
    public static string Abbreviation(UnitSystem system, EnkiQuantity quantity) =>
        Get(system, quantity).Abbreviation;

    private static Dictionary<(int, EnkiQuantity), Choice> BuildMap()
    {
        var m = new Dictionary<(int, EnkiQuantity), Choice>();

        // ---------- Field (US oilfield) ----------
        Add(UnitSystem.Field, EnkiQuantity.Length,              LengthUnit.Foot,                                         "ft");
        Add(UnitSystem.Field, EnkiQuantity.Angle,               AngleUnit.Degree,                                        "°");
        Add(UnitSystem.Field, EnkiQuantity.Velocity,            SpeedUnit.FootPerHour,                                   "ft/hr");
        Add(UnitSystem.Field, EnkiQuantity.Acceleration,        AccelerationUnit.StandardGravity,                        "g");
        Add(UnitSystem.Field, EnkiQuantity.RotationRate,        RotationalSpeedUnit.RevolutionPerMinute,                 "RPM");
        Add(UnitSystem.Field, EnkiQuantity.Time,                DurationUnit.Hour,                                       "hr");
        Add(UnitSystem.Field, EnkiQuantity.Mass,                MassUnit.Pound,                                          "lb");
        Add(UnitSystem.Field, EnkiQuantity.Force,               ForceUnit.KilopoundForce,                                "klbf");
        Add(UnitSystem.Field, EnkiQuantity.Torque,              TorqueUnit.KilopoundForceFoot,                           "klbf·ft");
        Add(UnitSystem.Field, EnkiQuantity.Pressure,            PressureUnit.PoundForcePerSquareInch,                    "psi");
        Add(UnitSystem.Field, EnkiQuantity.Density,             DensityUnit.PoundPerUSGallon,                            "ppg");
        Add(UnitSystem.Field, EnkiQuantity.VolumetricFlowRate,  VolumeFlowUnit.OilBarrelPerMinute,                       "bbl/min");
        Add(UnitSystem.Field, EnkiQuantity.DynamicViscosity,    DynamicViscosityUnit.Centipoise,                         "cP");
        Add(UnitSystem.Field, EnkiQuantity.Volume,              VolumeUnit.OilBarrel,                                    "bbl");
        Add(UnitSystem.Field, EnkiQuantity.Temperature,         TemperatureUnit.DegreeFahrenheit,                        "°F");
        Add(UnitSystem.Field, EnkiQuantity.MagneticFluxDensity, MagneticFieldUnit.Nanotesla,                       "nT");
        // GammaRay (GAPI) and PhotoelectricFactor (b/e) have no UnitsNet
        // analog — stored/displayed as bare doubles with a fixed unit string.
        AddOilfield(UnitSystem.Field, EnkiQuantity.GammaRay,             "GAPI");
        AddOilfield(UnitSystem.Field, EnkiQuantity.PhotoelectricFactor,  "b/e");
        Add(UnitSystem.Field, EnkiQuantity.Resistivity,         ElectricResistivityUnit.OhmMeter,                        "Ω·m");
        Add(UnitSystem.Field, EnkiQuantity.Conductivity,        ElectricConductivityUnit.SiemensPerMeter,                "S/m");
        // Sonic transit time: oilfield idiom is μs/ft. UnitsNet doesn't
        // expose a dedicated Slowness quantity today; we carry the
        // abbreviation and convert ad-hoc in the Measurement layer later.
        AddOilfield(UnitSystem.Field, EnkiQuantity.SonicTransitTime,     "μs/ft");
        AddOilfield(UnitSystem.Field, EnkiQuantity.Porosity,             "v/v");
        Add(UnitSystem.Field, EnkiQuantity.Voltage,             ElectricPotentialUnit.Millivolt,                         "mV");
        AddOilfield(UnitSystem.Field, EnkiQuantity.Dimensionless,        "");

        // ---------- Metric (metric-country oilfield) ----------
        Add(UnitSystem.Metric, EnkiQuantity.Length,              LengthUnit.Meter,                                        "m");
        Add(UnitSystem.Metric, EnkiQuantity.Angle,               AngleUnit.Degree,                                        "°");
        Add(UnitSystem.Metric, EnkiQuantity.Velocity,            SpeedUnit.MeterPerHour,                                  "m/hr");
        Add(UnitSystem.Metric, EnkiQuantity.Acceleration,        AccelerationUnit.StandardGravity,                        "g");
        Add(UnitSystem.Metric, EnkiQuantity.RotationRate,        RotationalSpeedUnit.RevolutionPerMinute,                 "RPM");
        Add(UnitSystem.Metric, EnkiQuantity.Time,                DurationUnit.Hour,                                       "hr");
        Add(UnitSystem.Metric, EnkiQuantity.Mass,                MassUnit.Kilogram,                                       "kg");
        Add(UnitSystem.Metric, EnkiQuantity.Force,               ForceUnit.Kilonewton,                                    "kN");
        Add(UnitSystem.Metric, EnkiQuantity.Torque,              TorqueUnit.KilonewtonMeter,                              "kN·m");
        Add(UnitSystem.Metric, EnkiQuantity.Pressure,            PressureUnit.Bar,                                        "bar");
        Add(UnitSystem.Metric, EnkiQuantity.Density,             DensityUnit.KilogramPerCubicMeter,                       "kg/m³");
        Add(UnitSystem.Metric, EnkiQuantity.VolumetricFlowRate,  VolumeFlowUnit.LiterPerMinute,                           "L/min");
        Add(UnitSystem.Metric, EnkiQuantity.DynamicViscosity,    DynamicViscosityUnit.Centipoise,                         "cP");
        Add(UnitSystem.Metric, EnkiQuantity.Volume,              VolumeUnit.CubicMeter,                                   "m³");
        Add(UnitSystem.Metric, EnkiQuantity.Temperature,         TemperatureUnit.DegreeCelsius,                           "°C");
        Add(UnitSystem.Metric, EnkiQuantity.MagneticFluxDensity, MagneticFieldUnit.Nanotesla,                       "nT");
        AddOilfield(UnitSystem.Metric, EnkiQuantity.GammaRay,             "GAPI");
        AddOilfield(UnitSystem.Metric, EnkiQuantity.PhotoelectricFactor,  "b/e");
        Add(UnitSystem.Metric, EnkiQuantity.Resistivity,         ElectricResistivityUnit.OhmMeter,                        "Ω·m");
        Add(UnitSystem.Metric, EnkiQuantity.Conductivity,        ElectricConductivityUnit.SiemensPerMeter,                "S/m");
        AddOilfield(UnitSystem.Metric, EnkiQuantity.SonicTransitTime,     "μs/m");
        AddOilfield(UnitSystem.Metric, EnkiQuantity.Porosity,             "v/v");
        Add(UnitSystem.Metric, EnkiQuantity.Voltage,             ElectricPotentialUnit.Millivolt,                         "mV");
        AddOilfield(UnitSystem.Metric, EnkiQuantity.Dimensionless,        "");

        // ---------- SI (strict) ----------
        Add(UnitSystem.SI, EnkiQuantity.Length,              LengthUnit.Meter,                                        "m");
        Add(UnitSystem.SI, EnkiQuantity.Angle,               AngleUnit.Radian,                                        "rad");
        Add(UnitSystem.SI, EnkiQuantity.Velocity,            SpeedUnit.MeterPerSecond,                                "m/s");
        Add(UnitSystem.SI, EnkiQuantity.Acceleration,        AccelerationUnit.MeterPerSecondSquared,                  "m/s²");
        Add(UnitSystem.SI, EnkiQuantity.RotationRate,        RotationalSpeedUnit.RadianPerSecond,                     "rad/s");
        Add(UnitSystem.SI, EnkiQuantity.Time,                DurationUnit.Second,                                     "s");
        Add(UnitSystem.SI, EnkiQuantity.Mass,                MassUnit.Kilogram,                                       "kg");
        Add(UnitSystem.SI, EnkiQuantity.Force,               ForceUnit.Newton,                                        "N");
        Add(UnitSystem.SI, EnkiQuantity.Torque,              TorqueUnit.NewtonMeter,                                  "N·m");
        Add(UnitSystem.SI, EnkiQuantity.Pressure,            PressureUnit.Pascal,                                     "Pa");
        Add(UnitSystem.SI, EnkiQuantity.Density,             DensityUnit.KilogramPerCubicMeter,                       "kg/m³");
        Add(UnitSystem.SI, EnkiQuantity.VolumetricFlowRate,  VolumeFlowUnit.CubicMeterPerSecond,                      "m³/s");
        Add(UnitSystem.SI, EnkiQuantity.DynamicViscosity,    DynamicViscosityUnit.PascalSecond,                       "Pa·s");
        Add(UnitSystem.SI, EnkiQuantity.Volume,              VolumeUnit.CubicMeter,                                   "m³");
        Add(UnitSystem.SI, EnkiQuantity.Temperature,         TemperatureUnit.Kelvin,                                  "K");
        Add(UnitSystem.SI, EnkiQuantity.MagneticFluxDensity, MagneticFieldUnit.Tesla,                           "T");
        AddOilfield(UnitSystem.SI, EnkiQuantity.GammaRay,             "GAPI");
        AddOilfield(UnitSystem.SI, EnkiQuantity.PhotoelectricFactor,  "b/e");
        Add(UnitSystem.SI, EnkiQuantity.Resistivity,         ElectricResistivityUnit.OhmMeter,                        "Ω·m");
        Add(UnitSystem.SI, EnkiQuantity.Conductivity,        ElectricConductivityUnit.SiemensPerMeter,                "S/m");
        AddOilfield(UnitSystem.SI, EnkiQuantity.SonicTransitTime,     "s/m");
        AddOilfield(UnitSystem.SI, EnkiQuantity.Porosity,             "v/v");
        Add(UnitSystem.SI, EnkiQuantity.Voltage,             ElectricPotentialUnit.Volt,                              "V");
        AddOilfield(UnitSystem.SI, EnkiQuantity.Dimensionless,        "");

        return m;

        void Add(UnitSystem sys, EnkiQuantity q, Enum unit, string abbrev) =>
            m[(sys.Value, q)] = new Choice(unit, abbrev);

        void AddOilfield(UnitSystem sys, EnkiQuantity q, string abbrev) =>
            m[(sys.Value, q)] = new Choice(null, abbrev);
    }
}

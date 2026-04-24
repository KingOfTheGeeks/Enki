namespace SDI.Enki.Core.Units;

/// <summary>
/// The physical quantities Enki deals with across drilling + logging.
/// A superset of what UnitsNet covers out of the box plus the oilfield
/// specialties (GAPI, ppg-style density, sonic transit time as a proper
/// quantity, etc.). Every column or field storing a scalar measurement
/// should correspond to exactly one of these.
///
/// <para>
/// Int values are wire-stable and persisted in override tables / audit
/// records. Do not renumber.
/// </para>
///
/// <para>
/// Dimensionless is the escape hatch for pure ratios, counts, and
/// fractions that genuinely have no unit — don't use it as a cop-out
/// for "I haven't decided the unit yet".
/// </para>
/// </summary>
public enum EnkiQuantity
{
    // ---- kinematics / geometry ----
    /// <summary>Depth, MD, TVD, displacements. SI: meters.</summary>
    Length              = 1,
    /// <summary>Inclination, azimuth, dip, declination, tool face. SI: radians.</summary>
    Angle               = 2,
    /// <summary>ROP, logging speed. SI: m/s.</summary>
    Velocity            = 3,
    /// <summary>Tool gravity components (Gx/Gy/Gz). SI: m/s².</summary>
    Acceleration        = 4,
    /// <summary>Rotary RPM. SI: 1/s (Hz).</summary>
    RotationRate        = 5,
    /// <summary>General time — logging duration, NPT. SI: seconds.</summary>
    Time                = 6,

    // ---- mechanics ----
    /// <summary>Drillpipe / BHA mass. SI: kilograms.</summary>
    Mass                = 10,
    /// <summary>Hookload, WOB, tension. SI: newtons.</summary>
    Force               = 11,
    /// <summary>Torque at rotary / downhole. SI: N·m.</summary>
    Torque              = 12,
    /// <summary>Mud, pore, formation pressure. SI: pascals.</summary>
    Pressure            = 13,

    // ---- fluids ----
    /// <summary>Mud weight. Oilfield idiom: ppg. SI: kg/m³.</summary>
    Density             = 20,
    /// <summary>Mud pump output, return flow. SI: m³/s.</summary>
    VolumetricFlowRate  = 21,
    /// <summary>Mud viscosity. SI: Pa·s.</summary>
    DynamicViscosity    = 22,
    /// <summary>Mud tank / hole / displacement volume. SI: m³.</summary>
    Volume              = 23,

    // ---- thermal ----
    /// <summary>Bottom-hole, surface, flowline temperature. SI: kelvins.</summary>
    Temperature         = 30,

    // ---- magnetics + ranging (SDI specialty) ----
    /// <summary>BTotal, Bx / By / Bz. SI: tesla (but we show in nT universally).</summary>
    MagneticFluxDensity = 40,

    // ---- logging (wireline / LWD / MWD) ----
    /// <summary>Gamma ray. Calibration-defined, non-SI. Unit: GAPI (API GR).</summary>
    GammaRay            = 50,
    /// <summary>Formation resistivity. SI: Ω·m.</summary>
    Resistivity         = 51,
    /// <summary>Formation conductivity. SI: S/m (shown as mS/m typically).</summary>
    Conductivity        = 52,
    /// <summary>Sonic slowness / transit time. Oilfield idiom: μs/ft. SI: s/m.</summary>
    SonicTransitTime    = 53,
    /// <summary>Neutron / density-derived porosity. Unit: fraction (0–1); display
    /// may be p.u. (0–100) or %.</summary>
    Porosity            = 54,
    /// <summary>Density-tool Pe. Unit: barn/electron (b/e). Non-SI by convention.</summary>
    PhotoelectricFactor = 55,
    /// <summary>Spontaneous potential. SI: volts (shown as mV).</summary>
    Voltage             = 56,

    // ---- pure ratio / fallback ----
    /// <summary>Pure ratios, efficiencies, dimensionless fractions.</summary>
    Dimensionless       = 99,
}

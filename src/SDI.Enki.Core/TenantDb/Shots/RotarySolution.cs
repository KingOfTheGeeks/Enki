namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Computed rotating-dipole solution for a single <see cref="Rotary"/>
/// measurement. Produced by Marduk's <c>IRotaryProcessor.CreateSolution</c>.
/// Richer than a Gradient solution — carries pass-by geometry and sensor-
/// side position/orientation in addition to the rotor values.
/// </summary>
public class RotarySolution
{
    public int Id { get; set; }

    public int RotaryId { get; set; }

    // Rotor position/orientation
    public double RotorMeasuredDepth { get; set; }
    public double RotorInclination { get; set; }
    public double RotorAzimuth { get; set; }
    public int RotorMoment { get; set; }

    // Pass-by geometry (closest approach between two wells)
    public double PassByTotalDistance { get; set; }
    public double PassByApproachAngle { get; set; }
    public double DistanceToPassBy { get; set; }
    public double DistanceAtPassBy { get; set; }

    // Sensor-relative offsets
    public double NorthToSensor { get; set; }
    public double EastToSensor { get; set; }
    public double VerticalToSensor { get; set; }
    public double HighSideToSensor { get; set; }
    public double RightSideToSensor { get; set; }
    public double AxialToSensor { get; set; }

    // Sensor position/orientation
    public double SensorMeasuredDepth { get; set; }
    public double SensorInclination { get; set; }
    public double SensorAzimuth { get; set; }
    public double SensorToolface { get; set; }
    public double SensorShieldMatrixXY { get; set; }
    public double SensorShieldMatrixZ { get; set; }
    public int SensorLobe { get; set; }
    public double SensorMagnetometer { get; set; }

    // EF nav
    public Rotary? Rotary { get; set; }
}

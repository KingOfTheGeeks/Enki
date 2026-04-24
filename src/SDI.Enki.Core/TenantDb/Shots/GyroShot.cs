namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Gyro-sensor sample attached to a <see cref="Shot"/>. Unified from legacy
/// <c>GyroShot</c> + <c>RotaryGyroShot</c>; <see cref="ToolfaceOffset"/> is
/// the one Gradient-only column and is nullable for Rotary-sourced samples.
/// </summary>
public class GyroShot
{
    public int Id { get; set; }

    public int ShotId { get; set; }

    public DateTimeOffset Created { get; set; }
    public int Timestamp { get; set; }
    public int StartTimestamp { get; set; }

    public double Inclination { get; set; }
    public double Azimuth { get; set; }
    public double GyroToolface { get; set; }
    public double HighSideToolface { get; set; }
    public double EarthRateHorizontal { get; set; }
    public double Temperature { get; set; }

    public int Gain { get; set; }
    public double Noise { get; set; }
    public int AccelerometerQuality { get; set; }
    public double DeltaDrift { get; set; }
    public double DeltaBias { get; set; }

    public int Synch { get; set; }
    public int Index { get; set; }
    public int Status { get; set; }
    public int Mode { get; set; }

    /// <summary>Gradient-only.</summary>
    public double? ToolfaceOffset { get; set; }

    // EF nav
    public Shot? Shot { get; set; }
}

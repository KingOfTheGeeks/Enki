namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Unified shot record. Replaces the legacy parallel <c>GradientShots</c>
/// and <c>RotaryShots</c> tables: a single shape with nullable
/// <see cref="GradientId"/> / <see cref="RotaryId"/> parent FKs and a
/// CHECK constraint enforcing exactly one non-null. <see cref="SampleCount"/>
/// is Gradient-only and therefore nullable.
///
/// GyroShots, ToolSurveys, and ActiveFields each FK to <see cref="Id"/>
/// via a single ShotId column — simpler than the legacy's pair of
/// parallel child tables per run type.
/// </summary>
public class Shot
{
    public int Id { get; set; }

    public string ShotName { get; set; } = string.Empty;

    public DateTimeOffset FileTime { get; set; }
    public int ToolUptime { get; set; }
    public int ShotTime { get; set; }
    public int TimeStart { get; set; }
    public int TimeEnd { get; set; }
    public int NumberOfMags { get; set; }

    public int? MagneticsId { get; set; }
    public int? CalibrationsId { get; set; }

    public double Frequency { get; set; }
    public double Bandwidth { get; set; }
    public int SampleFrequency { get; set; }

    /// <summary>Gradient-only. Null for Rotary shots.</summary>
    public int? SampleCount { get; set; }

    /// <summary>Parent FK. Null when this is a rotary shot. See CHECK constraint.</summary>
    public int? GradientId { get; set; }

    /// <summary>Parent FK. Null when this is a gradient shot. See CHECK constraint.</summary>
    public int? RotaryId { get; set; }

    // EF navs
    public Magnetics? Magnetics { get; set; }
    public Calibration? Calibration { get; set; }
    public Gradient? Gradient { get; set; }
    public Rotary? Rotary { get; set; }
    public ICollection<GyroShot> GyroShots { get; set; } = new List<GyroShot>();
    public ICollection<ToolSurvey> ToolSurveys { get; set; } = new List<ToolSurvey>();
    public ICollection<ActiveField> ActiveFields { get; set; } = new List<ActiveField>();
}

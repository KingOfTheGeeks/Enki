using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Shots;

namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Unified logging record. Replaces legacy Athena's three parallel tables
/// (<c>Loggings</c>, <c>RotaryLoggings</c>, <c>PassiveLoggings</c>) — they
/// shared identical shape except for which run-type they pointed at.
///
/// Three nullable run FKs (<see cref="GradientRunId"/>, <see cref="RotaryRunId"/>,
/// <see cref="PassiveRunId"/>) plus a CHECK constraint enforce "exactly one parent."
///
/// Processing records (<c>LoggingProcessing</c>, <c>RotaryProcessing</c>,
/// <c>PassiveLoggingProcessing</c>) FK INTO this table (inverted from legacy)
/// so <see cref="Logging"/> stays clean — it does not carry three nullable
/// processing FK columns. Which of the three processing tables is populated
/// depends on the run type.
/// </summary>
public class Logging
{
    public int Id { get; set; }

    public string ShotName { get; set; } = string.Empty;

    public DateTimeOffset FileTime { get; set; }

    public int CalibrationId { get; set; }
    public int MagneticId { get; set; }
    public int LogSettingId { get; set; }

    /// <summary>Non-null when this Logging belongs to a Gradient run. CHECK: exactly one of three.</summary>
    public Guid? GradientRunId { get; set; }

    /// <summary>Non-null when this Logging belongs to a Rotary run. CHECK: exactly one of three.</summary>
    public Guid? RotaryRunId { get; set; }

    /// <summary>Non-null when this Logging belongs to a Passive run. CHECK: exactly one of three.</summary>
    public Guid? PassiveRunId { get; set; }

    // EF navs
    public Calibration? Calibration { get; set; }
    public Magnetics? Magnetics { get; set; }
    public LoggingSetting? LoggingSetting { get; set; }

    public Run? GradientRun { get; set; }
    public Run? RotaryRun { get; set; }
    public Run? PassiveRun { get; set; }

    // Inverted processing FKs: each processing table carries a LoggingId.
    public LoggingProcessing? LoggingProcessing { get; set; }
    public RotaryProcessing? RotaryProcessing { get; set; }
    public PassiveLoggingProcessing? PassiveLoggingProcessing { get; set; }

    // Children
    public ICollection<LoggingFile> Files { get; set; } = new List<LoggingFile>();
    public ICollection<Log> Logs { get; set; } = new List<Log>();
    public ICollection<LoggingTimeDepth> TimeDepths { get; set; } = new List<LoggingTimeDepth>();
    public ICollection<LoggingEfd> EfdSamples { get; set; } = new List<LoggingEfd>();
}

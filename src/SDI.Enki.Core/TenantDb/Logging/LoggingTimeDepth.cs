namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Time-to-depth mapping header for a Logging run. Unified from legacy
/// <c>LoggingTimeDepth</c> + <c>RotaryLoggingTimeDepth</c> + <c>PassiveLoggingTimeDepth</c>.
/// </summary>
public class LoggingTimeDepth
{
    public int Id { get; set; }

    public int LoggingId { get; set; }

    public string ShotName { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public double TimeInterval { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double StartDepth { get; set; }
    public double EndDepth { get; set; }

    public DateTimeOffset Closed { get; set; }

    public Logging? Logging { get; set; }

    public ICollection<LogTimeDepth> Samples { get; set; } = new List<LogTimeDepth>();
}

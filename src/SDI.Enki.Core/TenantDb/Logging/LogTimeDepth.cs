namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Individual (time, depth) sample inside a <see cref="LoggingTimeDepth"/>
/// header. Unified from legacy LogTimeDepth + RotaryLogTimeDepth +
/// PassiveLogTimeDepth (identical shapes).
/// </summary>
public class LogTimeDepth
{
    public int Id { get; set; }

    public int LoggingTimeDepthId { get; set; }

    public double Time { get; set; }
    public double Depth { get; set; }

    public LoggingTimeDepth? LoggingTimeDepth { get; set; }
}

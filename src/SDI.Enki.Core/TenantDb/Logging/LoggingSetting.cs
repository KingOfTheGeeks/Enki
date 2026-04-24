namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// User-tuned parameters for a logging run. Legacy Athena had three parallel
/// tables (<c>LoggingSettings</c>, <c>RotaryLoggingSettings</c>,
/// <c>PassiveLoggingSettings</c>) with identical shape — unified here into one.
/// </summary>
public class LoggingSetting
{
    public int Id { get; set; }

    public bool TrackDepth { get; set; }
    public double DepthOutputInterval { get; set; }
    public bool ProcessOneDirection { get; set; }
    public bool AverageOverInterval { get; set; }
    public bool ShowQualifyPlots { get; set; }
    public bool ReverseGsSign { get; set; }
    public double DepthOffset { get; set; }
    public bool OutputEfd { get; set; }
}

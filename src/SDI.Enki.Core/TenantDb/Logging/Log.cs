namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Per-depth sensor sample inside a Logging run. Unified from legacy
/// <c>Logs</c> + <c>RotaryLogs</c> + <c>PassiveLogs</c> (identical shapes).
/// </summary>
public class Log
{
    public int Id { get; set; }

    public int LoggingId { get; set; }

    public double Depth { get; set; }

    public double Bx { get; set; }
    public double By { get; set; }
    public double Bz { get; set; }

    public double Gx { get; set; }
    public double Gy { get; set; }
    public double Gz { get; set; }

    public Logging? Logging { get; set; }
}

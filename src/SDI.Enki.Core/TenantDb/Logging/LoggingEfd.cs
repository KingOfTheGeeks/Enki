namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Electromagnetic-field-decomposition sample produced during a Logging run.
/// Unified from legacy <c>loggingEfd</c> + <c>RotaryloggingEfd</c> +
/// <c>PassiveLoggingEfd</c> (identical shapes).
/// </summary>
public class LoggingEfd
{
    public int Id { get; set; }

    public int LoggingId { get; set; }

    public double MeasuredDepth { get; set; }

    public double Bx { get; set; }
    public double By { get; set; }
    public double Bz { get; set; }

    public double Gx { get; set; }
    public double Gy { get; set; }
    public double Gz { get; set; }

    public Logging? Logging { get; set; }
}

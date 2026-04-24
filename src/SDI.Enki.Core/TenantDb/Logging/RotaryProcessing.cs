namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Processing parameters for a <b>Rotary</b> logging run. Kept as a separate
/// table from <see cref="LoggingProcessing"/> because Rotary processing
/// carries real additional columns (<see cref="Units"/>, cut-offs, current,
/// etc.) — unifying would pollute the Gradient/Passive cases with irrelevant
/// nullable fields.
///
/// <b>FK direction inverted from legacy</b>: this table carries
/// <see cref="LoggingId"/> → <c>Loggings.Id</c>.
/// </summary>
public class RotaryProcessing(int loggingId)
{
    public int Id { get; set; }

    public int LoggingId { get; set; } = loggingId;

    public bool IsLodestone { get; set; }

    public bool Units { get; set; }
    public double LowCutoff { get; set; }
    public double HighCutoff { get; set; }
    public double Current { get; set; }
    public int SurveyMagUsed { get; set; }
    public bool AutoResend { get; set; }

    public Logging? Logging { get; set; }
}

namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Processing parameters for a <b>Passive</b> logging run.
///
/// <b>FK direction inverted from legacy</b>: this table carries
/// <see cref="LoggingId"/> → <c>Loggings.Id</c>.
/// </summary>
public class PassiveLoggingProcessing(int loggingId)
{
    public int Id { get; set; }

    public int LoggingId { get; set; } = loggingId;

    public bool IsLodestone { get; set; }

    public Logging? Logging { get; set; }
}

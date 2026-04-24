namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Processing parameters for a <b>Gradient</b> logging run.
///
/// <b>FK direction inverted from legacy</b>: this table carries
/// <see cref="LoggingId"/> → <c>Loggings.Id</c>, rather than <c>Loggings</c>
/// carrying a processing-id pointer. Decision captured in the design report:
/// keeps <see cref="Logging"/> clean (no trio of nullable processing-FK
/// columns + compound CHECK). The 1:1 relationship is enforced by a UNIQUE
/// index on <see cref="LoggingId"/>.
/// </summary>
public class LoggingProcessing(int loggingId)
{
    public int Id { get; set; }

    public int LoggingId { get; set; } = loggingId;

    public bool IsLodestone { get; set; }

    public Logging? Logging { get; set; }
}

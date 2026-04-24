namespace SDI.Enki.Core.TenantDb.Logging;

/// <summary>
/// Binary payload attached to a <see cref="Logging"/>. Unified from legacy
/// <c>LoggingFiles</c> + <c>RotaryLoggingFiles</c> + <c>PassiveLoggingFiles</c>
/// (identical shapes). CASCADE-deleted with parent.
/// </summary>
public class LoggingFile
{
    public int Id { get; set; }

    public int LoggingId { get; set; }

    public string Name { get; set; } = string.Empty;
    public byte[]? File { get; set; }

    public DateTime Timestamp { get; set; } = new DateTime(1900, 1, 1);

    public Logging? Logging { get; set; }
}

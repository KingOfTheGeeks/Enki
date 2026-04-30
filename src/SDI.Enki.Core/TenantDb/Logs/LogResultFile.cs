namespace SDI.Enki.Core.TenantDb.Logs;

/// <summary>
/// One output file Marduk produced from a <see cref="Log"/>'s
/// binary + config. Typically a LAS file (Log ASCII Standard) but
/// the entity is content-type agnostic — anything Marduk emits goes
/// here.
///
/// <para>
/// Replaces the legacy <c>LogFile</c> entity. Same shape (FileName
/// + ContentType + Bytes), different semantic: <c>LogFile</c> was
/// the captured input file in the legacy schema; <c>LogResultFile</c>
/// is the produced output file. Capture binary lives on
/// <c>Log.Binary</c> in the new shape.
/// </para>
///
/// <para>
/// Cascade-deleted with the parent Log.
/// </para>
/// </summary>
public class LogResultFile
{
    public int Id { get; set; }

    public int LogId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public byte[]? Bytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // EF nav
    public Log? Log { get; set; }
}

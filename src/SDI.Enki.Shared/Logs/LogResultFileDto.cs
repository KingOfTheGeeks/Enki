namespace SDI.Enki.Shared.Logs;

/// <summary>
/// Metadata for one result file Marduk produced from a
/// <c>Log</c>'s binary + config (typically a LAS file). The bytes
/// themselves stream via
/// <c>GET /logs/{logId}/result-files/{fileId}</c>.
/// </summary>
public sealed record LogResultFileDto(
    int             Id,
    int             LogId,
    string          FileName,
    string          ContentType,
    DateTimeOffset  CreatedAt);

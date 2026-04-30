namespace SDI.Enki.Shared.Logs;

/// <summary>
/// Full detail for a single Log. Phase 2 reshape — covers identity
/// + audit + the captured Binary metadata + JSON config + a list of
/// result files (LAS or similar) Marduk produced.
///
/// <para>
/// The actual binary bytes aren't carried here — streamed via
/// <c>GET /logs/{id}/binary</c>. Result files are referenced by id
/// + filename + content-type; bytes streamed via
/// <c>GET /logs/{id}/result-files/{fileId}</c>.
/// </para>
/// </summary>
public sealed record LogDetailDto(
    int             Id,
    Guid            RunId,
    string          ShotName,
    DateTimeOffset  FileTime,
    int?            CalibrationId,

    // Audit
    DateTimeOffset  CreatedAt,
    string?         CreatedBy,
    DateTimeOffset? UpdatedAt,
    string?         UpdatedBy,

    // Captured-data metadata (no bytes)
    bool            HasBinary,
    string?         BinaryName,
    DateTimeOffset? BinaryUploadedAt,
    string?         ConfigJson,
    DateTimeOffset? ConfigUpdatedAt,

    // Result files Marduk produced
    IReadOnlyList<LogResultFileDto> ResultFiles,

    string?         RowVersion);

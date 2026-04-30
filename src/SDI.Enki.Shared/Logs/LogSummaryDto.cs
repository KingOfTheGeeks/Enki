namespace SDI.Enki.Shared.Logs;

/// <summary>
/// One log row in a Run's logs list. Phase 2 reshape: lookup-FK
/// metadata (was Calibration / Magnetics / LogSetting) collapses to
/// just <c>CalibrationId</c>; the captured-data shape adds
/// <c>HasBinary</c> as a boolean projection so the grid renders an
/// "uploaded?" tick without pulling the bytes. Result files are
/// counted via <c>ResultFileCount</c>; full file metadata is on
/// <see cref="LogDetailDto"/>.
/// </summary>
public sealed record LogSummaryDto(
    int             Id,
    Guid            RunId,
    string          ShotName,
    DateTimeOffset  FileTime,
    int?            CalibrationId,
    bool            HasBinary,
    int             ResultFileCount,
    string?         RowVersion);

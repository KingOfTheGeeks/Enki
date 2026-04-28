namespace SDI.Enki.Shared.Shots;

/// <summary>
/// One Shot row in a Run's Shots list. Lightweight projection —
/// excludes the binary blobs and the JSON config/result columns
/// (those land on the detail DTO + are streamed separately for
/// the binaries via dedicated download endpoints).
///
/// <para>
/// <c>HasBinary</c> / <c>HasGyroBinary</c> are derived flags
/// (`Binary IS NOT NULL` / `GyroBinary IS NOT NULL`) so the grid
/// can render an "uploaded?" tick without pulling the bytes.
/// <c>ResultStatus</c> / <c>GyroResultStatus</c> reflect the
/// future-calc state machine (Pending / Computing / Success /
/// Failed; null = idle).
/// </para>
/// </summary>
public sealed record ShotSummaryDto(
    int             Id,
    Guid            RunId,
    string          ShotName,
    DateTimeOffset  FileTime,
    int?            CalibrationId,
    bool            HasBinary,
    bool            HasGyroBinary,
    string?         ResultStatus,
    string?         GyroResultStatus,
    string?         RowVersion);

namespace SDI.Enki.Shared.Shots;

/// <summary>
/// Full detail for a single Shot — identity + audit + primary
/// capture metadata + JSON config + JSON result + paired gyro
/// capture metadata + JSON gyro config + JSON gyro result.
///
/// <para>
/// The actual binary bytes are NOT carried on this DTO — they're
/// streamed via dedicated endpoints
/// (<c>GET /shots/{id}/binary</c>, <c>GET /shots/{id}/gyro-binary</c>).
/// This DTO carries only the <em>metadata</em> (filename, size hint
/// via HasBinary boolean, upload timestamp).
/// </para>
///
/// <para>
/// <c>ConfigJson</c> / <c>ResultJson</c> / <c>GyroConfigJson</c> /
/// <c>GyroResultJson</c> are passed through as raw strings — the
/// client can parse / pretty-print / JSON-tree-view them as it
/// pleases. Schema-erased on purpose so Marduk's parameter and
/// result shapes can iterate without DB migrations.
/// </para>
/// </summary>
public sealed record ShotDetailDto(
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

    // Primary capture
    bool            HasBinary,
    string?         BinaryName,
    DateTimeOffset? BinaryUploadedAt,
    string?         ConfigJson,
    DateTimeOffset? ConfigUpdatedAt,
    string?         ResultJson,
    DateTimeOffset? ResultComputedAt,
    string?         ResultMardukVersion,
    string?         ResultStatus,
    string?         ResultError,

    // Gyro capture
    bool            HasGyroBinary,
    string?         GyroBinaryName,
    DateTimeOffset? GyroBinaryUploadedAt,
    string?         GyroConfigJson,
    DateTimeOffset? GyroConfigUpdatedAt,
    string?         GyroResultJson,
    DateTimeOffset? GyroResultComputedAt,
    string?         GyroResultMardukVersion,
    string?         GyroResultStatus,
    string?         GyroResultError,

    string?         RowVersion);

namespace SDI.Enki.Shared.Licensing;

/// <summary>
/// Detail view for a single License. Includes the snapshot JSONs as raw
/// strings so the page can pretty-print or expand them; the encrypted
/// <c>.lic</c> bytes are NOT included — those come from a separate
/// <c>GET /licenses/{id}/file</c> endpoint as <c>application/octet-stream</c>.
/// </summary>
public sealed record LicenseDetailDto(
    Guid           Id,
    Guid           LicenseKey,
    string         Licensee,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string         Status,
    LicenseFeaturesDto Features,
    IReadOnlyList<LicenseToolSnapshotDto>        Tools,
    IReadOnlyList<LicenseCalibrationSnapshotDto> Calibrations,
    int            FileSizeBytes,
    string?        RevokedReason,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    string?        CreatedBy);

public sealed record LicenseToolSnapshotDto(
    Guid Id,
    int  SerialNumber,
    string FirmwareVersion,
    int  MagnetometerCount,
    int  AccelerometerCount);

public sealed record LicenseCalibrationSnapshotDto(
    Guid Id,
    Guid ToolId,
    int  ToolSerialNumber,
    string Name,
    DateTimeOffset CalibrationDate,
    string? CalibratedBy);

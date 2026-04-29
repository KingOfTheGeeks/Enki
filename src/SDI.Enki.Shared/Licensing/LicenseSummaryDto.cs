namespace SDI.Enki.Shared.Licensing;

/// <summary>List-view shape for the master Licenses table.</summary>
public sealed record LicenseSummaryDto(
    Guid           Id,
    Guid           LicenseKey,
    string         Licensee,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string         Status,
    int            ToolCount,
    int            CalibrationCount,
    DateTimeOffset CreatedAt);

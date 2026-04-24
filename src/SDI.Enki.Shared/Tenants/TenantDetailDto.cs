namespace SDI.Enki.Shared.Tenants;

public sealed record TenantDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? DisplayName,
    string Status,
    string? ContactEmail,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeactivatedAt,
    string ActiveDatabaseName,
    string ArchiveDatabaseName,
    string? SchemaVersion);

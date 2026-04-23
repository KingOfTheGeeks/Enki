namespace SDI.Enki.Shared.Tenants;

public sealed record TenantSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string? DisplayName,
    string Status,
    string? Region,
    DateTimeOffset CreatedAt);

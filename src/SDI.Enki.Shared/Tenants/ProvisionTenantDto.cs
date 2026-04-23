namespace SDI.Enki.Shared.Tenants;

public sealed record ProvisionTenantDto(
    string Code,
    string Name,
    string? DisplayName = null,
    string? Region = null,
    string? ContactEmail = null,
    string? Notes = null);

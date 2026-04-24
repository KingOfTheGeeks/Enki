namespace SDI.Enki.Infrastructure.Provisioning.Models;

/// <summary>
/// Inputs for provisioning a new tenant. Code becomes part of the two
/// database names (Enki_{Code}_Active and Enki_{Code}_Archive); it must
/// be unique across all tenants and valid as a SQL Server identifier.
///
/// Region deliberately lives on Jobs, not Tenants — a tenant is a
/// corporation and may operate globally. Where the work happens is a
/// per-Job attribute.
/// </summary>
public sealed record ProvisionTenantRequest(
    string Code,
    string Name,
    string? DisplayName = null,
    string? ContactEmail = null,
    string? Notes = null,
    string? ServerInstanceOverride = null);

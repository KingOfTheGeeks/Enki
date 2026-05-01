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
/// <param name="SeedSampleData">
/// When true, the newly-provisioned tenant's Active DB receives a
/// curated set of demo Jobs via <c>DevTenantSeeder</c>. Per-call so
/// only the canonical demo tenants (PERMIAN, BAKKEN, NORTHSEA,
/// CARNARVON) get demo data, not every tenant a user creates from
/// the UI. <c>DevMasterSeeder</c> sets it true (with a matching
/// SeedSpec); <c>TenantsController.Provision</c> and the Migrator
/// CLI leave it at the default false.
/// </param>
/// <param name="SeedSpec">
/// Per-tenant differentiation handed to <c>DevTenantSeeder</c> when
/// <see cref="SeedSampleData"/> is true. Carries the job + well names,
/// region label, unit-system preference, and surface coordinates that
/// distinguish one demo tenant from another (Permian Crest vs Bakken
/// Ridge vs Brent Atlantic vs Carnarvon, etc.). Required when
/// SeedSampleData is true; ignored otherwise.
/// </param>
public sealed record ProvisionTenantRequest(
    string Code,
    string Name,
    string? DisplayName = null,
    string? ContactEmail = null,
    string? Notes = null,
    string? ServerInstanceOverride = null,
    bool SeedSampleData = false,
    TenantSeedSpec? SeedSpec = null,
    /// <summary>
    /// Optional override for the new <c>Tenant.Id</c>. <c>DevMasterSeeder</c>
    /// passes a pinned ID from <c>SeedTenants</c> so that
    /// <c>SeedUsers</c> Tenant-type entries can reference the same
    /// GUID without a cross-host lookup. Null (the normal path) lets
    /// the entity default to <c>Guid.NewGuid()</c>.
    /// </summary>
    Guid? TenantId = null);

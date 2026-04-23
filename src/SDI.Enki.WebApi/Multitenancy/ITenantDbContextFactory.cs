using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Multitenancy;

/// <summary>
/// Builds <see cref="TenantDbContext"/> instances bound to the current
/// request's tenant. Services depend on this rather than injecting
/// TenantDbContext directly — that would require per-request scoped DI
/// wiring tricks, and would make cross-tenant leaks too easy.
/// </summary>
public interface ITenantDbContextFactory
{
    /// <summary>Creates a context pointing at the tenant's Active database (read-write).</summary>
    TenantDbContext CreateActive();

    /// <summary>Creates a context pointing at the tenant's Archive database (read-only).</summary>
    TenantDbContext CreateArchive();
}

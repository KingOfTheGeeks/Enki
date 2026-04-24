using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the master DbContext, ProvisioningOptions, and tenant
    /// provisioning service. Host applications (Migrator, WebApi) call this
    /// once with their master connection string.
    /// </summary>
    public static IServiceCollection AddEnkiInfrastructure(
        this IServiceCollection services,
        string masterConnectionString)
    {
        if (string.IsNullOrWhiteSpace(masterConnectionString))
            throw new ArgumentException("Master connection string must be non-empty.", nameof(masterConnectionString));

        services.AddDbContext<AthenaMasterDbContext>(opt =>
            opt.UseSqlServer(masterConnectionString));

        services.AddSingleton(new ProvisioningOptions(masterConnectionString));
        services.AddScoped<DatabaseAdmin>();
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();

        // No IEntityLookup DI registration — find-or-create is an extension
        // method on TenantDbContext (see Data/Lookups/TenantDbContextLookupExtensions).
        // Avoids the DI puzzle where IEntityLookup<T> would need a scoped
        // TenantDbContext that Enki deliberately does not container-register.
        return services;
    }
}

using AMR.Core.IO;
using AMR.Core.Survey.Implementations;
using AMR.Core.Survey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Infrastructure.Auditing;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Infrastructure.Surveys;

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
        string masterConnectionString,
        bool seedSampleData = false)
    {
        if (string.IsNullOrWhiteSpace(masterConnectionString))
            throw new ArgumentException("Master connection string must be non-empty.", nameof(masterConnectionString));

        services.AddDbContext<EnkiMasterDbContext>(opt =>
            opt.UseSqlServer(masterConnectionString, sql =>
            {
                // Retry on SQL Server transient faults (network blip,
                // connection pool warmup after a DB drop, brief PAUSE
                // under heavy load). Six attempts × up to 10 s backoff
                // is the EF Core sample default — generous enough that
                // a cold SQL Server on a dev box stops flaking startup.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 6,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            }));

        // seedSampleData on: newly-provisioned tenants get a handful of
        // demo Jobs in their Active DB. WebApi flips this on under
        // builder.Environment.IsDevelopment(); Migrator CLI keeps the
        // default off so re-migrating production tenants never invents
        // rows behind the user's back.
        services.AddSingleton(new ProvisioningOptions(masterConnectionString, seedSampleData));
        services.AddScoped<DatabaseAdmin>();
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();

        // Fallback audit identity for hosts that don't register their own
        // (Migrator CLI, tests). WebApi / Blazor register an HttpContext-
        // backed implementation after this call; last-registration-wins
        // ensures the Http version is picked at resolve time.
        services.TryAddSingleton<ICurrentUser, SystemCurrentUser>();

        // Marduk survey-calculator + the auto-calc wrapper. Registered
        // here (not in the WebApi host) so the dev seeder — which lives
        // in Infrastructure — can recompute trajectory columns after
        // seeding a well. Both are stateless → singleton.
        services.AddSingleton<ISurveyCalculator, MinimumCurvature>();
        services.AddSingleton<ISurveyAutoCalculator, MardukSurveyAutoCalculator>();

        // Survey-file importer (CSV / TSV / whitespace / LAS 2.0).
        // Stateless; one instance shared across all requests is fine.
        services.AddSingleton<ISurveyImporter, SurveyImporter>();

        // No IEntityLookup DI registration — find-or-create is an extension
        // method on TenantDbContext (see Data/Lookups/TenantDbContextLookupExtensions).
        // Avoids the DI puzzle where IEntityLookup<T> would need a scoped
        // TenantDbContext that Enki deliberately does not container-register.
        return services;
    }
}

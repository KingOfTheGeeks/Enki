using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SDI.Enki.Migrator.Commands;

/// <summary>
/// Convenience: run <c>migrate-identity</c>, then <c>migrate-master</c>,
/// then <c>migrate-tenants</c> — the standard sequence after a host
/// update that touches schema in any of the three. Each underlying
/// step is idempotent so re-runs are safe.
///
/// <para>
/// Stops on the first failure and returns its non-zero exit code so a
/// CI pipeline catches partial failures. Tenant-DB args (<c>--tenants</c>,
/// <c>--parallel</c>, etc.) are forwarded to the inner
/// <see cref="MigrateCommand"/> unchanged.
/// </para>
/// </summary>
internal static class MigrateAllCommand
{
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string[] tenantArgs)
    {
        var identityResult = await MigrateIdentityCommand.RunAsync(services, configuration, environment);
        if (identityResult != 0) return identityResult;

        var masterResult = await MigrateMasterCommand.RunAsync(services);
        if (masterResult != 0) return masterResult;

        var tenantsResult = await MigrateCommand.RunAsync(services, tenantArgs);
        return tenantsResult;
    }
}

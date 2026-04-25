using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.DesignTime;

/// <summary>
/// Enables <c>dotnet ef migrations add</c> and <c>database update</c>
/// against <see cref="TenantDbContext"/> without booting a host. The
/// connection string is required for <c>database update</c> (which
/// connects) and harmless for <c>migrations add</c> (which doesn't).
///
/// <para>
/// <b>No fallback connection string.</b> Set <c>EnkiMasterCs</c> before
/// invoking <c>dotnet ef</c> — at design time we point at the master
/// instance because individual tenant DBs aren't enumerated here; the
/// runtime <see cref="WebApi.Multitenancy.TenantDbContextFactory"/>
/// resolves per-tenant connection strings from the master registry.
/// </para>
/// </summary>
public class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var connectionString = ConnectionStrings.RequireMaster();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(TenantDbContext).Assembly.FullName))
            .Options;

        return new TenantDbContext(options);
    }
}

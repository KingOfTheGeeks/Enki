using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.DesignTime;

/// <summary>
/// Enables `dotnet ef migrations add` and `dotnet ef database update` against
/// <see cref="TenantDbContext"/> without needing a running host application.
/// The connection string is a placeholder — at runtime, each tenant's real
/// connection string is produced by the tenant resolver from the
/// TenantDatabase table in the master DB.
/// </summary>
public class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=Enki_Tenant_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly(typeof(TenantDbContext).Assembly.FullName))
            .Options;

        return new TenantDbContext(options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.DesignTime;

/// <summary>
/// Enables `dotnet ef migrations add` and `dotnet ef database update` against
/// <see cref="AthenaMasterDbContext"/> without needing a running host application.
/// The connection string here is only used by design-time tooling; runtime
/// bindings come from the Migrator app or WebApi's configuration.
/// </summary>
public class AthenaMasterDbContextFactory : IDesignTimeDbContextFactory<AthenaMasterDbContext>
{
    public AthenaMasterDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AthenaMasterDbContext>()
            .UseSqlServer(
                // Design-time placeholder. Real connection strings live in appsettings.
                "Server=(localdb)\\MSSQLLocalDB;Database=Enki_Master_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly(typeof(AthenaMasterDbContext).Assembly.FullName))
            .Options;

        return new AthenaMasterDbContext(options);
    }
}

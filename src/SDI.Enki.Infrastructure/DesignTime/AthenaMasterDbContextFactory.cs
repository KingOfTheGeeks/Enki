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
                // Design-time placeholder. `dotnet ef migrations add` doesn't
                // actually connect; `dotnet ef database update` does. Replace
                // YOUR_USER / YOUR_PASSWORD with your dev credentials if you
                // use `database update` without an explicit --connection flag.
                "Server=10.1.7.50;Database=Enki_Master;User Id=sa;Password=!@m@nAdm1n1str@t0r;TrustServerCertificate=True;Encrypt=True;",
                sql => sql.MigrationsAssembly(typeof(AthenaMasterDbContext).Assembly.FullName))
            .Options;

        return new AthenaMasterDbContext(options);
    }
}

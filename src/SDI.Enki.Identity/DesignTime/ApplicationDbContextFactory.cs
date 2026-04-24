using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.DesignTime;

/// <summary>
/// Design-time factory for <see cref="ApplicationDbContext"/>. Enables
/// <c>dotnet ef migrations add</c> / <c>database update</c> without booting
/// the full Host. Runtime config comes from appsettings (see Program.cs).
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                "Server=10.1.7.50;Database=Enki_Identity;User Id=sa;Password=!@m@nAdm1n1str@t0r;TrustServerCertificate=True;Encrypt=True;",
                sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseOpenIddict()
            .Options;

        return new ApplicationDbContext(options);
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Auth store. Contains ASP.NET Identity's AspNet* tables plus OpenIddict's
/// client / scope / authorization / token tables. Single database
/// (<c>Enki_Identity</c>), separate from master and tenant DBs — an
/// authentication concern should never mix with drilling-domain storage.
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // OpenIddict tables are registered via UseOpenIddict() in
        // Program.cs's AddDbContext call; no further config needed here.
    }
}

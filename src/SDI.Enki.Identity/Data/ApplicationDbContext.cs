using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedUserType = SDI.Enki.Shared.Identity.UserType;

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
    /// <summary>
    /// Append-only audit table for sensitive Identity-DB actions
    /// (admin-role flips, password resets, lockouts). Populated by
    /// manual writes from <c>AdminUsersController</c>; read API at
    /// <c>/admin/audit/identity</c>.
    /// </summary>
    public DbSet<IdentityAuditLog> IdentityAuditLogs => Set<IdentityAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // OpenIddict tables are registered via UseOpenIddict() in
        // Program.cs's AddDbContext call; no further config needed here.

        // ApplicationUser.UserType: SmartEnum stored as its Name
        // (Team / External / …). Column shape stays string-shaped to
        // avoid an EF migration on the existing nvarchar(max) column —
        // capped here at 50 chars on the read side which EF treats as a
        // safe-narrowing of the in-memory type, not a schema change.
        builder.Entity<ApplicationUser>()
            .Property(u => u.UserType)
            .HasMaxLength(50)
            .HasConversion(new ValueConverter<SharedUserType?, string?>(
                v => v != null ? v.Name : null,
                v => v != null ? SharedUserType.FromName(v) : null));

        // Audit table: same index set as the tenant + master twins —
        // entity-scoped lookup (EntityType, EntityId) for "show me
        // every action against user X" and time-range index on
        // ChangedAt for the global recent-changes feed.
        builder.Entity<IdentityAuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
            e.Property(x => x.EntityId).IsRequired().HasMaxLength(100);
            e.Property(x => x.Action).IsRequired().HasMaxLength(40);
            e.Property(x => x.ChangedBy).IsRequired().HasMaxLength(100);
            e.Property(x => x.ChangedColumns).HasMaxLength(2000);

            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ChangedAt);
        });
    }
}

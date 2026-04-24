using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Migrations;
using SDI.Enki.Core.Master.Migrations.Enums;
using SDI.Enki.Core.Master.Settings;
using SDI.Enki.Core.Master.Settings.Enums;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Users;

namespace SDI.Enki.Infrastructure.Data;

/// <summary>
/// Master database context. Single instance per deployment. Holds the tenant
/// registry, canonical user records, global tool + calibration fleet, and
/// cross-tenant audit (MigrationRun). No job/run/shot data lives here —
/// that's in per-tenant databases served by <see cref="TenantDbContext"/>.
/// </summary>
public class AthenaMasterDbContext : DbContext
{
    // ICurrentUser is optional so design-time / tests / Migrator startup
    // (where no user principal exists) still construct a DbContext. When
    // null, audit fields fall back to "system".
    private readonly ICurrentUser? _currentUser;

    public AthenaMasterDbContext(DbContextOptions<AthenaMasterDbContext> options) : base(options) { }

    public AthenaMasterDbContext(
        DbContextOptions<AthenaMasterDbContext> options,
        ICurrentUser? currentUser) : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDatabase> TenantDatabases => Set<TenantDatabase>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

    public DbSet<User> Users => Set<User>();
    public DbSet<UserTemplate> UserTemplates => Set<UserTemplate>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Calibration> Calibrations => Set<Calibration>();

    public DbSet<MigrationRun> MigrationRuns => Set<MigrationRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureTenant(builder);
        ConfigureTenantDatabase(builder);
        ConfigureTenantUser(builder);
        ConfigureUser(builder);
        ConfigureUserTemplate(builder);
        ConfigureSetting(builder);
        ConfigureTool(builder);
        ConfigureCalibration(builder);
        ConfigureMigrationRun(builder);

        MasterSeedData.Apply(builder);

        ApplySingularTableNames(builder);
    }

    /// <summary>
    /// Intercepts every insert/update and stamps <see cref="IAuditable"/>
    /// properties (CreatedAt/By, UpdatedAt/By) from the current user so
    /// individual controllers / services don't have to remember to do it.
    /// RowVersion is handled by EF Core natively via the <c>IsRowVersion</c>
    /// config — we don't touch it here.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser?.UserId ?? "system";

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Respect explicit CreatedAt (entities often set it in
                    // a property initializer); otherwise stamp now.
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= actor;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    // CreatedAt/By are immutable once set — block any update.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Override EF Core's default "DbSet-name" table-naming to use the entity's
    /// CLR type name — i.e. singular rather than plural. Uses the low-level
    /// <c>IMutableEntityType.SetTableName</c> so it doesn't accidentally
    /// promote incidentally-referenced types (e.g. SmartEnum value types) to
    /// full entities the way <c>builder.Entity(clr).ToTable(...)</c> would.
    ///
    /// Entities without a table (shared-type M2M junctions via <c>UsingEntity</c>,
    /// owned types, keyless views) return null from <c>GetTableName()</c> and
    /// are skipped, preserving their explicit configuration.
    /// </summary>
    private static void ApplySingularTableNames(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes().ToList())
        {
            if (entity.GetTableName() is null) continue;
            if (entity.ClrType.IsGenericType) continue;
            entity.SetTableName(entity.ClrType.Name);
        }
    }

    private static void ConfigureTenant(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired().HasMaxLength(32);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.ContactEmail).HasMaxLength(256);
            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => TenantStatus.FromValue(v));

            // Audit fields (IAuditable) — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureTenantDatabase(ModelBuilder b)
    {
        b.Entity<TenantDatabase>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.Kind });
            e.Property(x => x.ServerInstance).IsRequired().HasMaxLength(200);
            e.Property(x => x.DatabaseName).IsRequired().HasMaxLength(100);
            e.Property(x => x.SchemaVersion).HasMaxLength(64);

            e.Property(x => x.Kind).HasConversion(
                v => v.Value,
                v => TenantDatabaseKind.FromValue(v));
            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => TenantDatabaseStatus.FromValue(v));

            e.HasOne(x => x.Tenant)
             .WithMany(t => t.Databases)
             .HasForeignKey(x => x.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTenantUser(ModelBuilder b)
    {
        b.Entity<TenantUser>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.UserId });

            e.Property(x => x.Role).HasConversion(
                v => v.Value,
                v => TenantUserRole.FromValue(v));

            e.HasOne(x => x.Tenant)
             .WithMany(t => t.Users)
             .HasForeignKey(x => x.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.User)
             .WithMany(u => u.Tenants)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureUser(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);

            // Many-to-many User ↔ UserTemplate (pure junction, auto-skip-nav)
            e.HasMany(x => x.Templates)
             .WithMany(t => t.Users)
             .UsingEntity(j => j.ToTable("UserUserTemplate"));
        });
    }

    private static void ConfigureUserTemplate(ModelBuilder b)
    {
        b.Entity<UserTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.Property(x => x.Description).IsRequired().HasMaxLength(200);
        });
    }

    private static void ConfigureSetting(ModelBuilder b)
    {
        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.JsonObject).IsRequired();
            e.Property(x => x.ObjectClass).IsRequired();

            e.Property(x => x.Type).HasConversion(
                v => v.Value,
                v => SettingType.FromValue(v));

            // Many-to-many Setting ↔ User (pure junction)
            e.HasMany(x => x.Users)
             .WithMany()
             .UsingEntity(j => j.ToTable("SettingUser"));
        });
    }

    private static void ConfigureTool(ModelBuilder b)
    {
        b.Entity<Tool>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SerialNumber).IsUnique();
            e.Property(x => x.FirmwareVersion).IsRequired().HasMaxLength(64);
        });
    }

    private static void ConfigureCalibration(ModelBuilder b)
    {
        b.Entity<Calibration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadJson).IsRequired();
            e.Property(x => x.CalibratedBy).HasMaxLength(100);

            e.HasOne(x => x.Tool)
             .WithMany(t => t.Calibrations)
             .HasForeignKey(x => x.ToolId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ToolId);
            e.HasIndex(x => x.SerialNumber);
        });
    }

    private static void ConfigureMigrationRun(ModelBuilder b)
    {
        b.Entity<MigrationRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TargetVersion).IsRequired().HasMaxLength(64);

            e.Property(x => x.Kind).HasConversion(
                v => v.Value,
                v => TenantDatabaseKind.FromValue(v));
            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => MigrationRunStatus.FromValue(v));

            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.StartedAt);
        });
    }
}

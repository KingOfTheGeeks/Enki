using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Audit;
using SDI.Enki.Core.Master.Licensing;
using SDI.Enki.Core.Master.Licensing.Enums;
using SDI.Enki.Core.Master.Migrations;
using SDI.Enki.Core.Master.Migrations.Enums;
using SDI.Enki.Core.Master.Settings;
using SDI.Enki.Core.Master.Settings.Enums;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Core.Master.Users;

namespace SDI.Enki.Infrastructure.Data;

/// <summary>
/// Master database context. Single instance per deployment. Holds the tenant
/// registry, canonical user records, global tool + calibration fleet, and
/// cross-tenant audit (MigrationRun). No job/run/shot data lives here —
/// that's in per-tenant databases served by <see cref="TenantDbContext"/>.
/// </summary>
public class EnkiMasterDbContext : DbContext
{
    // ICurrentUser is optional so design-time / tests / Migrator startup
    // (where no user principal exists) still construct a DbContext. When
    // null, audit fields fall back to "system".
    private readonly ICurrentUser? _currentUser;

    public EnkiMasterDbContext(DbContextOptions<EnkiMasterDbContext> options) : base(options) { }

    public EnkiMasterDbContext(
        DbContextOptions<EnkiMasterDbContext> options,
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
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Calibration> Calibrations => Set<Calibration>();

    public DbSet<License> Licenses => Set<License>();

    public DbSet<MigrationRun> MigrationRuns => Set<MigrationRun>();

    /// <summary>
    /// Append-only master-DB change history. Populated by
    /// <see cref="SaveChangesAsync"/> for every IAuditable mutation;
    /// no application code writes to this DbSet directly. Read API at
    /// <c>/admin/audit/master</c>.
    /// </summary>
    public DbSet<MasterAuditLog> MasterAuditLogs => Set<MasterAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureTenant(builder);
        ConfigureTenantDatabase(builder);
        ConfigureTenantUser(builder);
        ConfigureUser(builder);
        ConfigureUserTemplate(builder);
        ConfigureSetting(builder);
        ConfigureSystemSetting(builder);
        ConfigureTool(builder);
        ConfigureCalibration(builder);
        ConfigureLicense(builder);
        ConfigureMigrationRun(builder);
        ConfigureMasterAuditLog(builder);

        MasterSeedData.Apply(builder);

        ApplySingularTableNames(builder);
    }

    /// <summary>
    /// Stamps <see cref="IAuditable"/> properties on every insert /
    /// update <i>and</i> captures a parallel <see cref="MasterAuditLog"/>
    /// row for every IAuditable insert / update / delete. Mirrors
    /// <c>TenantDbContext.SaveChangesAsync</c> — same two-phase shape,
    /// same best-effort audit save (see that method's doc-comment for
    /// why a user-initiated transaction wrapping both saves doesn't
    /// compose with the SQL Server retry strategy).
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser?.UserId ?? "system";

        var pending = new List<PendingAudit>();

        foreach (var entry in ChangeTracker.Entries<IAuditable>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Respect explicit CreatedAt (entities often set it in
                    // a property initializer); otherwise stamp now.
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= actor;
                    pending.Add(new PendingAudit(entry, "Created",
                        PreCapturedEntityId: null,
                        OldValues: null,
                        ChangedColumns: null));
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    // CreatedAt/By are immutable once set — block any update.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    pending.Add(new PendingAudit(entry, "Updated",
                        PreCapturedEntityId: ReadEntityId(entry),
                        OldValues: SerializeProperties(entry, current: false),
                        ChangedColumns: ReadChangedColumns(entry)));
                    break;

                case EntityState.Deleted:
                    pending.Add(new PendingAudit(entry, "Deleted",
                        PreCapturedEntityId: ReadEntityId(entry),
                        OldValues: SerializeProperties(entry, current: false),
                        ChangedColumns: null));
                    break;
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        if (pending.Count == 0)
            return result;

        try
        {
            var auditRows = pending.Select(p => BuildAuditRow(p, now, actor)).ToList();
            MasterAuditLogs.AddRange(auditRows);
            await base.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            foreach (var stale in ChangeTracker.Entries<MasterAuditLog>()
                                              .Where(e => e.State == EntityState.Added)
                                              .ToList())
            {
                stale.State = EntityState.Detached;
            }

            var logger = this.GetService<ILoggerFactory>()?.CreateLogger("Enki.Audit.Master");
            logger?.LogWarning(ex,
                "Failed to write {Count} MasterAuditLog rows; underlying mutation " +
                "succeeded but audit is missing for this batch.",
                pending.Count);
        }

        return result;
    }

    private sealed record PendingAudit(
        EntityEntry<IAuditable> Entry,
        string Action,
        string? PreCapturedEntityId,
        string? OldValues,
        string? ChangedColumns);

    private static MasterAuditLog BuildAuditRow(PendingAudit p, DateTimeOffset now, string actor)
    {
        var newValues = p.Action == "Deleted"
            ? null
            : SerializeProperties(p.Entry, current: true);

        return new MasterAuditLog
        {
            EntityType     = p.Entry.Metadata.ClrType.Name,
            EntityId       = p.PreCapturedEntityId ?? ReadEntityId(p.Entry),
            Action         = p.Action,
            OldValues      = p.OldValues,
            NewValues      = newValues,
            ChangedColumns = p.ChangedColumns,
            ChangedAt      = now,
            ChangedBy      = actor,
        };
    }

    private static string ReadEntityId(EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();
        return primaryKey is null
            ? "(unknown)"
            : string.Join("|", primaryKey.Properties.Select(p =>
                entry.Property(p.Name).CurrentValue?.ToString() ?? "(null)"));
    }

    private static string ReadChangedColumns(EntityEntry entry) =>
        string.Join("|", entry.Properties
            .Where(p => p.Metadata.Name != nameof(IAuditable.RowVersion))
            .Where(p => p.IsModified)
            .Select(p => p.Metadata.Name));

    private static string SerializeProperties(EntityEntry entry, bool current)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in entry.Properties.Where(p => p.Metadata.Name != nameof(IAuditable.RowVersion)))
        {
            dict[p.Metadata.Name] = current ? p.CurrentValue : p.OriginalValue;
        }
        return JsonSerializer.Serialize(dict);
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

            e.Property(x => x.RowVersion).IsRowVersion();

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

            // Role column retired 2026-05-01 — see TenantUser comments.
            // The drop migration is RemoveTenantUserRole.

            e.HasOne(x => x.Tenant)
             .WithMany(t => t.Users)
             .HasForeignKey(x => x.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.User)
             .WithMany(u => u.Tenants)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // IAuditable — populated by SaveChangesAsync override. Adding
            // RowVersion retired the deferred tech-debt note that used to
            // sit on TenantMembersController; SetRole now uses the same
            // 409-on-conflict pattern as every other PUT/PATCH.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
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

    private static void ConfigureSystemSetting(ModelBuilder b)
    {
        b.Entity<SystemSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired().HasMaxLength(120);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Value).IsRequired();

            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // Seed every known SystemSettingKey with the canonical default from
        // SystemSettingDefaults. Adding a new known key means: register it
        // in SystemSettingKeys, add the default in SystemSettingDefaults,
        // and add a SeedSetting line here with a fresh stable id.
        var systemSettingSeedDate = new DateTimeOffset(2026, 4, 24, 0, 0, 0, TimeSpan.Zero);

        SeedSetting(b,  1, SystemSettingKeys.JobRegionSuggestions,                 systemSettingSeedDate);
        SeedSetting(b,  2, SystemSettingKeys.CalibrationDefaultGTotal,             systemSettingSeedDate);
        SeedSetting(b,  3, SystemSettingKeys.CalibrationDefaultBTotal,             systemSettingSeedDate);
        SeedSetting(b,  4, SystemSettingKeys.CalibrationDefaultDipDegrees,         systemSettingSeedDate);
        SeedSetting(b,  5, SystemSettingKeys.CalibrationDefaultDeclinationDegrees, systemSettingSeedDate);
        SeedSetting(b,  6, SystemSettingKeys.CalibrationDefaultCoilConstant,       systemSettingSeedDate);
        SeedSetting(b,  7, SystemSettingKeys.CalibrationDefaultActiveBDipDegrees,  systemSettingSeedDate);
        SeedSetting(b,  8, SystemSettingKeys.CalibrationDefaultSampleRateHz,       systemSettingSeedDate);
        SeedSetting(b,  9, SystemSettingKeys.CalibrationDefaultManualSign,         systemSettingSeedDate);
        SeedSetting(b, 10, SystemSettingKeys.CalibrationDefaultCurrent,            systemSettingSeedDate);
        SeedSetting(b, 11, SystemSettingKeys.CalibrationDefaultMagSource,          systemSettingSeedDate);
        SeedSetting(b, 12, SystemSettingKeys.CalibrationDefaultIncludeDeclination, systemSettingSeedDate);
    }

    private static void SeedSetting(ModelBuilder b, int id, string key, DateTimeOffset stamp) =>
        b.Entity<SystemSetting>().HasData(new
        {
            Id        = id,
            Key       = key,
            Value     = SystemSettingDefaults.Get(key),
            CreatedAt = stamp,
            CreatedBy = "system",
        });

    private static void ConfigureTool(ModelBuilder b)
    {
        b.Entity<Tool>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SerialNumber).IsUnique();
            e.Property(x => x.FirmwareVersion).IsRequired().HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(1000);

            e.Property(x => x.Generation).HasConversion(
                v => v.Value,
                v => ToolGeneration.FromValue(v));
            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => ToolStatus.FromValue(v));

            // Retirement metadata. Disposition is nullable — Active tools
            // don't carry one, so the converter has to handle null on both
            // sides. RetirementReason matches the DTO's MaxLength(500);
            // RetirementLocation is shorter (200) because it's a place name.
            e.Property(x => x.RetiredBy).HasMaxLength(100);
            e.Property(x => x.RetirementReason).HasMaxLength(500);
            e.Property(x => x.RetirementLocation).HasMaxLength(200);
            e.Property(x => x.Disposition).HasConversion(
                v => v == null ? (int?)null : v.Value,
                v => v == null ? null : ToolDisposition.FromValue(v.Value));

            // Restrict, not SetNull. SQL Server rejects SetNull here because
            // Tool already participates in a cascade-delete chain (Calibration
            // → Tool), and a self-referential SET-NULL would form a second
            // cascade path. Restrict (NO ACTION on SQL Server) means deleting
            // a tool that's a replacement target will fail with a FK error —
            // fine in practice because Tool has no Delete endpoint; the audit
            // row is preserved, which is what we want anyway.
            e.HasOne(x => x.ReplacementTool)
             .WithMany()
             .HasForeignKey(x => x.ReplacementToolId)
             .OnDelete(DeleteBehavior.Restrict);

            // Audit fields (IAuditable) — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Generation);
            e.HasIndex(x => x.Disposition);
        });
    }

    private static void ConfigureCalibration(ModelBuilder b)
    {
        b.Entity<Calibration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadJson).IsRequired();
            e.Property(x => x.CalibratedBy).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(1000);

            e.Property(x => x.Source).HasConversion(
                v => v.Value,
                v => CalibrationSource.FromValue(v));

            // Audit fields (IAuditable) — populated by SaveChangesAsync override.
            // Calibrations are append-only in normal use, but the audit columns
            // exist so a Notes/Source amendment is still tracked if it happens.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            e.HasOne(x => x.Tool)
             .WithMany(t => t.Calibrations)
             .HasForeignKey(x => x.ToolId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ToolId);
            e.HasIndex(x => x.SerialNumber);
            // Listing the latest cal per tool is the hot path; index the
            // (ToolId, IsSuperseded) pair so the "current cal" queries don't
            // scan the full table for tools with long calibration histories.
            e.HasIndex(x => new { x.ToolId, x.IsSuperseded });
        });
    }

    private static void ConfigureLicense(ModelBuilder b)
    {
        b.Entity<License>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.LicenseKey).IsUnique();
            e.Property(x => x.Licensee).IsRequired().HasMaxLength(200);
            e.Property(x => x.RevokedReason).HasMaxLength(500);

            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => LicenseStatus.FromValue(v));

            // FeaturesJson / ToolSnapshotJson / CalibrationSnapshotJson +
            // FileBytes default to nvarchar(max) / varbinary(max), which is
            // what we want — no explicit max-length cap. The audit-aid
            // snapshots can run several MB if the operator picks every
            // tool + calibration in the fleet; varbinary(max) is fine.

            // Audit fields (IAuditable) — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ExpiresAt);
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

    /// <summary>
    /// Append-only master change-history table. Same shape + index set
    /// as the tenant-side <c>AuditLog</c>: entity-scoped lookup index
    /// (EntityType, EntityId) for "show me all changes to Tenant X" and
    /// time-range index on ChangedAt for tenant-wide feeds.
    /// </summary>
    private static void ConfigureMasterAuditLog(ModelBuilder b)
    {
        b.Entity<MasterAuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
            e.Property(x => x.EntityId).IsRequired().HasMaxLength(100);
            e.Property(x => x.Action).IsRequired().HasMaxLength(20);
            e.Property(x => x.ChangedBy).IsRequired().HasMaxLength(100);
            e.Property(x => x.ChangedColumns).HasMaxLength(2000);

            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ChangedAt);
        });
    }
}

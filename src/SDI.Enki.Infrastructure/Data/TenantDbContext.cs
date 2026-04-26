using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Comments;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Logging;
using SDI.Enki.Core.Units;
using SDI.Enki.Core.TenantDb.Models;
using SDI.Enki.Core.TenantDb.Operators;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using Calibration = SDI.Enki.Core.TenantDb.Shots.Calibration;

namespace SDI.Enki.Infrastructure.Data;

/// <summary>
/// Per-tenant database context. One instance per tenant database (Active or
/// Archive). The same schema is deployed to every tenant DB via EF migrations;
/// the connection string is what differs. Resolution happens through
/// <c>ITenantDbContextFactory</c> at the WebApi layer — services never
/// construct a raw TenantDbContext.
///
/// <para>
/// <b>Audit:</b> entities implementing <see cref="IAuditable"/> get their
/// <c>CreatedAt</c> / <c>CreatedBy</c> / <c>UpdatedAt</c> / <c>UpdatedBy</c>
/// fields stamped automatically by the <see cref="SaveChangesAsync"/>
/// override. Mirrors the pattern on <c>EnkiMasterDbContext</c>.
/// <see cref="ICurrentUser"/> is optional so design-time tooling, the
/// Migrator CLI, and the provisioning service (none of which have a
/// principal) can still construct + write through the context — null
/// resolves to <c>"system"</c> for the audit actor.
/// </para>
/// </summary>
public class TenantDbContext : DbContext
{
    private readonly ICurrentUser? _currentUser;

    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options) { }

    public TenantDbContext(
        DbContextOptions<TenantDbContext> options,
        ICurrentUser? currentUser) : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobUser> JobUsers => Set<JobUser>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<Well> Wells => Set<Well>();
    public DbSet<TieOn> TieOns => Set<TieOn>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<Tubular> Tubulars => Set<Tubular>();
    public DbSet<Formation> Formations => Set<Formation>();
    public DbSet<CommonMeasure> CommonMeasures => Set<CommonMeasure>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Magnetics> Magnetics => Set<Magnetics>();
    public DbSet<Calibration> Calibrations => Set<Calibration>();
    public DbSet<Gradient> Gradients => Set<Gradient>();
    public DbSet<Rotary> Rotaries => Set<Rotary>();
    public DbSet<Passive> Passives => Set<Passive>();
    public DbSet<Shot> Shots => Set<Shot>();
    public DbSet<GyroShot> GyroShots => Set<GyroShot>();
    public DbSet<ToolSurvey> ToolSurveys => Set<ToolSurvey>();
    public DbSet<ActiveField> ActiveFields => Set<ActiveField>();
    public DbSet<GradientSolution> GradientSolutions => Set<GradientSolution>();
    public DbSet<RotarySolution> RotarySolutions => Set<RotarySolution>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ReferencedJob> ReferencedJobs => Set<ReferencedJob>();
    public DbSet<GradientFile> GradientFiles => Set<GradientFile>();
    public DbSet<RotaryFile> RotaryFiles => Set<RotaryFile>();
    public DbSet<PassiveFile> PassiveFiles => Set<PassiveFile>();

    public DbSet<Logging> Loggings => Set<Logging>();
    public DbSet<LoggingSetting> LoggingSettings => Set<LoggingSetting>();
    public DbSet<LoggingFile> LoggingFiles => Set<LoggingFile>();
    public DbSet<Log> Logs => Set<Log>();
    public DbSet<LoggingTimeDepth> LoggingTimeDepths => Set<LoggingTimeDepth>();
    public DbSet<LogTimeDepth> LogTimeDepths => Set<LogTimeDepth>();
    public DbSet<LoggingEfd> LoggingEfd => Set<LoggingEfd>();
    public DbSet<LoggingProcessing> LoggingProcessing => Set<LoggingProcessing>();
    public DbSet<RotaryProcessing> RotaryProcessing => Set<RotaryProcessing>();
    public DbSet<PassiveLoggingProcessing> PassiveLoggingProcessing => Set<PassiveLoggingProcessing>();

    public DbSet<GradientModel> GradientModels => Set<GradientModel>();
    public DbSet<RotaryModel> RotaryModels => Set<RotaryModel>();
    public DbSet<SavedGradientModel> SavedGradientModels => Set<SavedGradientModel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureJob(builder);
        ConfigureJobUser(builder);
        ConfigureRun(builder);
        ConfigureWell(builder);
        ConfigureTieOn(builder);
        ConfigureSurvey(builder);
        ConfigureTubular(builder);
        ConfigureFormation(builder);
        ConfigureCommonMeasure(builder);
        ConfigureOperator(builder);
        ConfigureMagnetics(builder);
        ConfigureCalibration(builder);
        ConfigureGradient(builder);
        ConfigureRotary(builder);
        ConfigurePassive(builder);
        ConfigureShot(builder);
        ConfigureGyroShot(builder);
        ConfigureToolSurvey(builder);
        ConfigureActiveField(builder);
        ConfigureGradientSolution(builder);
        ConfigureRotarySolution(builder);
        ConfigureComment(builder);
        ConfigureReferencedJob(builder);
        ConfigureFiles(builder);
        ConfigureLoggingFamily(builder);
        ConfigureModels(builder);

        ApplySingularTableNames(builder);
    }

    /// <summary>
    /// Stamps <see cref="IAuditable"/> properties on every insert /
    /// update. Mirrors <c>EnkiMasterDbContext.SaveChangesAsync</c> —
    /// same rules, same immutability guard on <c>CreatedAt</c> /
    /// <c>CreatedBy</c>. <see cref="IAuditable.RowVersion"/> is left to
    /// EF's native concurrency handling via <c>IsRowVersion</c> in
    /// per-entity config.
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
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= actor;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// See <c>EnkiMasterDbContext.ApplySingularTableNames</c> for rationale —
    /// same implementation, same reasons.
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

    private static void ConfigureJob(ModelBuilder b)
    {
        b.Entity<Job>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.Property(x => x.WellName).HasMaxLength(100);
            e.Property(x => x.Region).HasMaxLength(64);
            e.Property(x => x.Description).IsRequired().HasMaxLength(200);

            e.Property(x => x.UnitSystem).HasConversion(
                v => v.Value,
                v => UnitSystem.FromValue(v));
            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => JobStatus.FromValue(v));

            // Audit fields (IAuditable) — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            // Index on Status for the common "list active jobs" filter,
            // and on Region for region-scoped reporting queries.
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Region);
        });
    }

    private static void ConfigureJobUser(ModelBuilder b)
    {
        b.Entity<JobUser>(e =>
        {
            // Composite PK (JobId, UserId)
            e.HasKey(x => new { x.JobId, x.UserId });

            // Tenant-side FK to Job; UserId has NO SQL FK (master-DB Guid).
            e.HasOne(x => x.Job)
             .WithMany(j => j.Users)
             .HasForeignKey(x => x.JobId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.UserId);
        });
    }

    private static void ConfigureRun(ModelBuilder b)
    {
        b.Entity<Run>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.Property(x => x.Description).IsRequired().HasMaxLength(200);

            e.Property(x => x.Type).HasConversion(
                v => v.Value,
                v => RunType.FromValue(v));
            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => RunStatus.FromValue(v));

            // Audit fields (IAuditable) — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            e.HasOne(x => x.Job)
             .WithMany(j => j.Runs)
             .HasForeignKey(x => x.JobId)
             .OnDelete(DeleteBehavior.Cascade);

            // Run ↔ Operator: auto-skip-navigation many-to-many (pure junction).
            // Legacy had three parallel junctions (GradientRunOperator, RotaryRunOperator,
            // OperatorPassiveRun); we collapse them because Run is unified.
            e.HasMany(x => x.Operators)
             .WithMany(o => o.Runs)
             .UsingEntity(j => j.ToTable("RunOperator"));

            e.HasIndex(x => x.JobId);
            e.HasIndex(x => x.Type);
        });
    }

    private static void ConfigureWell(ModelBuilder b)
    {
        b.Entity<Well>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.Property(x => x.Type).HasConversion(
                v => v.Value,
                v => WellType.FromValue(v));

            // Wells belong to a Job — required FK with cascade so
            // deleting a Job nukes its Wells (and via their child
            // configs, every Survey/TieOn/Tubular/Formation/CommonMeasure
            // under them too).
            e.HasOne(x => x.Job)
             .WithMany(j => j.Wells)
             .HasForeignKey(x => x.JobId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.JobId);

            // IAuditable — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureTieOn(ModelBuilder b)
    {
        b.Entity<TieOn>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Well)
             .WithMany(w => w.TieOns)
             .HasForeignKey(x => x.WellId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.WellId);

            // IAuditable.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureSurvey(ModelBuilder b)
    {
        b.Entity<Survey>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Well)
             .WithMany(w => w.Surveys)
             .HasForeignKey(x => x.WellId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.WellId);
            e.HasIndex(x => new { x.WellId, x.Depth });

            // IAuditable.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureTubular(ModelBuilder b)
    {
        b.Entity<Tubular>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);

            e.Property(x => x.Type).HasConversion(
                v => v.Value,
                v => TubularType.FromValue(v));

            e.HasOne(x => x.Well)
             .WithMany(w => w.Tubulars)
             .HasForeignKey(x => x.WellId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.WellId);
            e.HasIndex(x => new { x.WellId, x.Order });

            // IAuditable.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureFormation(ModelBuilder b)
    {
        b.Entity<Formation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.HasOne(x => x.Well)
             .WithMany(w => w.Formations)
             .HasForeignKey(x => x.WellId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.WellId);

            // IAuditable.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureCommonMeasure(ModelBuilder b)
    {
        b.Entity<CommonMeasure>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Well)
             .WithMany(w => w.CommonMeasures)
             .HasForeignKey(x => x.WellId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.WellId);

            // IAuditable.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureOperator(ModelBuilder b)
    {
        b.Entity<Operator>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.Name);
        });
    }

    private static void ConfigureMagnetics(ModelBuilder b)
    {
        b.Entity<Magnetics>(e =>
        {
            e.HasKey(x => x.Id);

            // UNIQUE on the natural key for the legacy per-shot lookup
            // pattern — replaces the legacy trg_ValidateMagnetics
            // AFTER-INSERT trigger. Writers for per-shot lookup rows
            // go through IEntityLookup.FindOrCreateAsync. Filtered to
            // WellId IS NULL so per-well curated rows (which may
            // duplicate any triple) don't collide with the lookup
            // pool.
            e.HasIndex(x => new { x.BTotal, x.Dip, x.Declination })
                .IsUnique()
                .HasFilter("[WellId] IS NULL");

            // 1:0..1 — at most one per-well Magnetics row per Well.
            // Filtered unique index lets WellId be null for the
            // per-shot lookup rows above. Cascade on delete: if the
            // Well goes, its Magnetics row goes too; per-shot lookup
            // rows survive because they have no WellId.
            e.HasIndex(x => x.WellId)
                .IsUnique()
                .HasFilter("[WellId] IS NOT NULL");

            e.HasOne(x => x.Well)
                .WithOne(w => w.Magnetics)
                .HasForeignKey<Magnetics>(x => x.WellId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCalibration(ModelBuilder b)
    {
        b.Entity<Calibration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            // UNIQUE on natural key. Cap CalibrationString at 4000 chars
            // because NVARCHAR(MAX) can't participate in a B-tree index;
            // the underlying column stays effectively unlimited — this is
            // just the indexable projection.
            e.Property(x => x.CalibrationString).IsRequired().HasMaxLength(4000);
            e.HasIndex(x => new { x.Name, x.CalibrationString }).IsUnique();
        });
    }

    private static void ConfigureGradient(ModelBuilder b)
    {
        b.Entity<Gradient>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.HasOne(x => x.Run)
             .WithMany(r => r.Gradients)
             .HasForeignKey(x => x.RunId)
             .OnDelete(DeleteBehavior.Cascade);

            // Self-referencing hierarchy
            e.HasOne(x => x.Parent)
             .WithMany(x => x.Children)
             .HasForeignKey(x => x.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => new { x.RunId, x.Order });
        });
    }

    private static void ConfigureRotary(ModelBuilder b)
    {
        b.Entity<Rotary>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.HasOne(x => x.Run)
             .WithMany(r => r.Rotaries)
             .HasForeignKey(x => x.RunId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Parent)
             .WithMany(x => x.Children)
             .HasForeignKey(x => x.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => new { x.RunId, x.Order });
        });
    }

    private static void ConfigurePassive(ModelBuilder b)
    {
        b.Entity<Passive>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.HasOne(x => x.Run)
             .WithMany(r => r.Passives)
             .HasForeignKey(x => x.RunId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.RunId);
            e.HasIndex(x => new { x.RunId, x.Order });
        });
    }

    private static void ConfigureShot(ModelBuilder b)
    {
        b.Entity<Shot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShotName).IsRequired().HasMaxLength(200);

            // Optional parent FKs — exactly one non-null enforced by CHECK
            // constraint below. Restrict rather than cascade: deleting a
            // Gradient/Rotary parent should require explicit shot cleanup.
            e.HasOne(x => x.Gradient)
             .WithMany(g => g.Shots)
             .HasForeignKey(x => x.GradientId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Rotary)
             .WithMany(r => r.Shots)
             .HasForeignKey(x => x.RotaryId)
             .OnDelete(DeleteBehavior.Restrict);

            // Magnetics + Calibration are nullable-FK lookups managed via
            // IEntityLookup.FindOrCreateAsync at write time. No cascade:
            // deleting a lookup row would orphan shots.
            e.HasOne(x => x.Magnetics)
             .WithMany()
             .HasForeignKey(x => x.MagneticsId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Calibration)
             .WithMany()
             .HasForeignKey(x => x.CalibrationsId)
             .OnDelete(DeleteBehavior.Restrict);

            // CHECK: exactly one of GradientId / RotaryId is non-null.
            // Enforces the "a Shot belongs to either a Gradient or a Rotary,
            // never both and never neither" invariant at the DB layer.
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Shots_ExactlyOneParent",
                "([GradientId] IS NULL AND [RotaryId] IS NOT NULL) OR ([GradientId] IS NOT NULL AND [RotaryId] IS NULL)"));

            e.HasIndex(x => x.GradientId);
            e.HasIndex(x => x.RotaryId);
            e.HasIndex(x => x.MagneticsId);
            e.HasIndex(x => x.CalibrationsId);
        });
    }

    private static void ConfigureGyroShot(ModelBuilder b)
    {
        b.Entity<GyroShot>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Shot)
             .WithMany(s => s.GyroShots)
             .HasForeignKey(x => x.ShotId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ShotId);
        });
    }

    private static void ConfigureToolSurvey(ModelBuilder b)
    {
        b.Entity<ToolSurvey>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Shot)
             .WithMany(s => s.ToolSurveys)
             .HasForeignKey(x => x.ShotId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ShotId);
        });
    }

    private static void ConfigureActiveField(ModelBuilder b)
    {
        b.Entity<ActiveField>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Shot)
             .WithMany(s => s.ActiveFields)
             .HasForeignKey(x => x.ShotId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ShotId);
        });
    }

    private static void ConfigureGradientSolution(ModelBuilder b)
    {
        b.Entity<GradientSolution>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Gradient)
             .WithMany(g => g.Solutions)
             .HasForeignKey(x => x.GradientId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.GradientId);
        });
    }

    private static void ConfigureRotarySolution(ModelBuilder b)
    {
        b.Entity<RotarySolution>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Rotary)
             .WithMany(r => r.Solutions)
             .HasForeignKey(x => x.RotaryId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.RotaryId);
        });
    }

    private static void ConfigureComment(ModelBuilder b)
    {
        b.Entity<Comment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).IsRequired();
            e.Property(x => x.User).IsRequired().HasMaxLength(200);

            // Three many-to-many junctions, one unified Comments table.
            // Junction table names preserved from legacy.
            e.HasMany(x => x.Gradients)
             .WithMany(g => g.Comments)
             .UsingEntity(j => j.ToTable("GradientComment"));

            e.HasMany(x => x.Rotaries)
             .WithMany(r => r.Comments)
             .UsingEntity(j => j.ToTable("RotaryComment"));

            e.HasMany(x => x.Passives)
             .WithMany(p => p.Comments)
             .UsingEntity(j => j.ToTable("PassiveComment"));
        });
    }

    private static void ConfigureReferencedJob(ModelBuilder b)
    {
        b.Entity<ReferencedJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Purpose).HasMaxLength(400);

            e.HasOne(x => x.Job)
             .WithMany(j => j.References)
             .HasForeignKey(x => x.JobId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.JobId);
            // Composite index on referenced keys for "who references this job?" lookups.
            e.HasIndex(x => new { x.ReferencedTenantId, x.ReferencedJobId });
        });
    }

    private static void ConfigureFiles(ModelBuilder b)
    {
        b.Entity<GradientFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.HasOne(x => x.Gradient)
             .WithMany(g => g.Files)
             .HasForeignKey(x => x.GradientId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.GradientId);
        });

        b.Entity<RotaryFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.HasOne(x => x.Rotary)
             .WithMany(r => r.Files)
             .HasForeignKey(x => x.RotaryId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.RotaryId);
        });

        b.Entity<PassiveFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.HasOne(x => x.Passive)
             .WithMany(p => p.Files)
             .HasForeignKey(x => x.PassiveId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.PassiveId);
        });
    }

    private static void ConfigureLoggingFamily(ModelBuilder b)
    {
        // ---- LoggingSetting (unified from 3 identical legacy tables) ----
        b.Entity<LoggingSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("LoggingSettings");
        });

        // ---- Logging (unified — the heart of the family) ----
        b.Entity<Logging>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("Loggings");
            e.Property(x => x.ShotName).IsRequired().HasMaxLength(200);

            // Three nullable run FKs — exactly one non-null enforced by CHECK.
            // DeleteBehavior.NoAction on all three to break the multiple-cascade-paths
            // EF would otherwise complain about (Run cascade to Logging + Shot cascade
            // from Run would collide). Parent deletion via app-layer orchestration.
            e.HasOne(x => x.GradientRun)
             .WithMany().HasForeignKey(x => x.GradientRunId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.RotaryRun)
             .WithMany().HasForeignKey(x => x.RotaryRunId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.PassiveRun)
             .WithMany().HasForeignKey(x => x.PassiveRunId)
             .OnDelete(DeleteBehavior.NoAction);

            // Lookup FKs (Restrict — don't orphan Loggings by deleting a lookup row).
            e.HasOne(x => x.Calibration)
             .WithMany().HasForeignKey(x => x.CalibrationId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Magnetics)
             .WithMany().HasForeignKey(x => x.MagneticId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LoggingSetting)
             .WithMany().HasForeignKey(x => x.LogSettingId)
             .OnDelete(DeleteBehavior.Restrict);

            // CHECK: exactly one run FK non-null.
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Loggings_ExactlyOneRun",
                "(CASE WHEN [GradientRunId] IS NULL THEN 0 ELSE 1 END) + " +
                "(CASE WHEN [RotaryRunId]   IS NULL THEN 0 ELSE 1 END) + " +
                "(CASE WHEN [PassiveRunId]  IS NULL THEN 0 ELSE 1 END) = 1"));

            e.HasIndex(x => x.GradientRunId);
            e.HasIndex(x => x.RotaryRunId);
            e.HasIndex(x => x.PassiveRunId);
            e.HasIndex(x => x.CalibrationId);
            e.HasIndex(x => x.MagneticId);
            e.HasIndex(x => x.LogSettingId);
        });

        // ---- LoggingFile ----
        b.Entity<LoggingFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("LoggingFiles");
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.HasOne(x => x.Logging).WithMany(l => l.Files)
             .HasForeignKey(x => x.LoggingId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingId);
        });

        // ---- Log ----
        b.Entity<Log>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("Logs");
            e.HasOne(x => x.Logging).WithMany(l => l.Logs)
             .HasForeignKey(x => x.LoggingId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingId);
            e.HasIndex(x => new { x.LoggingId, x.Depth });
        });

        // ---- LoggingTimeDepth ----
        b.Entity<LoggingTimeDepth>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("LoggingTimeDepth");   // legacy singular
            e.Property(x => x.ShotName).IsRequired().HasMaxLength(200);
            e.HasOne(x => x.Logging).WithMany(l => l.TimeDepths)
             .HasForeignKey(x => x.LoggingId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingId);
        });

        // ---- LogTimeDepth ----
        b.Entity<LogTimeDepth>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("LogTimeDepth");   // legacy singular
            e.HasOne(x => x.LoggingTimeDepth).WithMany(h => h.Samples)
             .HasForeignKey(x => x.LoggingTimeDepthId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingTimeDepthId);
        });

        // ---- LoggingEfd ----
        b.Entity<LoggingEfd>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("LoggingEfd");   // legacy singular
            e.HasOne(x => x.Logging).WithMany(l => l.EfdSamples)
             .HasForeignKey(x => x.LoggingId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingId);
        });

        // ---- LoggingProcessing (1:1 with Logging; FK INVERTED from legacy) ----
        b.Entity<LoggingProcessing>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("LoggingProcessing");
            e.HasOne(x => x.Logging).WithOne(l => l.LoggingProcessing)
             .HasForeignKey<LoggingProcessing>(x => x.LoggingId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingId).IsUnique();
        });

        b.Entity<RotaryProcessing>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("RotaryProcessing");
            e.HasOne(x => x.Logging).WithOne(l => l.RotaryProcessing)
             .HasForeignKey<RotaryProcessing>(x => x.LoggingId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingId).IsUnique();
        });

        b.Entity<PassiveLoggingProcessing>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("PassiveLoggingProcessing");
            e.HasOne(x => x.Logging).WithOne(l => l.PassiveLoggingProcessing)
             .HasForeignKey<PassiveLoggingProcessing>(x => x.LoggingId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.LoggingId).IsUnique();
        });
    }

    private static void ConfigureModels(ModelBuilder b)
    {
        b.Entity<GradientModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            // Two explicit FKs to Well (target + injection). Restrict — a
            // modelling scenario references Wells; deleting a Well requires
            // explicit cleanup of the models referencing it.
            e.HasOne(x => x.TargetWell)
             .WithMany()
             .HasForeignKey(x => x.TargetWellId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InjectionWell)
             .WithMany()
             .HasForeignKey(x => x.InjectionWellId)
             .OnDelete(DeleteBehavior.Restrict);

            // Many-to-many GradientModel ↔ Run. Junction table preserves
            // legacy-ish name (GradientModelRun — singular, matches the
            // other tenant-DB pluralisation patterns).
            e.HasMany(x => x.Runs)
             .WithMany(r => r.GradientModels)
             .UsingEntity(j => j.ToTable("GradientModelRun"));

            e.HasIndex(x => x.TargetWellId);
            e.HasIndex(x => x.InjectionWellId);
        });

        b.Entity<RotaryModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.HasOne(x => x.TargetWell)
             .WithMany()
             .HasForeignKey(x => x.TargetWellId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InjectionWell)
             .WithMany()
             .HasForeignKey(x => x.InjectionWellId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Runs)
             .WithMany(r => r.RotaryModels)
             .UsingEntity(j => j.ToTable("RotaryModelRun"));

            e.HasIndex(x => x.TargetWellId);
            e.HasIndex(x => x.InjectionWellId);
        });

        b.Entity<SavedGradientModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Json).IsRequired();

            e.HasOne(x => x.GradientModel)
             .WithMany(m => m.SavedSnapshots)
             .HasForeignKey(x => x.GradientModelId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.GradientModelId);
            e.HasIndex(x => x.CreationTime);
        });
    }
}

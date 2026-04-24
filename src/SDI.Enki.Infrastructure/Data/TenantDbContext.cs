using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
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
/// Phase-1b subset: Job, Run, Well, TieOn, Survey, plus the JobUser junction
/// into master-DB Users. Shots, Loggings, Solutions, etc. land in Phase 1c+.
/// </summary>
public class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
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
    }

    private static void ConfigureJob(ModelBuilder b)
    {
        b.Entity<Job>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.Property(x => x.WellName).HasMaxLength(100);
            e.Property(x => x.Description).IsRequired().HasMaxLength(200);

            e.Property(x => x.Units).HasConversion(
                v => v.Value,
                v => Units.FromValue(v));
            e.Property(x => x.Status).HasConversion(
                v => v.Value,
                v => JobStatus.FromValue(v));
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

            // UNIQUE on the natural key — replaces the legacy
            // trg_ValidateMagnetics AFTER-INSERT trigger. Writers go through
            // IEntityLookup.FindOrCreateAsync.
            e.HasIndex(x => new { x.BTotal, x.Dip, x.Declination }).IsUnique();
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
}

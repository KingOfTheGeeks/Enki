using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Operators;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;

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
}

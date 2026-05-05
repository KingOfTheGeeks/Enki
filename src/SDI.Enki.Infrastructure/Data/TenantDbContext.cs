using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Audit;
using SDI.Enki.Core.TenantDb.Comments;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Logs;
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

    // Phase 2 reshape: Shot stays as the single per-capture entity
    // under a Run (Gradient/Rotary types). Each Shot carries its own
    // Binary + Config + Result + a paired Gyro set; legacy parent-
    // grouping tables (Gradient / Rotary / Passive) and per-sample
    // child tables (GyroShot / ToolSurvey / ActiveField /
    // GradientSolution / RotarySolution + the file-attachment trio)
    // are deleted. Passive runs carry their data on Run directly.
    public DbSet<Shot> Shots => Set<Shot>();

    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ReferencedJob> ReferencedJobs => Set<ReferencedJob>();

    // Logs family — Phase 2 reshape: Log + LogResultFile only. The
    // pre-Marduk artifact entities (LogSample / LogTimeWindow /
    // LogTimeWindowSample / LogEfdSample / LogProcessing × 3 /
    // LogSetting / LogFile) are deleted. Captured data is now a
    // varbinary on Log; processed output is one or more
    // LogResultFile rows.
    public DbSet<Log> Logs => Set<Log>();
    public DbSet<LogResultFile> LogResultFiles => Set<LogResultFile>();

    public DbSet<GradientModel> GradientModels => Set<GradientModel>();
    public DbSet<RotaryModel> RotaryModels => Set<RotaryModel>();
    public DbSet<SavedGradientModel> SavedGradientModels => Set<SavedGradientModel>();

    /// <summary>
    /// Append-only change-history table. Populated by
    /// <see cref="SaveChangesAsync"/> for every IAuditable mutation;
    /// no application code writes to this DbSet directly. Read API at
    /// <c>/tenants/{code}/audit</c>.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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
        ConfigureShot(builder);
        ConfigureComment(builder);
        ConfigureReferencedJob(builder);
        ConfigureLogFamily(builder);
        ConfigureModels(builder);
        ConfigureAuditLog(builder);

        ApplySingularTableNames(builder);
    }

    /// <summary>
    /// Stamps <see cref="IAuditable"/> properties on every insert /
    /// update <i>and</i> captures a parallel <see cref="AuditLog"/> row
    /// for every IAuditable insert / update / delete. Mirrors the
    /// stamping rules from <c>EnkiMasterDbContext.SaveChangesAsync</c>;
    /// the audit-log capture is unique to the tenant context.
    ///
    /// <para>
    /// <b>Two-phase capture:</b> the audit row is built <i>after</i>
    /// <c>base.SaveChangesAsync</c> so int-IDENTITY primary keys
    /// (Survey/Tubular/Formation/CommonMeasure/TieOn/Magnetics/Log/Well
    /// — every tenant entity except Job/Run/Shot) have their generated
    /// id available; reading <c>CurrentValue</c> pre-save would yield
    /// 0 / a temp negative value and the audit table would lose its
    /// EntityId. Modified/Deleted entries snapshot their pre-state in
    /// phase 1 because <c>OriginalValues</c> are reset (Modified) / the
    /// entry is detached (Deleted) after save.
    /// </para>
    ///
    /// <para>
    /// <b>Atomicity trade-off:</b> the audit save runs as a separate
    /// SaveChanges on the same context. We deliberately do <i>not</i>
    /// wrap both in a user-initiated transaction — the SQL Server
    /// retry strategy (EnableRetryOnFailure on every Enki context)
    /// rejects user transactions because retry-with-tx requires
    /// <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c>,
    /// which doesn't compose cleanly with an interceptor (the lambda
    /// would re-execute on retry but our pending list was built
    /// before entry). Instead the audit save is best-effort: failure
    /// is logged + swallowed, matching the pattern used by
    /// <c>IAuthEventLogger</c> + <c>IAuthzDenialAuditor</c>. The
    /// underlying mutation either fully succeeded or fully failed
    /// (single SaveChanges still wraps an EF transaction); only the
    /// audit row is at-most-once.
    /// </para>
    ///
    /// <para>
    /// <b>RowVersion exclusion:</b> the concurrency token is
    /// operational metadata, not audit data, and would dump 8 bytes
    /// of base64 noise into every row. Property snapshots skip it.
    /// </para>
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = _currentUser?.UserId ?? "system";

        // Phase 1: stamp audit fields, snapshot pre-save state for
        // Modified/Deleted (gone after save), and queue a build hint
        // for each affected entry.
        var pending = new List<PendingAudit>();

        foreach (var entry in ChangeTracker.Entries<IAuditable>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= actor;
                    // EntityId + NewValues read post-save (IDENTITY key
                    // not generated yet).
                    pending.Add(new PendingAudit(entry, "Created",
                        PreCapturedEntityId: null,
                        OldValues: null,
                        ChangedColumns: null));
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
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

        // Phase 1b: persist the underlying mutation. If this throws,
        // we let it propagate — no audit row gets written for a save
        // that didn't happen.
        var result = await base.SaveChangesAsync(cancellationToken);

        if (pending.Count == 0)
            return result;

        // Phase 2: build audit rows now that IDENTITY keys are populated.
        // Best-effort save — failure here is logged + swallowed so the
        // caller's mutation isn't reported as failed when only the
        // observability log tail dropped.
        try
        {
            var auditRows = pending.Select(p => BuildAuditRow(p, now, actor)).ToList();
            AuditLogs.AddRange(auditRows);
            await base.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Detach the un-persisted audit rows so a subsequent
            // SaveChanges on the same context doesn't pick them up
            // and write them with stale state.
            foreach (var stale in ChangeTracker.Entries<AuditLog>()
                                              .Where(e => e.State == EntityState.Added)
                                              .ToList())
            {
                stale.State = EntityState.Detached;
            }

            var logger = this.GetService<ILoggerFactory>()?.CreateLogger("Enki.Audit.Tenant");
            logger?.LogWarning(ex,
                "Failed to write {Count} AuditLog rows for {ContextType}; underlying " +
                "mutation succeeded but audit is missing for this batch.",
                pending.Count, nameof(TenantDbContext));
        }

        return result;
    }

    /// <summary>
    /// Hint queued during phase 1 so phase 2 can finish constructing
    /// the audit row after <c>base.SaveChangesAsync</c> has run.
    /// </summary>
    private sealed record PendingAudit(
        EntityEntry<IAuditable> Entry,
        string Action,
        string? PreCapturedEntityId,
        string? OldValues,
        string? ChangedColumns);

    private static AuditLog BuildAuditRow(PendingAudit p, DateTimeOffset now, string actor)
    {
        var newValues = p.Action == "Deleted"
            ? null
            : SerializeProperties(p.Entry, current: true);

        return new AuditLog
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

    /// <summary>
    /// Composite-PK-aware EntityId reader. Reads <c>CurrentValue</c>;
    /// callers that need the pre-save value (Modified/Deleted) call
    /// this in phase 1 before SaveChanges runs.
    /// </summary>
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

            // ToolId — soft FK to master Tool (no SQL constraint; master
            // and tenant DBs are separate). Indexed for "runs using tool X"
            // queries. Validated at the application layer in RunsController.
            e.HasIndex(x => x.ToolId);

            // SnapshotCalibrationId — FK to tenant Calibration row holding
            // the run's snapshotted master Calibration payload. Restrict
            // on delete: removing a snapshot row that any Run still
            // references should be an explicit cleanup.
            e.HasOne(x => x.SnapshotCalibration)
             .WithMany()
             .HasForeignKey(x => x.SnapshotCalibrationId)
             .OnDelete(DeleteBehavior.Restrict);

            // MagneticsId — REQUIRED 1:1 to a tenant Magnetics row owned
            // by this Run. Cascade on delete: removing the run takes its
            // own magnetics row with it. Same Magnetics entity is also
            // used per-Well (with WellId set) and as the legacy per-shot
            // lookup (with WellId null + dedup index); the per-run usage
            // is a third shape — WellId null, exclusively pointed at by
            // exactly one Run.
            e.HasOne(x => x.Magnetics)
             .WithMany()
             .HasForeignKey(x => x.MagneticsId)
             .OnDelete(DeleteBehavior.Restrict);

            // Passive-only capture/calc fields. Populated only when
            // Type == Passive (Gradient/Rotary capture lives on Shot
            // children instead). All nullable; the future calc
            // service watches PassiveResultStatus = 'Pending'.
            e.Property(x => x.PassiveBinary).HasColumnType("varbinary(max)");
            e.Property(x => x.PassiveBinaryName).HasMaxLength(255);
            e.Property(x => x.PassiveConfigJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.PassiveResultJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.PassiveResultMardukVersion).HasMaxLength(50);
            e.Property(x => x.PassiveResultStatus).HasMaxLength(20);
            e.Property(x => x.PassiveResultError).HasColumnType("nvarchar(max)");
            // Indexed so the future calc service's "what's pending"
            // sweep is a seek not a scan.
            e.HasIndex(x => x.PassiveResultStatus);

            e.HasIndex(x => x.JobId);
            e.HasIndex(x => x.Type);

            // Soft-delete: queries see only ArchivedAt IS NULL by
            // default. RunsController.Delete sets the marker;
            // RunsController.Restore clears it; admin views that
            // need archived runs use IgnoreQueryFilters() on the
            // server side. Same shape as Well.ArchivedAt.
            e.HasIndex(x => x.ArchivedAt);
            e.HasQueryFilter(x => x.ArchivedAt == null);
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

            // Soft-delete: queries see only ArchivedAt IS NULL by
            // default. WellsController.Delete sets the marker;
            // WellsController.Restore clears it; admin views that
            // need archived wells use IgnoreQueryFilters() on the
            // server side. Indexed for the lifecycle endpoints.
            e.HasIndex(x => x.ArchivedAt);
            e.HasQueryFilter(x => x.ArchivedAt == null);
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

            // Magnetics now serves two shapes that share the table:
            //
            //   1. Per-well canonical reference — exactly one row per
            //      Well (WellId not null), enforced by the filtered
            //      unique index below.
            //   2. Per-Run owned magnetics — one row per Run (WellId
            //      null), pointed at by Run.MagneticsId. Two runs can
            //      legitimately carry identical (BTotal, Dip,
            //      Declination) values (same well, same operator-
            //      entered reference) so NO unique index across the
            //      triple — each run owns its own row.
            //
            // The earlier filtered unique index on (BTotal, Dip,
            // Declination) WHERE WellId IS NULL was for a legacy
            // per-shot lookup pattern (Shot/Log → Magnetics dedup);
            // the post-issue-#26 reshape replaced that with the per-
            // run snapshot model and Shot/Log no longer reference
            // Magnetics. Index dropped — without it the per-run rows
            // can coexist freely.
            e.HasIndex(x => x.WellId)
                .IsUnique()
                .HasFilter("[WellId] IS NOT NULL");

            e.HasOne(x => x.Well)
                .WithOne(w => w.Magnetics)
                .HasForeignKey<Magnetics>(x => x.WellId)
                .OnDelete(DeleteBehavior.Cascade);

            // Concurrency token. Without this the column is plain
            // varbinary(MAX), SQL Server never auto-populates it, the
            // GET projection returns null, and the per-well edit form's
            // round-trip RowVersion arrives at the controller as null —
            // ApplyClientRowVersion then 400s with "RowVersion is
            // required for optimistic concurrency." Every other
            // IAuditable entity has this; Magnetics was missed.
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    private static void ConfigureCalibration(ModelBuilder b)
    {
        b.Entity<Calibration>(e =>
        {
            e.HasKey(x => x.Id);

            // Soft FKs to master Tool / Calibration. No SQL constraint
            // (cross-DB), so the validation lives in
            // CalibrationSnapshotService at insert time. Indexed so a
            // tenant query for "snapshots that exist for this tool" or
            // "snapshot of this specific master cal" is a seek.
            e.Property(x => x.MasterCalibrationId).IsRequired();
            e.Property(x => x.ToolId).IsRequired();
            e.HasIndex(x => x.ToolId);
            e.HasIndex(x => x.MasterCalibrationId).IsUnique();   // one snapshot row per master cal per tenant

            e.Property(x => x.SerialNumber).IsRequired();
            e.Property(x => x.CalibrationDate).IsRequired();
            e.Property(x => x.CalibratedBy).HasMaxLength(200);
            e.Property(x => x.MagnetometerCount).IsRequired();
            e.Property(x => x.IsNominal).IsRequired();

            // PayloadJson is the verbatim Marduk ToolCalibration JSON
            // copied from the master row. nvarchar(max) — payloads run
            // a few KB and the column is opaque to query.
            e.Property(x => x.PayloadJson).IsRequired().HasColumnType("nvarchar(max)");

            // IAuditable — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }

    /// <summary>
    /// Phase 2 reshape: Shot is the single per-capture entity under
    /// a Run. Two parallel (binary + config + result + status) sets
    /// — primary and gyro — directly on the row. The legacy
    /// Gradient/Rotary parent-grouping FKs + CHECK constraint are
    /// gone; Shot belongs to exactly one Run via <c>RunId</c>.
    /// </summary>
    private static void ConfigureShot(ModelBuilder b)
    {
        b.Entity<Shot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShotName).IsRequired().HasMaxLength(200);

            // Parent Run — required, cascade so dropping a Run takes
            // its Shots with it.
            e.HasOne(x => x.Run)
             .WithMany(r => r.Shots)
             .HasForeignKey(x => x.RunId)
             .OnDelete(DeleteBehavior.Cascade);

            // Calibration — optional FK; required at calc time but a
            // Shot can be created before the calibration is selected.
            // Restrict on delete: removing a Calibration that any
            // Shot references should be an explicit cleanup.
            e.HasOne(x => x.Calibration)
             .WithMany()
             .HasForeignKey(x => x.CalibrationId)
             .OnDelete(DeleteBehavior.Restrict);

            // Audit (IAuditable) — populated by SaveChangesAsync override.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            // ---- primary capture columns ----
            e.Property(x => x.Binary).HasColumnType("varbinary(max)");
            e.Property(x => x.BinaryName).HasMaxLength(255);
            e.Property(x => x.ConfigJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ResultMardukVersion).HasMaxLength(50);
            e.Property(x => x.ResultStatus).HasMaxLength(20);
            e.Property(x => x.ResultError).HasColumnType("nvarchar(max)");

            // ---- gyro capture columns ----
            e.Property(x => x.GyroBinary).HasColumnType("varbinary(max)");
            e.Property(x => x.GyroBinaryName).HasMaxLength(255);
            e.Property(x => x.GyroConfigJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.GyroResultJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.GyroResultMardukVersion).HasMaxLength(50);
            e.Property(x => x.GyroResultStatus).HasMaxLength(20);
            e.Property(x => x.GyroResultError).HasColumnType("nvarchar(max)");

            // Indexed for the calc service's pending-work sweeps and
            // the Run → Shots fan-out lookup.
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.CalibrationId);
            e.HasIndex(x => x.ResultStatus);
            e.HasIndex(x => x.GyroResultStatus);
        });
    }

    /// <summary>
    /// Comments reparent to Shot in Phase 2 (was m:n with
    /// Gradient/Rotary/Passive in the legacy shape; those parents
    /// are deleted). 1:N — each Comment belongs to exactly one
    /// Shot. Cascade on delete: removing a Shot takes its comments
    /// with it.
    /// </summary>
    private static void ConfigureComment(ModelBuilder b)
    {
        b.Entity<Comment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).IsRequired();
            e.Property(x => x.User).IsRequired().HasMaxLength(200);

            e.HasOne(x => x.Shot)
             .WithMany(s => s.Comments)
             .HasForeignKey(x => x.ShotId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ShotId);
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

    /// <summary>
    /// Logs family configuration — Phase 2 reshape. The legacy
    /// pre-Marduk artifact entities (LogSample / LogTimeWindow /
    /// LogTimeWindowSample / LogEfdSample / LogProcessing × 3 /
    /// LogSetting / LogFile) are deleted; Log keeps its identity
    /// + RunId + CalibrationId, gains Binary/Config columns, and
    /// owns a single 1:N child collection of LogResultFile rows
    /// (Marduk's processed output — typically LAS files).
    /// </summary>
    private static void ConfigureLogFamily(ModelBuilder b)
    {
        // ---- Log (the captured-data parent) ----
        b.Entity<Log>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShotName).IsRequired().HasMaxLength(200);

            // Parent Run — required, cascade.
            e.HasOne(x => x.Run)
             .WithMany(r => r.Logs)
             .HasForeignKey(x => x.RunId)
             .OnDelete(DeleteBehavior.Cascade);

            // Calibration — optional FK; required for Marduk to
            // process the binary. Restrict on delete: a calibration
            // referenced by Logs needs explicit cleanup.
            e.HasOne(x => x.Calibration)
             .WithMany().HasForeignKey(x => x.CalibrationId)
             .OnDelete(DeleteBehavior.Restrict);

            // IAuditable — audit-log capture + concurrency wired by
            // SaveChangesAsync.
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            // Captured-data columns — Phase 2 simplification.
            e.Property(x => x.Binary).HasColumnType("varbinary(max)");
            e.Property(x => x.BinaryName).HasMaxLength(255);
            e.Property(x => x.ConfigJson).HasColumnType("nvarchar(max)");

            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.CalibrationId);
        });

        // ---- LogResultFile (Marduk's processed output files) ----
        b.Entity<LogResultFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).IsRequired().HasMaxLength(255);
            e.Property(x => x.ContentType).IsRequired().HasMaxLength(100);
            e.Property(x => x.Bytes).HasColumnType("varbinary(max)");

            e.HasOne(x => x.Log)
             .WithMany(l => l.ResultFiles)
             .HasForeignKey(x => x.LogId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.LogId);
        });
    }

    /// <summary>
    /// Append-only change-history table. <c>EntityType</c> +
    /// <c>EntityId</c> indexed for the entity-scoped read query
    /// (<c>WHERE EntityType = 'Survey' AND EntityId = '42'</c>);
    /// <c>ChangedAt</c> indexed for time-range scans. JSON columns
    /// are <c>NVARCHAR(MAX)</c> by EF default — adequate for a few
    /// hundred properties per entity.
    /// </summary>
    private static void ConfigureAuditLog(ModelBuilder b)
    {
        b.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
            e.Property(x => x.EntityId).IsRequired().HasMaxLength(100);
            e.Property(x => x.Action).IsRequired().HasMaxLength(20);
            e.Property(x => x.ChangedBy).IsRequired().HasMaxLength(100);
            e.Property(x => x.ChangedColumns).HasMaxLength(2000);

            // Entity-scoped lookup: "show me all changes to Survey #42"
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            // Time-range queries: "show me all changes in the last 7 days"
            e.HasIndex(x => x.ChangedAt);
        });
    }

    private static void ConfigureModels(ModelBuilder b)
    {
        b.Entity<GradientModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            // Two explicit FKs to Well (target + intercept). Restrict — a
            // modelling scenario references Wells; deleting a Well requires
            // explicit cleanup of the models referencing it.
            e.HasOne(x => x.TargetWell)
             .WithMany()
             .HasForeignKey(x => x.TargetWellId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InterceptWell)
             .WithMany()
             .HasForeignKey(x => x.InterceptWellId)
             .OnDelete(DeleteBehavior.Restrict);

            // Many-to-many GradientModel ↔ Run. Junction table preserves
            // legacy-ish name (GradientModelRun — singular, matches the
            // other tenant-DB pluralisation patterns).
            e.HasMany(x => x.Runs)
             .WithMany(r => r.GradientModels)
             .UsingEntity(j => j.ToTable("GradientModelRun"));

            e.HasIndex(x => x.TargetWellId);
            e.HasIndex(x => x.InterceptWellId);
        });

        b.Entity<RotaryModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.HasOne(x => x.TargetWell)
             .WithMany()
             .HasForeignKey(x => x.TargetWellId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InterceptWell)
             .WithMany()
             .HasForeignKey(x => x.InterceptWellId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Runs)
             .WithMany(r => r.RotaryModels)
             .UsingEntity(j => j.ToTable("RotaryModelRun"));

            e.HasIndex(x => x.TargetWellId);
            e.HasIndex(x => x.InterceptWellId);
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

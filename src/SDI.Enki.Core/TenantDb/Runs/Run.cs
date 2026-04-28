using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Logs;
using SDI.Enki.Core.TenantDb.Models;
using SDI.Enki.Core.TenantDb.Operators;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;

namespace SDI.Enki.Core.TenantDb.Runs;

/// <summary>
/// A survey run — Gradient, Rotary, or Passive. Single unified record type
/// with a <see cref="Type"/> discriminator.
///
/// <para>
/// <b>Phase 2 reshape:</b> the per-shot detail that legacy Athena spread
/// across 60+ tables collapses to (binary file + JSON config + JSON result)
/// tuples that Marduk processes server-side. Concretely:
/// </para>
///
/// <list type="bullet">
///   <item>Gradient and Rotary runs carry many <see cref="Shot"/>s. Each
///   Shot has its own primary capture (Binary + Config + Result) and an
///   optional gyro capture (GyroBinary + GyroConfig + GyroResult).
///   Marduk consumes (binary, config, calibration) at calc time and
///   produces the result.</item>
///   <item>Passive runs have no Shots. Their captured data attaches
///   directly here on the Run row via the <c>Passive*</c> columns
///   (PassiveBinary + PassiveConfigJson + PassiveResultJson). The columns
///   are nullable because Gradient and Rotary runs don't populate them.</item>
///   <item>Any run type can carry zero or more <see cref="Log"/>s — the
///   sensor stream during trip in/out of hole. Logs are independent of
///   Shots and have their own (binary + config + result-files) shape.</item>
/// </list>
///
/// <para>
/// Implements <see cref="IAuditable"/> — CreatedAt / CreatedBy / UpdatedAt /
/// UpdatedBy / RowVersion are managed by
/// <c>TenantDbContext.SaveChangesAsync</c>; don't set them from business code.
/// </para>
/// </summary>
public class Run(string name, string description, double startDepth, double endDepth, RunType type) : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public double StartDepth { get; set; } = startDepth;
    public double EndDepth { get; set; } = endDepth;

    public DateTimeOffset? StartTimestamp { get; set; }
    public DateTimeOffset? EndTimestamp { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Planned;
    public RunType Type { get; set; } = type;

    public Guid JobId { get; set; }

    /// <summary>
    /// Stub tool selection. Persisted name from
    /// <c>SDI.Enki.Shared.Tools.ToolCatalog</c>; calibration is
    /// looked up there. Will be replaced by the real
    /// <c>AMR.Core.Tools</c> integration.
    /// </summary>
    public string? ToolName { get; set; }

    // Gradient-specific — nullable because Rotary and Passive runs don't set them.
    public double? BridleLength { get; set; }
    public double? CurrentInjection { get; set; }

    // -------- Passive-only capture/calc fields --------
    // Populated only when Type == RunType.Passive. Passive runs don't
    // have Shots; the captured data + processing config + Marduk
    // result attach directly here. ResultStatus is the future-calc
    // seam: any change to PassiveBinary or PassiveConfigJson sets it
    // to Pending and clears the Result fields, so a future calc
    // service knows there's work to do.

    /// <summary>
    /// Optional captured binary file for Passive runs. Up to 250 KB
    /// (enforced at the API layer). Null on Gradient and Rotary runs;
    /// nullable on Passive too — the run can exist without an upload.
    /// </summary>
    public byte[]? PassiveBinary { get; set; }
    public string? PassiveBinaryName { get; set; }
    public DateTimeOffset? PassiveBinaryUploadedAt { get; set; }

    /// <summary>
    /// User-supplied processing parameters as JSON. Schema-erased so
    /// the Marduk-side parameter shape can iterate without DB
    /// migrations. Phase 2 keeps this loose; promote to typed columns
    /// when the shape stabilises.
    /// </summary>
    public string? PassiveConfigJson { get; set; }
    public DateTimeOffset? PassiveConfigUpdatedAt { get; set; }

    /// <summary>
    /// Marduk's computed output as JSON. <c>null</c> until the calc
    /// service runs. Recomputable: if Marduk improves or the binary /
    /// config change, the Result is invalidated and regenerated.
    /// </summary>
    public string? PassiveResultJson { get; set; }
    public DateTimeOffset? PassiveResultComputedAt { get; set; }
    public string? PassiveResultMardukVersion { get; set; }

    /// <summary>
    /// Calc-pipeline state. <c>null</c> = idle (no calc requested);
    /// <c>Pending</c> = upload happened, calc service should pick this
    /// up; <c>Computing</c> = service grabbed it; <c>Success</c> /
    /// <c>Failed</c> = terminal. The future calc trigger reads
    /// <c>WHERE PassiveResultStatus = 'Pending'</c>.
    /// </summary>
    public string? PassiveResultStatus { get; set; }
    public string? PassiveResultError { get; set; }

    // IAuditable — managed by TenantDbContext.SaveChangesAsync; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    /// <summary>
    /// Soft-delete marker. <c>null</c> means the run is active and
    /// appears in normal queries; non-null means archived — the row
    /// stays in the DB for audit / restore but is hidden by the
    /// global query filter on <c>TenantDbContext</c>. Set by
    /// <c>RunsController.Delete</c>; cleared by
    /// <c>RunsController.Restore</c>. Same shape as
    /// <see cref="SDI.Enki.Core.TenantDb.Wells.Well.ArchivedAt"/>.
    /// </summary>
    public DateTimeOffset?  ArchivedAt { get; set; }

    // EF navs
    public Job? Job { get; set; }
    public ICollection<Operator> Operators { get; set; } = new List<Operator>();

    /// <summary>
    /// Captured shot events. Populated on Gradient and Rotary runs;
    /// always empty on Passive (Passive's capture lives on this Run
    /// directly via the <c>Passive*</c> columns above).
    /// </summary>
    public ICollection<Shot> Shots { get; set; } = new List<Shot>();

    /// <summary>
    /// Logs collected during this run — sensor stream during trip in
    /// or out of the hole. Independent of Shots; any run type can
    /// have zero or more.
    /// </summary>
    public ICollection<Log> Logs { get; set; } = new List<Log>();

    /// <summary>
    /// Modeling artifacts (anti-collision scenarios, saved snapshots).
    /// Untouched in Phase 2 — these belong to the next phase's
    /// modeling work; left in the schema as the Models family already
    /// references Run.
    /// </summary>
    public ICollection<GradientModel> GradientModels { get; set; } = new List<GradientModel>();
    public ICollection<RotaryModel> RotaryModels { get; set; } = new List<RotaryModel>();
}

using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Shots;

namespace SDI.Enki.Core.TenantDb.Logs;

/// <summary>
/// One log under a <see cref="Run"/> — the sensor stream during a
/// trip in or out of the hole. Any run type can carry zero or more
/// Logs; they're independent of <see cref="Shot"/>s (which are the
/// per-shot capture events for Gradient / Rotary runs).
///
/// <para>
/// <b>Phase 2 reshape:</b> the legacy 10-entity Log family
/// (LogSample / LogTimeWindow / LogEfdSample / LogProcessing × 3
/// types / LogSetting / LogFile) was the pre-Marduk MATLAB-era
/// structure — every per-depth sample landed as a row. With Marduk
/// processing server-side, the captured data is just a binary blob
/// + a JSON config; the result is one or more output files (LAS or
/// similar). The reshape collapses the 10-entity family to:
///
/// <list type="bullet">
///   <item><see cref="Log"/> (this entity) — identity + Binary +
///   ConfigJson + Calibration FK.</item>
///   <item><see cref="LogResultFile"/> — 1:N child holding the
///   processed output files (LAS, etc.) Marduk produces.</item>
/// </list>
///
/// All the sample-shape children are deleted; their data is now
/// inside the binary blob and the result-files attached to it.
/// </para>
///
/// <para>
/// Implements <see cref="IAuditable"/> — every log mutation lands in
/// the per-tenant audit log and surfaces in the "Recent changes"
/// tile on the run detail page. Optimistic concurrency on every PUT
/// via <see cref="RowVersion"/>.
/// </para>
/// </summary>
public class Log(Guid runId, string shotName, DateTimeOffset fileTime) : IAuditable
{
    public int Id { get; set; }

    public Guid RunId { get; set; } = runId;

    public string ShotName { get; set; } = shotName;

    public DateTimeOffset FileTime { get; set; } = fileTime;

    /// <summary>
    /// Optional FK to <see cref="Calibration"/>. Required for Marduk
    /// to compute log results; nullable so a Log can be uploaded
    /// before its calibration is selected. Same shape as
    /// <see cref="Shot.CalibrationId"/>.
    /// </summary>
    public int? CalibrationId { get; set; }

    // ---------- captured data ----------

    /// <summary>
    /// The captured .bin file. Up to 250 KB (enforced at the API
    /// layer). Nullable — a Log row can exist before its binary is
    /// uploaded.
    /// </summary>
    public byte[]? Binary { get; set; }
    public string? BinaryName { get; set; }
    public DateTimeOffset? BinaryUploadedAt { get; set; }

    /// <summary>
    /// User-supplied processing parameters as JSON. Schema-erased so
    /// the Marduk-side parameter shape can iterate without DB
    /// migrations.
    /// </summary>
    public string? ConfigJson { get; set; }
    public DateTimeOffset? ConfigUpdatedAt { get; set; }

    // IAuditable — managed by TenantDbContext.SaveChangesAsync.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF navs
    public Run? Run { get; set; }
    public Calibration? Calibration { get; set; }

    /// <summary>
    /// Output files Marduk produced from the binary + config (e.g.
    /// LAS files). 1:N — a single Log can produce multiple output
    /// files. Cascade-deleted with the parent Log.
    /// </summary>
    public ICollection<LogResultFile> ResultFiles { get; set; } = new List<LogResultFile>();
}

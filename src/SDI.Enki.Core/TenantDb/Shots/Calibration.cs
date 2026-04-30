using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Tenant-side snapshot of a master <c>Calibration</c> row. Created when a
/// <c>Run</c> gets its <c>ToolId</c> assigned: the master Calibration in
/// effect for that tool at that moment is copied into the tenant DB
/// (<see cref="MasterCalibrationId"/> remembers which one). Subsequent
/// Shots / Logs under that run default their own <c>CalibrationId</c> to
/// the snapshot, so the calc pipeline always has a tenant-local payload
/// to feed Marduk without crossing the master ↔ tenant DB boundary on
/// the hot path.
///
/// <para>
/// <b>Reproducibility model:</b> snapshots are append-only. Re-assigning
/// the same tool to a run is idempotent (no duplicate row). Switching
/// the run's tool inserts a NEW snapshot and points the run at it;
/// existing Shots / Logs keep their original snapshot row, so a future
/// re-process of historical captures uses the calibration that was
/// in effect at the time the data was collected.
/// </para>
///
/// <para>
/// Distinct from <c>SDI.Enki.Core.Master.Tools.Calibration</c> which
/// lives in the master DB and is the canonical fleet-wide cal store.
/// This tenant-side row is a copy — schema-compatible columns plus a
/// <see cref="MasterCalibrationId"/> back-reference for traceability.
/// </para>
/// </summary>
public class Calibration : IAuditable
{
    public int Id { get; set; }

    /// <summary>
    /// Soft FK to <c>SDI.Enki.Core.Master.Tools.Calibration.Id</c>.
    /// No SQL constraint (master and tenant DBs are physically
    /// separate); validated at snapshot time and never re-checked
    /// after — calibrations are append-only on the master side too,
    /// so the row this points at can be relied on to remain.
    /// </summary>
    public Guid MasterCalibrationId { get; set; }

    /// <summary>
    /// Soft FK to <c>SDI.Enki.Core.Master.Tools.Tool.Id</c>. Carried
    /// alongside <see cref="MasterCalibrationId"/> so a list of
    /// "snapshots that exist for this tool" is a tenant-local query.
    /// </summary>
    public Guid ToolId { get; set; }

    /// <summary>Denormalised tool serial — convenient for displays.</summary>
    public int SerialNumber { get; set; }

    public DateTimeOffset CalibrationDate { get; set; }

    public string? CalibratedBy { get; set; }

    /// <summary>
    /// Marduk's <c>ToolCalibration</c> JSON payload, copied verbatim
    /// from the master row at snapshot time. The calc pipeline
    /// deserialises this directly without re-fetching.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    public int MagnetometerCount { get; set; }

    /// <summary>
    /// Mirror of <c>SDI.Enki.Core.Master.Tools.Calibration.IsNominal</c>:
    /// true when every accelerometer + magnetometer bias is zero
    /// (placeholder calibration, not real measurement data). Carried
    /// over so list-filter UIs don't need to re-parse the payload.
    /// </summary>
    public bool IsNominal { get; set; }

    // IAuditable — managed by TenantDbContext.SaveChangesAsync. Snapshots
    // are append-only in normal use; UpdatedAt/By stay null.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }
}

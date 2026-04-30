using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;
using TenantCalibration = SDI.Enki.Core.TenantDb.Shots.Calibration;

namespace SDI.Enki.WebApi.Calibrations;

/// <summary>
/// Outcome of <see cref="CalibrationSnapshotService.EnsureSnapshotAsync"/>.
/// Discriminated record so the controller branches cleanly without
/// magic int values.
/// </summary>
public abstract record CalibrationSnapshotResult
{
    /// <summary>The snapshot already exists in the tenant — no insert was queued.</summary>
    public sealed record Existing(TenantCalibration Snapshot) : CalibrationSnapshotResult;

    /// <summary>
    /// A new tenant row was tracked via <c>Add</c>. Caller must
    /// <c>SaveChangesAsync</c> on the tenant DbContext to persist it;
    /// after save, the entity's <c>Id</c> is populated, and any Run
    /// pointing at it via the <c>SnapshotCalibration</c> nav has its
    /// <c>SnapshotCalibrationId</c> FK populated by EF.
    /// </summary>
    public sealed record Created(TenantCalibration Snapshot) : CalibrationSnapshotResult;

    /// <summary>The supplied <c>ToolId</c> doesn't resolve to any row in master Tools.</summary>
    public sealed record ToolNotFound(Guid ToolId) : CalibrationSnapshotResult;

    /// <summary>The tool exists but has no master <c>Calibration</c> rows to snapshot.</summary>
    public sealed record ToolHasNoCalibrations(Guid ToolId) : CalibrationSnapshotResult;
}

/// <summary>
/// Copies the latest non-superseded master <c>Calibration</c> for a
/// given tool into the current tenant DB as a
/// <see cref="TenantCalibration"/> row. Idempotent: re-snapshotting
/// the same master cal returns the existing tenant row without
/// queuing a duplicate insert.
///
/// <para>
/// <b>Why a service:</b> the operation crosses the master ↔ tenant DB
/// boundary, which neither <see cref="EnkiMasterDbContext"/> nor
/// <see cref="TenantDbContext"/> can do alone. Centralised here so
/// the snapshot semantics (idempotence, ordering, failure shapes)
/// stay consistent across every code path that triggers a snapshot
/// (today: <c>RunsController.Create</c> / <c>Update</c> on tool
/// assignment).
/// </para>
///
/// <para>
/// <b>Caller responsibility:</b> on a <c>Created</c> result the
/// service has only queued the insert via
/// <c>tenantDb.Calibrations.Add(...)</c>. The caller batches the
/// <c>SaveChangesAsync</c> with whatever Run mutation triggered the
/// snapshot, so a single optimistic-concurrency window covers both.
/// EF populates the entity's <c>Id</c> + the parent Run's
/// <c>SnapshotCalibrationId</c> FK on save (we wire the nav, not the
/// raw FK).
/// </para>
/// </summary>
public sealed class CalibrationSnapshotService(EnkiMasterDbContext master)
{
    /// <summary>
    /// Ensure a tenant <see cref="TenantCalibration"/> exists for the
    /// latest non-superseded master Calibration of
    /// <paramref name="toolId"/>. See <see cref="CalibrationSnapshotResult"/>
    /// for the outcome shapes the caller branches on.
    /// </summary>
    public async Task<CalibrationSnapshotResult> EnsureSnapshotAsync(
        TenantDbContext tenantDb,
        Guid toolId,
        CancellationToken ct)
    {
        // Pick the latest non-superseded master calibration for the
        // tool. If none, distinguish tool-not-found from tool-has-
        // no-cals so the controller can produce a useful error.
        var latest = await master.Calibrations
            .AsNoTracking()
            .Where(c => c.ToolId == toolId && !c.IsSuperseded)
            .OrderByDescending(c => c.CalibrationDate)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            var toolExists = await master.Tools
                .AsNoTracking()
                .AnyAsync(t => t.Id == toolId, ct);
            return toolExists
                ? new CalibrationSnapshotResult.ToolHasNoCalibrations(toolId)
                : new CalibrationSnapshotResult.ToolNotFound(toolId);
        }

        // Idempotence: if we've already snapshotted this exact master
        // cal into the tenant, hand back the existing row. The unique
        // index on MasterCalibrationId enforces this at the SQL level
        // too — concurrent snapshot attempts for the same master cal
        // produce one winner and surface 409 on the loser, which the
        // caller's optimistic-concurrency loop can re-read past.
        var existing = await tenantDb.Calibrations
            .Where(c => c.MasterCalibrationId == latest.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return new CalibrationSnapshotResult.Existing(existing);

        var snapshot = new TenantCalibration
        {
            MasterCalibrationId = latest.Id,
            ToolId              = latest.ToolId,
            SerialNumber        = latest.SerialNumber,
            CalibrationDate     = latest.CalibrationDate,
            CalibratedBy        = latest.CalibratedBy,
            PayloadJson         = latest.PayloadJson,
            MagnetometerCount   = latest.MagnetometerCount,
            IsNominal           = latest.IsNominal,
        };
        tenantDb.Calibrations.Add(snapshot);
        return new CalibrationSnapshotResult.Created(snapshot);
    }
}

using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tools.Enums;

namespace SDI.Enki.Core.Master.Tools;

/// <summary>
/// A calibration record for a physical <see cref="Tool"/>. Backs Marduk's
/// <c>ICalibrationRegistry</c>. <see cref="PayloadJson"/> serialises
/// Marduk's <c>ToolCalibration</c> domain model verbatim. Fleet-wide — a
/// tool's calibration does not change depending on which tenant it's
/// currently deployed under.
///
/// Append-only: implements <see cref="IAuditable"/> for CreatedAt/By, but
/// updates are not the intended workflow. Supersession is modelled by
/// the <see cref="IsSuperseded"/> flag on older rows; UpdatedAt/By and
/// RowVersion are present (per IAuditable) but expected to stay null in
/// normal operation.
/// </summary>
public class Calibration(Guid toolId, int serialNumber, DateTimeOffset calibrationDate, string payloadJson) : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ToolId { get; set; } = toolId;

    /// <summary>Denormalised tool serial for quick lookup without joining.</summary>
    public int SerialNumber { get; set; } = serialNumber;

    public DateTimeOffset CalibrationDate { get; set; } = calibrationDate;

    /// <summary>Name or username of whoever ran the calibration. Optional.</summary>
    public string? CalibratedBy { get; set; }

    /// <summary>Serialized Marduk <c>ToolCalibration</c> payload.</summary>
    public string PayloadJson { get; set; } = payloadJson;

    /// <summary>
    /// Magnetometer count from the payload, denormalised so list views
    /// don't have to parse <see cref="PayloadJson"/>.
    /// </summary>
    public int MagnetometerCount { get; set; }

    /// <summary>
    /// True when every accelerometer + magnetometer bias is zero — i.e.
    /// the row is a placeholder calibration, not real measurement data.
    /// Computed at insert time and persisted so list filtering doesn't
    /// re-parse the payload.
    /// </summary>
    public bool IsNominal { get; set; }

    /// <summary>
    /// Flips to true when a newer calibration for the same tool is added.
    /// Latest-only views default to <c>!IsSuperseded</c>.
    /// </summary>
    public bool IsSuperseded { get; set; }

    public CalibrationSource Source { get; set; } = CalibrationSource.Imported;

    public string? Notes { get; set; }

    /// <summary>
    /// Zip archive of the original 24 shot binaries (<c>1.bin</c>–<c>24.bin</c>)
    /// when <see cref="Source"/> is <c>ComputedInEnki</c>; null for migrated /
    /// imported calibrations whose raw data wasn't captured. Lets operators
    /// re-process the same input later (different shot enablement / mag
    /// source / reference field overrides) without re-uploading.
    /// </summary>
    public byte[]? RawShotBinariesZip { get; set; }

    /// <summary>
    /// Serialised <c>CalibrationShotData[24]</c> from the NarrowBand pass —
    /// the parsed-and-narrowbanded intermediate that feeds Marduk's
    /// <c>CalibrationComputationService.Compute</c>. Stored alongside the
    /// raw binaries so re-runs can skip the parse + NarrowBand work and
    /// jump straight to varying the compute inputs. Null for migrated /
    /// imported cals.
    /// </summary>
    public string? ParsedShotsJson { get; set; }

    // IAuditable — managed by the DbContext interceptor. Calibrations are
    // append-only in normal use; UpdatedAt/By and RowVersion stay null
    // unless an operator amends Notes/Status post-hoc.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public Tool? Tool { get; set; }
}

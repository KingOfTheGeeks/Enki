using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Licensing.Enums;

namespace SDI.Enki.Core.Master.Licensing;

/// <summary>
/// A signed + encrypted <c>.lic</c> file issued to a customer ("Heimdall"
/// envelope format, v2). The encrypted file bytes are stored on the row
/// so an admin can re-download a previously-issued license without
/// re-running the generator (which depends on point-in-time tool +
/// calibration state).
///
/// The three snapshot JSON columns capture exactly which feature flags,
/// which tools, and which calibrations were baked into the file. This is
/// not for re-generation — the file is the source of truth — it's an
/// audit aid so an admin reviewing an old license can see what's in it
/// without decrypting.
///
/// Implements <see cref="IAuditable"/>: revocation flips Status + sets
/// RevokedAt + RevokedReason, which counts as a normal mutation, so the
/// audit interceptor stamps UpdatedAt/By.
/// </summary>
public class License(string licensee, Guid licenseKey, DateTimeOffset issuedAt, DateTimeOffset expiresAt) : IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Decryption key — the customer types this in to activate. Stored in the clear for re-download.</summary>
    public Guid LicenseKey { get; set; } = licenseKey;

    /// <summary>Customer-facing licensee name (org / company).</summary>
    public string Licensee { get; set; } = licensee;

    public DateTimeOffset IssuedAt  { get; set; } = issuedAt;
    public DateTimeOffset ExpiresAt { get; set; } = expiresAt;

    public LicenseStatus Status { get; set; } = LicenseStatus.Active;

    /// <summary>Serialised <see cref="Shared.Licensing.LicenseFeaturesDto"/> at issue time.</summary>
    public string FeaturesJson { get; set; } = "{}";

    /// <summary>Tool ids + serial snapshot (audit aid; the file itself is authoritative).</summary>
    public string ToolSnapshotJson { get; set; } = "[]";

    /// <summary>Calibration ids + names + dates snapshot (audit aid).</summary>
    public string CalibrationSnapshotJson { get; set; } = "[]";

    /// <summary>The encrypted <c>.lic</c> bytes. Re-download serves these unchanged.</summary>
    public byte[] FileBytes { get; set; } = [];

    public string? RevokedReason { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    // IAuditable — managed by the DbContext interceptor.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }
}

using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Shared.Licensing;

namespace SDI.Enki.Infrastructure.Licensing;

/// <summary>
/// Generates the encrypted + signed <c>.lic</c> envelope for a customer.
/// Tests fake this so the fakes don't need to drag the RSA / AES-GCM
/// crypto into their path; the real implementation lives in
/// <see cref="HeimdallLicenseFileGenerator"/>.
/// </summary>
public interface ILicenseFileGenerator
{
    /// <summary>
    /// Builds the canonical signed JSON, encrypts it with AES-GCM keyed
    /// off <paramref name="licenseKey"/>, and packages the v2 Heimdall
    /// envelope (HMDL header + iterations + salt + nonce + tag +
    /// ciphertext). Marduk's <c>HeimdallEnvelopeDecryptor</c> decrypts.
    /// </summary>
    /// <param name="licensee">Customer-facing org name.</param>
    /// <param name="licenseKey">GUID the customer types in to activate.</param>
    /// <param name="expiry">UTC expiry timestamp baked into the payload.</param>
    /// <param name="tools">Tools whose metadata + chosen calibration go into the envelope.</param>
    /// <param name="calibrationsByToolId">Each tool's chosen calibration row, keyed by tool id.</param>
    /// <param name="features">Feature flags to bake in.</param>
    byte[] Generate(
        string licensee,
        Guid licenseKey,
        DateTime expiry,
        IReadOnlyList<Tool> tools,
        IReadOnlyDictionary<Guid, Calibration> calibrationsByToolId,
        LicenseFeaturesDto features);
}

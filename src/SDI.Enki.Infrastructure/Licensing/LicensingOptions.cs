namespace SDI.Enki.Infrastructure.Licensing;

/// <summary>
/// Bound from the <c>Licensing</c> configuration section. The private key
/// path is the only required setting — if missing, the host must refuse
/// to start so a misconfigured prod doesn't silently swallow license-
/// generation requests. Dev typically points at <c>dev-keys/private.pem</c>;
/// prod is set via <c>Enki__Licensing__PrivateKeyPath</c> env var or
/// equivalent.
/// </summary>
public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";

    /// <summary>
    /// Path (absolute or relative to the host's content root) to the
    /// PEM-encoded RSA private key used for license signing. Required.
    /// </summary>
    public string PrivateKeyPath { get; set; } = "";

    /// <summary>
    /// PBKDF2 iteration count for the AES-GCM key derivation. Defaults
    /// to 600,000 (matches Nabu's emitter and Marduk's
    /// <c>HeimdallEnvelopeDecryptor</c> which doesn't care about the
    /// number — it reads it out of the envelope header).
    /// </summary>
    public int Pbkdf2Iterations { get; set; } = 600_000;
}

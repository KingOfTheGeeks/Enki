using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Shared.Calibrations;
using SDI.Enki.Shared.Licensing;

namespace SDI.Enki.Infrastructure.Licensing;

/// <summary>
/// Generates Heimdall-format v2 license envelopes — the exact byte
/// layout produced by Nabu's <c>LicenseService</c> and consumed by
/// Marduk's <c>HeimdallEnvelopeDecryptor</c>:
///
/// <code>
///   "HMDL"           4 bytes  ASCII magic
///   version          1 byte   0x02
///   kdfAlg           1 byte   0x01 (PBKDF2-SHA256)
///   iterations       4 bytes  uint32 little-endian
///   saltLen          1 byte   16
///   nonceLen         1 byte   12
///   tagLen           1 byte   16
///   salt             16 bytes
///   nonce            12 bytes
///   tag              16 bytes
///   ciphertext       N bytes  AES-GCM, no AAD
/// </code>
///
/// The plaintext is a UTF-8 JSON document with the canonical signed
/// shape (Licensee / Expiry / Tools / Calibrations / 11 feature flags
/// / Signature). The signature is RSA-SHA256 PKCS#1 v1.5 over the
/// SAME JSON minus the Signature field, computed first; the Signature
/// is then appended to the JSON before encryption.
///
/// Don't change a byte — Esagila and any field-deployed Marduk decoder
/// rely on this exact format.
/// </summary>
public sealed class HeimdallLicenseFileGenerator : ILicenseFileGenerator
{
    private readonly LicensingOptions _options;
    private readonly ILogger<HeimdallLicenseFileGenerator> _logger;
    private readonly string _resolvedKeyPath;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        // Match Nabu: PascalCase property names, no indentation. Marduk's
        // LicenseDocumentParser uses PropertyNamingPolicy=null too, so the
        // shape is byte-stable.
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions PayloadReadOptions = new(JsonSerializerDefaults.Web);

    public HeimdallLicenseFileGenerator(
        IOptions<LicensingOptions> options,
        ILogger<HeimdallLicenseFileGenerator> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.PrivateKeyPath))
            throw new InvalidOperationException(
                "Licensing:PrivateKeyPath is not configured. Set it in appsettings " +
                "(or the Enki__Licensing__PrivateKeyPath env var) so the license " +
                "generator can sign issued .lic files.");

        _resolvedKeyPath = Path.IsPathRooted(_options.PrivateKeyPath)
            ? _options.PrivateKeyPath
            : Path.Combine(AppContext.BaseDirectory, _options.PrivateKeyPath);

        if (!File.Exists(_resolvedKeyPath))
            throw new InvalidOperationException(
                $"License signing private key not found at '{_resolvedKeyPath}'. " +
                "Generate one (openssl genrsa -out private.pem 2048) or update " +
                "Licensing:PrivateKeyPath.");
    }

    public byte[] Generate(
        string licensee,
        Guid licenseKey,
        DateTime expiry,
        IReadOnlyList<Tool> tools,
        IReadOnlyDictionary<Guid, Calibration> calibrationsByToolId,
        LicenseFeaturesDto features)
    {
        if (string.IsNullOrWhiteSpace(licensee))
            throw new ArgumentException("Licensee is required.", nameof(licensee));
        if (tools.Count == 0)
            throw new ArgumentException("At least one tool is required.", nameof(tools));

        var expiryUtc = expiry.Kind == DateTimeKind.Utc ? expiry : expiry.ToUniversalTime();

        var toolRecords = tools.Select(BuildToolRecord).ToList();

        var calRecords = new List<object>(tools.Count);
        foreach (var tool in tools)
        {
            if (!calibrationsByToolId.TryGetValue(tool.Id, out var cal))
                throw new InvalidOperationException(
                    $"Tool {tool.SerialNumber} has no calibration assigned for the license.");

            calRecords.Add(BuildCalibrationRecord(cal));
        }

        // 1) UNSIGNED canonical payload — exact field order Nabu uses.
        var unsigned = BuildPayloadObject(licensee, expiryUtc, toolRecords, calRecords, features, signature: null);
        var unsignedJson = JsonSerializer.Serialize(unsigned, PayloadJsonOptions);
        var dataToSign = Encoding.UTF8.GetBytes(unsignedJson);

        // 2) RSA-SHA256 PKCS#1 v1.5 signature.
        byte[] signatureBytes;
        using (var rsa = RSA.Create())
        {
            var pem = File.ReadAllText(_resolvedKeyPath, Encoding.UTF8);
            rsa.ImportFromPem(pem);
            signatureBytes = rsa.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        CryptographicOperations.ZeroMemory(dataToSign);
        var base64Signature = Convert.ToBase64String(signatureBytes);

        // 3) SIGNED payload — same shape with Signature field appended.
        var signed = BuildPayloadObject(licensee, expiryUtc, toolRecords, calRecords, features, base64Signature);
        var signedJson = JsonSerializer.Serialize(signed, PayloadJsonOptions);

        // 4) Derive AES key + encrypt with AES-GCM (no AAD; matches Nabu +
        //    Marduk's no-AAD decryption branch).
        var salt = RandomNumberGenerator.GetBytes(16);
        var key  = Rfc2898DeriveBytes.Pbkdf2(
            licenseKey.ToString("D"),  // Marduk's NormalizeKey tries D/N/B/P + casing; we pin to D.
            salt,
            _options.Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            32);
        var nonce = RandomNumberGenerator.GetBytes(12);

        var plaintext = Encoding.UTF8.GetBytes(signedJson);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using (var gcm = new AesGcm(key, tagSizeInBytes: 16))
        {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(key);

        // 5) Package as v2 envelope. BinaryWriter writes integers little-endian,
        //    matching Marduk's BinaryPrimitives.ReadUInt32LittleEndian on read.
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(Encoding.ASCII.GetBytes("HMDL"));
            bw.Write((byte)2);                          // version
            bw.Write((byte)1);                          // kdfAlg = PBKDF2-SHA256
            bw.Write(_options.Pbkdf2Iterations);        // uint32 LE
            bw.Write((byte)16);                         // saltLen
            bw.Write((byte)12);                         // nonceLen
            bw.Write((byte)16);                         // tagLen
            bw.Write(salt);
            bw.Write(nonce);
            bw.Write(tag);
            bw.Write(ciphertext);
        }

        _logger.LogInformation("Generated {Bytes}-byte license for {Licensee}", ms.Length, licensee);
        return ms.ToArray();
    }

    private static object BuildToolRecord(Tool tool)
    {
        var (major, minor) = ParseFirmware(tool.FirmwareVersion);
        return new
        {
            tool.SerialNumber,
            FirmwareVersion = new { Major = major, Minor = minor },
            tool.Configuration,
            tool.Size,
            tool.MagnetometerCount,
            tool.AccelerometerCount,
            tool.Id,
        };
    }

    private static object BuildCalibrationRecord(Calibration cal)
    {
        var p = JsonSerializer.Deserialize<ToolCalibrationPayload>(cal.PayloadJson, PayloadReadOptions)
            ?? throw new InvalidOperationException(
                $"Calibration {cal.Id} payload could not be deserialised.");

        // Marduk's ToolCalibration has AccelerometerAxisPermutation as int[][]
        // even though the JSON entries can come in as doubles — cast to int
        // here so the consumer's typed deserialise doesn't choke.
        var accelPerm = p.AccelerometerAxisPermutation
            .Select(row => row.Select(v => (int)v).ToArray())
            .ToArray();

        return new
        {
            Id                = p.Id,
            ToolId            = p.ToolId,
            p.Name,
            p.MagnetometerCount,
            p.CalibrationDate,
            p.CalibratedBy,
            AccelerometerAxisPermutation = accelPerm,
            p.AccelerometerBias,
            p.AccelerometerScaleFactor,
            p.AccelerometerAlignmentAngles,
            p.MagnetometerAxisPermutation,
            p.MagnetometerBias,
            p.MagnetometerScaleFactor,
            p.MagnetometerAlignmentAngles,
            p.MagnetometerLocations,
        };
    }

    private static object BuildPayloadObject(
        string licensee,
        DateTime expiryUtc,
        List<object> toolRecords,
        List<object> calRecords,
        LicenseFeaturesDto features,
        string? signature)
    {
        // Property order matches Nabu's LicenseService exactly. Signature is
        // omitted on the unsigned pass and appended last on the signed pass.
        if (signature is null)
        {
            return new
            {
                Licensee = licensee,
                Expiry = expiryUtc,
                Tools = toolRecords,
                Calibrations = calRecords,
                features.AllowWarrior,
                features.AllowNorthSea,
                features.AllowSerial,
                features.AllowRotary,
                features.AllowGradient,
                features.AllowPassive,
                features.AllowWarriorLogging,
                features.AllowCalibrate,
                features.AllowSurvey,
                features.AllowResults,
                features.AllowGyro,
            };
        }

        return new
        {
            Licensee = licensee,
            Expiry = expiryUtc,
            Tools = toolRecords,
            Calibrations = calRecords,
            features.AllowWarrior,
            features.AllowNorthSea,
            features.AllowSerial,
            features.AllowRotary,
            features.AllowGradient,
            features.AllowPassive,
            features.AllowWarriorLogging,
            features.AllowCalibrate,
            features.AllowSurvey,
            features.AllowResults,
            features.AllowGyro,
            Signature = signature,
        };
    }

    private static (int Major, int Minor) ParseFirmware(string firmware)
    {
        var parts = firmware.Split('.', 2);
        var major = parts.Length > 0 && int.TryParse(parts[0], out var mj) ? mj : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 0;
        return (major, minor);
    }
}

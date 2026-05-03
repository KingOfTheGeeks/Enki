using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Licensing;
using SDI.Enki.Core.Master.Licensing.Enums;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Licensing;
using SDI.Enki.Shared.Licensing;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Coverage for <see cref="LicensesController"/> — the request-validation
/// + DB-mutation surface. The crypto round-trip is verified separately
/// in <c>HeimdallLicenseFileGeneratorTests</c>; here a fake generator
/// returns canned bytes so we can land all branch coverage without
/// invoking the AES-GCM pipeline.
/// </summary>
public class LicensesControllerTests
{
    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"licenses-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private sealed class FakeGenerator : ILicenseFileGenerator
    {
        public byte[] LastBytes { get; private set; } = [];
        public byte[] Generate(
            string licensee, Guid licenseKey, DateTime expiry,
            IReadOnlyList<Tool> tools,
            IReadOnlyDictionary<Guid, Calibration> calibrationsByToolId,
            LicenseFeaturesDto features)
        {
            // Canned bytes — assertions only need a non-empty array
            // and to confirm the controller round-tripped them through
            // the License row.
            LastBytes = [0x48, 0x4D, 0x44, 0x4C, 0x02];   // "HMDL" + version
            return LastBytes;
        }
    }

    private static LicensesController NewSut(EnkiMasterDbContext db, ILicenseFileGenerator? gen = null)
    {
        return new LicensesController(db, gen ?? new FakeGenerator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static Tool NewTool(int serial = 1000001) =>
        new(serialNumber: serial, firmwareVersion: "1.55", magnetometerCount: 3, accelerometerCount: 1)
        {
            Generation = ToolGeneration.G2,
            Status     = ToolStatus.Active,
        };

    private static Calibration NewCalibration(Tool tool, bool isSuperseded = false, bool isNominal = false) =>
        new(toolId: tool.Id, serialNumber: tool.SerialNumber,
            calibrationDate: DateTimeOffset.UtcNow.AddDays(-30),
            payloadJson: "{}")
        {
            CalibratedBy      = "M.King",
            MagnetometerCount = 3,
            Source            = CalibrationSource.ComputedInEnki,
            IsSuperseded      = isSuperseded,
            IsNominal         = isNominal,
        };

    private static License NewLicense(string licensee = "Permian Crest Corp")
    {
        // The controller's create path serialises the snapshot lists; for
        // List/Get/Download tests we can fill those with stable JSON.
        var tool = new LicenseToolSnapshotDto(Guid.NewGuid(), 1000001, "1.55", 3, 1);
        var cal = new LicenseCalibrationSnapshotDto(Guid.NewGuid(), tool.Id, 1000001, "G2-1000001-2025-11-23", DateTimeOffset.UtcNow.AddDays(-30), "M.King");
        var features = new LicenseFeaturesDto(AllowWarrior: true, AllowGradient: true);

        return new License(
                licensee:   licensee,
                licenseKey: Guid.NewGuid(),
                issuedAt:   DateTimeOffset.UtcNow,
                expiresAt:  DateTimeOffset.UtcNow.AddYears(1))
        {
            FeaturesJson            = JsonSerializer.Serialize(features),
            ToolSnapshotJson        = JsonSerializer.Serialize(new[] { tool }),
            CalibrationSnapshotJson = JsonSerializer.Serialize(new[] { cal }),
            FileBytes               = [0x48, 0x4D, 0x44, 0x4C],
        };
    }

    // ---------- list ----------

    [Fact]
    public async Task List_ReturnsLicensesOrderedByIssuedAtDescending()
    {
        await using var db = NewDb();
        var older = NewLicense("Alpha");   older.IssuedAt = DateTimeOffset.UtcNow.AddDays(-5);
        var newer = NewLicense("Bravo");   newer.IssuedAt = DateTimeOffset.UtcNow.AddDays(-1);
        db.Licenses.AddRange(older, newer);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var list = (await sut.List(CancellationToken.None)).ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("Bravo", list[0].Licensee);
        Assert.Equal("Alpha", list[1].Licensee);
        Assert.Equal(1, list[0].ToolCount);
        Assert.Equal(1, list[0].CalibrationCount);
    }

    // ---------- detail ----------

    [Fact]
    public async Task Get_KnownId_ReturnsDetailWithDeserializedJson()
    {
        await using var db = NewDb();
        var lic = NewLicense();
        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(lic.Id, CancellationToken.None));
        var dto = Assert.IsType<LicenseDetailDto>(ok.Value);
        Assert.Equal(lic.Id, dto.Id);
        Assert.Single(dto.Tools);
        Assert.Single(dto.Calibrations);
        Assert.True(dto.Features.AllowWarrior);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.Get(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    // ---------- file download ----------

    [Fact]
    public async Task DownloadFile_KnownId_ReturnsFileBytes()
    {
        await using var db = NewDb();
        var lic = NewLicense("Crest Energy / Pad-7");   // includes '/' to test sanitisation
        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var result = await sut.DownloadFile(lic.Id, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/octet-stream", fileResult.ContentType);
        Assert.Equal(lic.FileBytes, fileResult.FileContents);
        Assert.DoesNotContain('/', fileResult.FileDownloadName);
        Assert.EndsWith(".lic", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task DownloadFile_UnknownId_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.DownloadFile(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task DownloadKeyFile_KnownId_ReturnsTextWithKeyDetails()
    {
        await using var db = NewDb();
        var lic = NewLicense("Crest Energy");
        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var result = await sut.DownloadKeyFile(lic.Id, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.StartsWith("text/plain", fileResult.ContentType);
        var content = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.Contains("Crest Energy", content);
        Assert.Contains(lic.LicenseKey.ToString("D"), content);
    }

    [Fact]
    public async Task DownloadKeyFile_UnknownId_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.DownloadKeyFile(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    // ---------- create ----------

    [Fact]
    public async Task Create_EmptyLicenseKey_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var dto = new CreateLicenseDto(
            Licensee:                       "X",
            LicenseKey:                     Guid.Empty,
            ExpiresAt:                      DateTime.UtcNow.AddYears(1),
            ToolIds:                        [Guid.NewGuid()],
            CalibrationOverridesByToolId:   null,
            Features:                       new LicenseFeaturesDto());

        var obj = Assert.IsType<ObjectResult>(await sut.Create(dto, CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Create_WarriorLoggingWithoutWarrior_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var dto = new CreateLicenseDto(
            Licensee:                       "X",
            LicenseKey:                     Guid.NewGuid(),
            ExpiresAt:                      DateTime.UtcNow.AddYears(1),
            ToolIds:                        [Guid.NewGuid()],
            CalibrationOverridesByToolId:   null,
            Features:                       new LicenseFeaturesDto(
                AllowWarrior:        false,
                AllowWarriorLogging: true));

        var obj = Assert.IsType<ObjectResult>(await sut.Create(dto, CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateLicenseKey_ReturnsConflictProblem()
    {
        await using var db = NewDb();
        var existing = NewLicense();
        db.Licenses.Add(existing);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var dto = new CreateLicenseDto(
            Licensee:                       "X",
            LicenseKey:                     existing.LicenseKey,
            ExpiresAt:                      DateTime.UtcNow.AddYears(1),
            ToolIds:                        [Guid.NewGuid()],
            CalibrationOverridesByToolId:   null,
            Features:                       new LicenseFeaturesDto());

        var obj = Assert.IsType<ObjectResult>(await sut.Create(dto, CancellationToken.None));
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyToolIds_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        // ToolIds [] hits the `toolIds.Count == 0` gate after the
        // distinct() filter — DataAnnotations MinLength(1) is a separate
        // ModelState check that doesn't run in unit-test invocation.
        var dto = new CreateLicenseDto(
            Licensee:                       "X",
            LicenseKey:                     Guid.NewGuid(),
            ExpiresAt:                      DateTime.UtcNow.AddYears(1),
            ToolIds:                        [],
            CalibrationOverridesByToolId:   null,
            Features:                       new LicenseFeaturesDto());

        var obj = Assert.IsType<ObjectResult>(await sut.Create(dto, CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownToolId_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var unknownId = Guid.NewGuid();
        var dto = new CreateLicenseDto(
            Licensee:                       "X",
            LicenseKey:                     Guid.NewGuid(),
            ExpiresAt:                      DateTime.UtcNow.AddYears(1),
            ToolIds:                        [unknownId],
            CalibrationOverridesByToolId:   null,
            Features:                       new LicenseFeaturesDto());

        var obj = Assert.IsType<ObjectResult>(await sut.Create(dto, CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Create_ToolWithNoCalibration_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var tool = NewTool();
        db.Tools.Add(tool);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var dto = new CreateLicenseDto(
            Licensee:                       "X",
            LicenseKey:                     Guid.NewGuid(),
            ExpiresAt:                      DateTime.UtcNow.AddYears(1),
            ToolIds:                        [tool.Id],
            CalibrationOverridesByToolId:   null,
            Features:                       new LicenseFeaturesDto());

        var obj = Assert.IsType<ObjectResult>(await sut.Create(dto, CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Create_CalibrationOverrideNotFoundForTool_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var tool = NewTool();
        db.Tools.Add(tool);
        var cal = NewCalibration(tool);
        db.Calibrations.Add(cal);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var dto = new CreateLicenseDto(
            Licensee:   "X",
            LicenseKey: Guid.NewGuid(),
            ExpiresAt:  DateTime.UtcNow.AddYears(1),
            ToolIds:    [tool.Id],
            // Override points at a calibration that exists but for a
            // *different* tool id (we use a random Guid here so the
            // (Id == explicitId && ToolId == tool.Id) clause fails).
            CalibrationOverridesByToolId: new Dictionary<Guid, Guid>
            {
                [tool.Id] = Guid.NewGuid(),
            },
            Features:   new LicenseFeaturesDto());

        var obj = Assert.IsType<ObjectResult>(await sut.Create(dto, CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Create_HappyPath_PersistsAndReturns201()
    {
        await using var db = NewDb();
        var tool = NewTool();
        db.Tools.Add(tool);
        var cal = NewCalibration(tool);
        db.Calibrations.Add(cal);
        await db.SaveChangesAsync();

        var generator = new FakeGenerator();
        var sut = NewSut(db, generator);

        var dto = new CreateLicenseDto(
            Licensee:                       "Crest Energy",
            LicenseKey:                     Guid.NewGuid(),
            ExpiresAt:                      DateTime.UtcNow.AddYears(1),
            ToolIds:                        [tool.Id],
            CalibrationOverridesByToolId:   null,
            Features:                       new LicenseFeaturesDto(AllowGradient: true));

        var result = await sut.Create(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<LicenseDetailDto>(created.Value);
        Assert.Equal("Crest Energy", detail.Licensee);
        Assert.Equal(generator.LastBytes.Length, detail.FileSizeBytes);

        // Persisted with the exact bytes the generator emitted.
        var persisted = await db.Licenses.AsNoTracking().FirstAsync(l => l.Id == detail.Id);
        Assert.Equal(generator.LastBytes, persisted.FileBytes);
    }

    // ---------- revoke ----------

    [Fact]
    public async Task Revoke_KnownActiveLicense_FlipsStatusToRevoked()
    {
        await using var db = NewDb();
        var lic = NewLicense();
        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var result = await sut.Revoke(lic.Id, new RevokeLicenseDto("Customer churn"), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        var reloaded = await db.Licenses.AsNoTracking().FirstAsync(l => l.Id == lic.Id);
        Assert.Equal(LicenseStatus.Revoked, reloaded.Status);
        Assert.Equal("Customer churn", reloaded.RevokedReason);
        Assert.NotNull(reloaded.RevokedAt);
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_IsIdempotent()
    {
        await using var db = NewDb();
        var lic = NewLicense();
        lic.Status        = LicenseStatus.Revoked;
        lic.RevokedAt     = DateTimeOffset.UtcNow.AddDays(-1);
        lic.RevokedReason = "first revocation";
        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var result = await sut.Revoke(lic.Id, new RevokeLicenseDto("second attempt"), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        // Idempotent: original revocation reason / timestamp preserved.
        var reloaded = await db.Licenses.AsNoTracking().FirstAsync(l => l.Id == lic.Id);
        Assert.Equal("first revocation", reloaded.RevokedReason);
    }

    [Fact]
    public async Task Revoke_UnknownId_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.Revoke(Guid.NewGuid(), new RevokeLicenseDto("nope"), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }
}

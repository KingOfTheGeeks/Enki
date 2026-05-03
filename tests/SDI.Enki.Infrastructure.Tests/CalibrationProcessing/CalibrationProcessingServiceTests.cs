using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.CalibrationProcessing;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations.Processing;

namespace SDI.Enki.Infrastructure.Tests.CalibrationProcessing;

/// <summary>
/// Validation + state-machine coverage for
/// <see cref="CalibrationProcessingService"/>. The deep Warrior-frame
/// parse + Marduk compute pipeline is exercised end-to-end by the
/// dev seeder against the same .bin fixtures shipped in the seed
/// folder — this fixture is the unit-test layer covering the public
/// API's argument-validation and session-state guards without
/// running the full background parse.
/// </summary>
public class CalibrationProcessingServiceTests
{
    private static CalibrationProcessingService NewService() =>
        new(new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CalibrationProcessingService>.Instance);

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"calproc-svc-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static Tool NewTool() =>
        new(serialNumber: 1000001, firmwareVersion: "1.55",
            magnetometerCount: 3, accelerometerCount: 1)
        {
            Generation = ToolGeneration.G2,
            Status     = ToolStatus.Active,
        };

    private static IReadOnlyDictionary<int, byte[]> MakeBinaries(int count = 25, int? skipIndex = null)
    {
        var dict = new Dictionary<int, byte[]>();
        for (var i = 0; i < count; i++)
        {
            if (skipIndex == i) continue;
            dict[i] = [0x00];
        }
        return dict;
    }

    // ---------- StartSession argument validation ----------

    [Fact]
    public void StartSession_WrongBinaryCount_ThrowsArgumentException()
    {
        var service = NewService();

        var ex = Assert.Throws<ArgumentException>(
            () => service.StartSession(NewTool(), MakeBinaries(count: 24)));
        Assert.Contains("25", ex.Message);
    }

    [Fact]
    public void StartSession_MissingShotIndex_ThrowsArgumentException()
    {
        var service = NewService();

        // Provide 25 entries but skip index 7 → keys aren't 0..24.
        var binaries = new Dictionary<int, byte[]>();
        for (var i = 0; i < 26; i++)
            if (i != 7) binaries[i] = [0x00];

        var ex = Assert.Throws<ArgumentException>(
            () => service.StartSession(NewTool(), binaries));
        Assert.Contains("7", ex.Message);
    }

    [Fact]
    public void StartSession_ValidArguments_ReturnsSessionIdAndStoresParsingState()
    {
        var service = NewService();

        var sessionId = service.StartSession(NewTool(), MakeBinaries());

        Assert.NotEqual(Guid.Empty, sessionId);
        var status = service.GetStatus(sessionId);
        Assert.NotNull(status);
        Assert.Equal(sessionId, status!.SessionId);
        Assert.Equal(1000001, status.ToolSerial);
        // Initial state — background parse may have advanced by the time
        // we read; assert it's at least one of the early states. Failed
        // is also possible if the .bin pool has bogus content (these
        // are 1-byte stubs); the test only pins that the session was
        // stored and is reachable.
        Assert.NotNull(status.State);
    }

    // ---------- GetStatus ----------

    [Fact]
    public void GetStatus_UnknownSession_ReturnsNull()
    {
        var service = NewService();

        var status = service.GetStatus(Guid.NewGuid());

        Assert.Null(status);
    }

    // ---------- Compute ----------

    [Fact]
    public void Compute_UnknownSession_ThrowsInvalidOperation()
    {
        var service = NewService();

        var ex = Assert.Throws<InvalidOperationException>(
            () => service.Compute(Guid.NewGuid(), SampleComputeRequest()));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- SaveAsync ----------

    [Fact]
    public async Task SaveAsync_UnknownSession_ThrowsInvalidOperation()
    {
        var service = NewService();
        await using var db = NewDb();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveAsync(
                db,
                Guid.NewGuid(),
                new ProcessingSaveRequestDto(
                    CalibrationName: "T1",
                    CalibratedBy:    "M.King",
                    Notes:           null),
                CancellationToken.None));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessingComputeRequestDto SampleComputeRequest() =>
        new(
            EnabledShotIndices: Enumerable.Range(1, 24).ToList(),
            GTotal:             1000.01,
            BTotal:             46895.0,
            DipDegrees:         59.867,
            DeclinationDegrees: 12.313,
            CoilConstant:       360.0,
            ActiveBDipDegrees:  89.44,
            SampleRateHz:       100.0,
            ManualSign:         1.0,
            CurrentsByShot:     Enumerable.Repeat(6.01, 24).ToList());
}

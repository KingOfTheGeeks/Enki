using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Settings;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Settings;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="SystemSettingsController"/>
/// + the small public <see cref="JobRegionSuggestionsController"/> in
/// the same file. The auth-policy gate is enforced upstream by the
/// host pipeline (covered by integration tests); these tests pin the
/// data-mutation logic — the allowlist gate, the upsert behaviour,
/// and the "list returns every known key even when the row doesn't
/// exist" projection.
/// </summary>
public class SystemSettingsControllerTests
{
    // ---------- fixture helpers ----------

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"system-settings-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static SystemSettingsController NewController(EnkiMasterDbContext db)
    {
        return new SystemSettingsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static JobRegionSuggestionsController NewSuggestionsController(EnkiMasterDbContext db)
    {
        return new JobRegionSuggestionsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    // ---------- list ----------

    [Fact]
    public async Task List_EmptyTable_ReturnsRowsForEveryKnownKey()
    {
        // The list endpoint deliberately materialises every key in
        // SystemSettingKeys.All so the admin UI can present them as
        // "unset" rather than "missing". A regression here would
        // silently drop unset keys from the UI.
        await using var db = NewDb();
        var sut = NewController(db);

        var ok   = Assert.IsType<OkObjectResult>(await sut.List(CancellationToken.None));
        var rows = Assert.IsType<List<SystemSettingDto>>(ok.Value);

        Assert.Equal(SystemSettingKeys.All.Count, rows.Count);
        Assert.All(rows, r => Assert.Contains(r.Key, SystemSettingKeys.All));
        Assert.All(rows, r => Assert.Equal("", r.Value));
    }

    [Fact]
    public async Task List_WithExistingRow_PopulatesValueAndAuditFields()
    {
        await using var db = NewDb();
        var setting = new SystemSetting(SystemSettingKeys.JobRegionSuggestions, "Permian Basin\nNorth Sea")
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "mike.king",
        };
        db.SystemSettings.Add(setting);
        await db.SaveChangesAsync();

        var sut = NewController(db);
        var ok   = Assert.IsType<OkObjectResult>(await sut.List(CancellationToken.None));
        var rows = Assert.IsType<List<SystemSettingDto>>(ok.Value);

        var match = rows.Single(r => r.Key == SystemSettingKeys.JobRegionSuggestions);
        Assert.Equal("Permian Basin\nNorth Sea", match.Value);
        Assert.Equal("mike.king", match.UpdatedBy);
        Assert.NotNull(match.UpdatedAt);
    }

    // ---------- set ----------

    [Fact]
    public async Task Set_UnknownKey_ReturnsValidationProblem()
    {
        // Unknown keys must be rejected at the write endpoint — the
        // table allowlist is the only thing keeping the admin UI
        // from accumulating typos.
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Set("Jobs:NotARealKey",
            new SetSystemSettingDto(Value: "anything"),
            CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);

        // Nothing should have landed in the DB.
        Assert.Empty(db.SystemSettings.ToList());
    }

    [Fact]
    public async Task Set_KnownKey_NewRow_Inserts()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Set(SystemSettingKeys.JobRegionSuggestions,
            new SetSystemSettingDto(Value: "Bakken\nWilliston"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var stored = await db.SystemSettings.SingleAsync();
        Assert.Equal(SystemSettingKeys.JobRegionSuggestions, stored.Key);
        Assert.Equal("Bakken\nWilliston", stored.Value);
    }

    [Fact]
    public async Task Set_KnownKey_ExistingRow_UpdatesInPlace()
    {
        // Upsert path — second Set on the same key shouldn't create a
        // second row, just update the existing one.
        await using var db = NewDb();
        db.SystemSettings.Add(new SystemSetting(SystemSettingKeys.JobRegionSuggestions, "old value"));
        await db.SaveChangesAsync();

        var sut = NewController(db);
        var result = await sut.Set(SystemSettingKeys.JobRegionSuggestions,
            new SetSystemSettingDto(Value: "new value"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var rows = await db.SystemSettings.ToListAsync();
        Assert.Single(rows);
        Assert.Equal("new value", rows[0].Value);
    }

    // ---------- region suggestions (public-to-authenticated) ----------

    [Fact]
    public async Task RegionSuggestions_NoRow_ReturnsEmptyList()
    {
        await using var db = NewDb();
        var sut = NewSuggestionsController(db);

        var result = await sut.Get(CancellationToken.None);

        Assert.Empty(result.Regions);
    }

    [Fact]
    public async Task RegionSuggestions_SplitsOnNewlinesAndTrimsBlanks()
    {
        // The settings value is stored as one region per line. Mixed
        // \n / \r\n + blank lines from copy-paste editing must come
        // through clean. The Trim option drops trailing whitespace
        // per item.
        await using var db = NewDb();
        db.SystemSettings.Add(new SystemSetting(
            SystemSettingKeys.JobRegionSuggestions,
            "Permian Basin\r\nWilliston Basin\n\nNorth Sea  \n"));
        await db.SaveChangesAsync();

        var sut = NewSuggestionsController(db);
        var result = await sut.Get(CancellationToken.None);

        Assert.Equal(
            new[] { "Permian Basin", "Williston Basin", "North Sea" },
            result.Regions);
    }
}

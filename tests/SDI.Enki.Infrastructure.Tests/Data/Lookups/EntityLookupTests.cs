using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Data.Lookups;

namespace SDI.Enki.Infrastructure.Tests.Data.Lookups;

/// <summary>
/// Behavioral tests for the <c>TenantDbContext.FindOrCreateAsync</c>
/// extension. Uses EF InMemory so we can exercise the query + conditional-
/// insert logic; the DB-level UNIQUE INDEX backstop isn't exercised here
/// (InMemory doesn't enforce unique constraints) — that's covered by the
/// integration smoke when the user runs the migration.
/// </summary>
public class EntityLookupTests
{
    private static TenantDbContext NewContext([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"lookup-{name}-{Guid.NewGuid():N}")
            .Options;
        return new TenantDbContext(options);
    }

    [Fact]
    public async Task FindOrCreateAsync_InsertsWhenMissing()
    {
        await using var db = NewContext();
        var sample = new Magnetics(bTotal: 50000, dip: 60, declination: 5);

        var id = await db.FindOrCreateAsync(
            sample,
            m => m.BTotal == 50000 && m.Dip == 60 && m.Declination == 5,
            m => m.Id);

        Assert.True(id > 0);
        Assert.Equal(1, await db.Magnetics.CountAsync());
    }

    [Fact]
    public async Task FindOrCreateAsync_ReturnsExistingWhenFound()
    {
        await using var db = NewContext();
        db.Magnetics.Add(new Magnetics(50000, 60, 5));
        await db.SaveChangesAsync();
        var existingId = (await db.Magnetics.FirstAsync()).Id;

        var returnedId = await db.FindOrCreateAsync(
            new Magnetics(50000, 60, 5),
            m => m.BTotal == 50000 && m.Dip == 60 && m.Declination == 5,
            m => m.Id);

        Assert.Equal(existingId, returnedId);
        Assert.Equal(1, await db.Magnetics.CountAsync()); // no duplicate inserted
    }

    [Fact]
    public async Task FindOrCreateAsync_InsertsWhenAnyKeyFieldDiffers()
    {
        await using var db = NewContext();
        db.Magnetics.Add(new Magnetics(50000, 60, 5));
        await db.SaveChangesAsync();

        // Different Declination — should insert a second row, not match.
        var id = await db.FindOrCreateAsync(
            new Magnetics(50000, 60, 6),
            m => m.BTotal == 50000 && m.Dip == 60 && m.Declination == 6,
            m => m.Id);

        Assert.True(id > 0);
        Assert.Equal(2, await db.Magnetics.CountAsync());
    }

    [Fact]
    public async Task FindOrCreateAsync_WorksForCalibrations()
    {
        await using var db = NewContext();

        var a = await db.FindOrCreateAsync(
            new Calibration("Tool-42", "<cal>payload</cal>"),
            c => c.Name == "Tool-42" && c.CalibrationString == "<cal>payload</cal>",
            c => c.Id);

        // Same natural key → same row returned, no insert.
        var b = await db.FindOrCreateAsync(
            new Calibration("Tool-42", "<cal>payload</cal>"),
            c => c.Name == "Tool-42" && c.CalibrationString == "<cal>payload</cal>",
            c => c.Id);

        Assert.Equal(a, b);
        Assert.Equal(1, await db.Calibrations.CountAsync());
    }
}

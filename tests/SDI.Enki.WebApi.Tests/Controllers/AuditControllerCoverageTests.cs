using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Audit;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Extra coverage for <see cref="AuditController"/> — the filter
/// arguments on List, the CSV export, and the
/// <c>includeChildren</c> subtree path on
/// <see cref="AuditController.ListForEntity"/>. The base CRUD shape
/// is covered in the original <c>AuditControllerTests</c>; this
/// fixture targets the branches that file leaves out.
/// </summary>
public class AuditControllerCoverageTests
{
    private static AuditController NewController(FakeTenantDbContextFactory factory)
    {
        return new AuditController(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static async Task<(FakeTenantDbContextFactory Factory, Job Job)> SeedJobAndWellAsync()
    {
        var factory = new FakeTenantDbContextFactory();
        await using var db = factory.NewActiveContext();

        var job = new Job("Test", "Test job", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var well = new Well(job.Id, "WELL-1", WellType.Target);
        db.Wells.Add(well);
        await db.SaveChangesAsync();

        return (factory, job);
    }

    // ---------- List filters ----------

    [Fact]
    public async Task List_EntityTypeFilter_LimitsToMatchingRows()
    {
        var (factory, _) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        var result = await sut.List(entityType: "Well", ct: CancellationToken.None);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, r => Assert.Equal("Well", r.EntityType));
    }

    [Fact]
    public async Task List_ActionFilter_LimitsToMatchingRows()
    {
        var (factory, _) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        var result = await sut.List(action: "Created", ct: CancellationToken.None);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, r => Assert.Equal("Created", r.Action));
    }

    [Fact]
    public async Task List_ChangedByFilter_PartialMatch_LimitsRows()
    {
        var (factory, _) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        // Audit rows created in the seed get ChangedBy = "system" (no
        // ICurrentUser registered on the InMemory factory). Substring
        // "sys" should match.
        var result = await sut.List(changedBy: "sys", ct: CancellationToken.None);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, r => Assert.Contains("sys", r.ChangedBy));
    }

    [Fact]
    public async Task List_DateRangeFilter_ExcludesOutsideWindow()
    {
        var (factory, _) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        // Future-only window — must return zero rows.
        var future = DateTimeOffset.UtcNow.AddYears(1);
        var result = await sut.List(
            from: future,
            to:   future.AddDays(1),
            ct:   CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    // ---------- CSV export ----------

    [Fact]
    public async Task ExportCsv_ReturnsTextCsvFileWithRows()
    {
        var (factory, _) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        var result = await sut.ExportCsv(ct: CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", fileResult.ContentType);
        Assert.EndsWith(".csv", fileResult.FileDownloadName);

        var text = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        // Header row contains the expected column names.
        Assert.Contains("EntityType", text);
        Assert.Contains("ChangedBy",  text);
        // At least one data row references our seeded entity type.
        Assert.Contains("Job", text);
    }

    [Fact]
    public async Task ExportCsv_HonoursEntityTypeFilter()
    {
        var (factory, _) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        var result = await sut.ExportCsv(entityType: "Well", ct: CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        var text = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        // Should include Well rows but no Job rows (filter is exact match).
        Assert.Contains("Well", text);
        Assert.DoesNotContain(",Job,", text);
    }

    // ---------- ListForEntity with includeChildren ----------

    [Fact]
    public async Task ListForEntity_IncludeChildrenFalse_ReturnsOnlyEntityRows()
    {
        var (factory, job) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        var result = await sut.ListForEntity(
            entityType:      "Job",
            entityId:        job.Id.ToString(),
            includeChildren: false,
            ct:              CancellationToken.None);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, r =>
        {
            Assert.Equal("Job", r.EntityType);
            Assert.Equal(job.Id.ToString(), r.EntityId);
        });
    }

    [Fact]
    public async Task ListForEntity_IncludeChildrenTrue_OnJob_ReturnsSubtreeRows()
    {
        var (factory, job) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        var result = await sut.ListForEntity(
            entityType:      "Job",
            entityId:        job.Id.ToString(),
            includeChildren: true,
            ct:              CancellationToken.None);

        // Should include the Job row + the Well row at minimum.
        Assert.True(result.Total >= 2);
        var entityTypes = result.Items.Select(r => r.EntityType).Distinct().ToList();
        Assert.Contains("Job",  entityTypes);
        Assert.Contains("Well", entityTypes);
    }

    [Fact]
    public async Task ListForEntity_IncludeChildrenTrue_OnWell_ResolvesSubtreePath()
    {
        var (factory, _) = await SeedJobAndWellAsync();

        // Capture the well id, then add Survey + Tubular under it so
        // the subtree resolver has multiple entity types to enumerate.
        int wellId;
        await using (var db = factory.NewActiveContext())
        {
            var well = db.Wells.First();
            wellId = well.Id;
            db.Surveys.Add(new Survey(well.Id, depth: 0, inclination: 0, azimuth: 0));
            db.Tubulars.Add(new Tubular(well.Id, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 100,
                diameter: 0.1, weight: 50));
            await db.SaveChangesAsync();
        }

        var sut = NewController(factory);

        var result = await sut.ListForEntity(
            entityType:      "Well",
            entityId:        wellId.ToString(),
            includeChildren: true,
            ct:              CancellationToken.None);

        var entityTypes = result.Items.Select(r => r.EntityType).Distinct().ToList();
        Assert.Contains("Well",     entityTypes);
        Assert.Contains("Survey",   entityTypes);
        Assert.Contains("Tubular",  entityTypes);
    }

    [Fact]
    public async Task ListForEntity_UnknownEntityType_TreatsAsLeafNoSubtree()
    {
        var (factory, job) = await SeedJobAndWellAsync();
        var sut = NewController(factory);

        // "Tenant" has no subtree branch in ResolveSubtreePairsAsync;
        // includeChildren=true falls through to "parent only".
        var result = await sut.ListForEntity(
            entityType:      "Unknown",
            entityId:        "doesnt-matter",
            includeChildren: true,
            ct:              CancellationToken.None);

        Assert.Empty(result.Items);
    }
}

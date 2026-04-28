using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="AuditController"/>.
/// Exercises the read shape — paging, ordering, entity filtering —
/// against a FakeTenantDbContextFactory whose underlying InMemory
/// context populates audit rows automatically through the
/// SaveChangesAsync override (verified separately in
/// AuditCaptureTests).
/// </summary>
public class AuditControllerTests
{
    private const string TestTenantCode = "ACME";

    private static AuditController NewController(FakeTenantDbContextFactory factory)
    {
        var controller = new AuditController(factory);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    /// <summary>
    /// Seed three Jobs to give the Tenant audit log enough material to
    /// page through. Each Created emits one audit row → 3 rows total.
    /// </summary>
    private static async Task<FakeTenantDbContextFactory> SeedThreeJobsAsync()
    {
        var factory = new FakeTenantDbContextFactory();
        await using var db = factory.NewActiveContext();

        db.Jobs.Add(new Job("Alpha", "first",  UnitSystem.Field));
        await db.SaveChangesAsync();

        db.Jobs.Add(new Job("Beta",  "second", UnitSystem.Field));
        await db.SaveChangesAsync();

        db.Jobs.Add(new Job("Gamma", "third",  UnitSystem.Field));
        await db.SaveChangesAsync();

        return factory;
    }

    [Fact]
    public async Task List_ReturnsPagedAuditRowsNewestFirst()
    {
        var factory = await SeedThreeJobsAsync();
        var ctrl = NewController(factory);

        var page = await ctrl.List(skip: null, take: null, ct: default);

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
        Assert.False(page.HasMore);
        // Newest first: Gamma was inserted last, so it's at index 0.
        Assert.Collection(page.Items,
            a => Assert.Equal("Job", a.EntityType),
            a => Assert.Equal("Job", a.EntityType),
            a => Assert.Equal("Job", a.EntityType));

        // ChangedAt ordering: descending.
        for (int i = 1; i < page.Items.Count; i++)
            Assert.True(page.Items[i - 1].ChangedAt >= page.Items[i].ChangedAt);
    }

    [Fact]
    public async Task List_HonoursSkipAndTake()
    {
        var factory = await SeedThreeJobsAsync();
        var ctrl = NewController(factory);

        var firstPage = await ctrl.List(skip: 0, take: 2, ct: default);
        Assert.Equal(3, firstPage.Total);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);

        var secondPage = await ctrl.List(skip: 2, take: 2, ct: default);
        Assert.Equal(3, secondPage.Total);
        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasMore);
    }

    [Fact]
    public async Task List_NegativeSkipAndZeroTake_AreClampedToSafeDefaults()
    {
        var factory = await SeedThreeJobsAsync();
        var ctrl = NewController(factory);

        // skip=-5 → 0; take=0 → 1 (Math.Clamp lower bound).
        var page = await ctrl.List(skip: -5, take: 0, ct: default);
        Assert.Equal(0, page.Skip);
        Assert.Equal(1, page.Take);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task List_TakeAboveMax_IsClampedToCeiling()
    {
        var factory = await SeedThreeJobsAsync();
        var ctrl = NewController(factory);

        // take=10000 → 500 ceiling.
        var page = await ctrl.List(skip: 0, take: 10000, ct: default);
        Assert.Equal(500, page.Take);
    }

    [Fact]
    public async Task ListForEntity_FiltersByEntityTypeAndId()
    {
        // Two jobs + one well, then update one of the jobs. The
        // per-entity audit for the updated job should return exactly
        // its Created + Updated rows; the other job and well are
        // filtered out.
        var factory = new FakeTenantDbContextFactory();
        Job target;
        await using (var seed = factory.NewActiveContext())
        {
            target = new Job("Target", "v1", UnitSystem.Field);
            seed.Jobs.Add(target);
            await seed.SaveChangesAsync();

            seed.Jobs.Add(new Job("Other", "irrelevant", UnitSystem.Field));
            await seed.SaveChangesAsync();

            seed.Wells.Add(new Well(target.Id, "OffsetWell", WellType.Offset));
            await seed.SaveChangesAsync();

            target.Description = "v2";
            await seed.SaveChangesAsync();
        }

        var ctrl = NewController(factory);
        var page = await ctrl.ListForEntity(
            entityType: "Job",
            entityId: target.Id.ToString(),
            skip: null, take: null, ct: default);

        Assert.Equal(2, page.Total);
        Assert.Collection(page.Items.OrderBy(a => a.Id),
            a =>
            {
                Assert.Equal("Created", a.Action);
                Assert.Equal(target.Id.ToString(), a.EntityId);
            },
            a =>
            {
                Assert.Equal("Updated", a.Action);
                Assert.Equal("Description", a.ChangedColumns);
            });
    }

    [Fact]
    public async Task ListForEntity_UnknownEntityId_ReturnsEmptyPage()
    {
        var factory = await SeedThreeJobsAsync();
        var ctrl = NewController(factory);

        var page = await ctrl.ListForEntity(
            entityType: "Survey",
            entityId: "999999",
            skip: null, take: null, ct: default);

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }
}

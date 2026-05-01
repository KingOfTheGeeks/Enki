using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="MasterUsersController"/>.
/// The controller is dead simple — a name-prefix search over master
/// users used by the "Add member" picker — so the tests just pin the
/// projection shape, the search behaviour, and the ordering.
///
/// Each test gets a fresh InMemory <see cref="EnkiMasterDbContext"/>
/// (unique name) so parallel xunit execution doesn't cross-pollute.
/// </summary>
public class MasterUsersControllerTests
{
    // ---------- fixture helpers ----------

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"master-users-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static User SeedUser(EnkiMasterDbContext db, string name)
    {
        var user = new User(name, Guid.NewGuid());
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    // ---------- list ----------

    [Fact]
    public async Task List_NoUsers_ReturnsEmpty()
    {
        await using var db = NewDb();
        var sut = new MasterUsersController(db);

        var result = await sut.List(q: null, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task List_NoQuery_ReturnsAllUsersOrderedByName()
    {
        await using var db = NewDb();
        SeedUser(db, "Zara Yusuf");
        SeedUser(db, "Adam Karabasz");
        SeedUser(db, "Mike King");

        var sut = new MasterUsersController(db);

        var result = (await sut.List(q: null, CancellationToken.None)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(
            new[] { "Adam Karabasz", "Mike King", "Zara Yusuf" },
            result.Select(u => u.Username));
    }

    [Fact]
    public async Task List_WithQuery_FiltersByNameContains()
    {
        await using var db = NewDb();
        SeedUser(db, "Mike King");
        SeedUser(db, "Mike Adams");
        SeedUser(db, "Sara Lee");

        var sut = new MasterUsersController(db);

        var result = (await sut.List(q: "Mike", CancellationToken.None)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, u => Assert.Contains("Mike", u.Username));
        // Still ordered by name so the picker UI is deterministic.
        Assert.Equal("Mike Adams", result[0].Username);
        Assert.Equal("Mike King",  result[1].Username);
    }

    [Fact]
    public async Task List_WhitespaceQuery_TreatedAsNoFilter()
    {
        // The controller's IsNullOrWhiteSpace gate means trailing
        // spaces from a copy-paste shouldn't accidentally hide all
        // results.
        await using var db = NewDb();
        SeedUser(db, "Mike King");
        SeedUser(db, "Sara Lee");

        var sut = new MasterUsersController(db);

        var result = (await sut.List(q: "   ", CancellationToken.None)).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task List_QueryWithSurroundingWhitespace_StillMatches()
    {
        // Trim is applied so paste-with-whitespace still finds the user.
        await using var db = NewDb();
        SeedUser(db, "Mike King");

        var sut = new MasterUsersController(db);

        var result = (await sut.List(q: "  Mike  ", CancellationToken.None)).ToList();

        Assert.Single(result);
        Assert.Equal("Mike King", result[0].Username);
    }

    [Fact]
    public async Task List_DtoCarriesIdAndIdentityId()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "Mike King");

        var sut = new MasterUsersController(db);

        var result = (await sut.List(q: null, CancellationToken.None)).Single();

        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(user.IdentityId, result.IdentityId);
        Assert.Equal("Mike King", result.Username);
    }

    // ---------- sync ----------

    [Fact]
    public async Task Sync_NewIdentityId_CreatesMasterRowAndReturns201()
    {
        await using var db = NewDb();
        var sut = new MasterUsersController(db);
        var identityId = Guid.NewGuid();

        var result = await sut.Sync(
            new SyncMasterUserDto(identityId, "Alice Field"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<SyncMasterUserResponseDto>(created.Value);
        Assert.True(dto.Created);
        Assert.NotEqual(Guid.Empty, dto.UserId);

        var saved = await db.Users.SingleAsync(u => u.IdentityId == identityId);
        Assert.Equal("Alice Field", saved.Name);
        Assert.Equal(dto.UserId, saved.Id);
    }

    [Fact]
    public async Task Sync_ExistingIdentityId_NoOpReturnsCreatedFalse()
    {
        // Idempotency contract — same identity id, same display name,
        // no row churn. The Blazor flow retries on transient failure;
        // a re-run after a successful run must not double-create.
        await using var db = NewDb();
        var existing = SeedUser(db, "Alice Field");
        var sut = new MasterUsersController(db);

        var result = await sut.Sync(
            new SyncMasterUserDto(existing.IdentityId, existing.Name),
            CancellationToken.None);

        var ok  = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SyncMasterUserResponseDto>(ok.Value);
        Assert.False(dto.Created);
        Assert.Equal(existing.Id, dto.UserId);
        Assert.Equal(1, await db.Users.CountAsync());   // no churn
    }

    [Fact]
    public async Task Sync_ExistingIdentityId_DifferentName_RefreshesTheName()
    {
        // A profile edit on the Identity side should propagate to the
        // master row's Name on the next sync. Without this the picker
        // shows the old name forever.
        await using var db = NewDb();
        var existing = SeedUser(db, "Alice Field");
        var sut = new MasterUsersController(db);

        var result = await sut.Sync(
            new SyncMasterUserDto(existing.IdentityId, "Alice Renamed"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SyncMasterUserResponseDto>(ok.Value);
        Assert.False(dto.Created);

        var saved = await db.Users.SingleAsync();
        Assert.Equal("Alice Renamed", saved.Name);
    }

    [Fact]
    public async Task Sync_EmptyGuid_Returns400()
    {
        await using var db = NewDb();
        var sut = new MasterUsersController(db);

        var result = await sut.Sync(
            new SyncMasterUserDto(Guid.Empty, "Doesn't matter"),
            CancellationToken.None);

        Assert.IsType<ObjectResult>(result);   // ValidationProblem
        Assert.Equal(0, await db.Users.CountAsync());
    }
}

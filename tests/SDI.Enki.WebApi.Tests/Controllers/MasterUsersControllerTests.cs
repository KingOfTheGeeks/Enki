using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;
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
}

using SDI.Enki.Infrastructure.Auditing;

namespace SDI.Enki.Infrastructure.Tests.Auditing;

/// <summary>
/// Pin the fallback identity returned to design-time tooling, the
/// Migrator CLI, and any host that doesn't register its own
/// <c>ICurrentUser</c>. Changing the literal "system" without
/// changing this test is a regression — every existing audit row in
/// the wild keys off it.
/// </summary>
public class SystemCurrentUserTests
{
    [Fact]
    public void UserIdAndUserName_AreSystem()
    {
        var sut = new SystemCurrentUser();

        Assert.Equal("system", sut.UserId);
        Assert.Equal("system", sut.UserName);
    }
}

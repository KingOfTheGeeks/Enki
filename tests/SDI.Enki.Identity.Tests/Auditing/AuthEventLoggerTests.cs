using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.Identity.Auditing;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Tests.Auditing;

/// <summary>
/// Coverage for the best-effort sign-in/out / token / lockout event
/// writer. Important behaviours: the row carries the supplied
/// fields, IP + UA enrich from <see cref="HttpContext"/>, oversized
/// fields truncate, and a SaveChanges failure logs but does not
/// propagate (audit-write failure must never abort an auth flow).
/// </summary>
public class AuthEventLoggerTests
{
    private static ApplicationDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"auth-event-{name}-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(opts);
    }

    private sealed class StubAccessor(HttpContext? ctx) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => ctx; set => throw new NotSupportedException(); }
    }

    private static AuthEventLogger NewSut(ApplicationDbContext db, HttpContext? ctx = null) =>
        new(db, new StubAccessor(ctx), NullLogger<AuthEventLogger>.Instance);

    [Fact]
    public async Task LogAsync_PersistsRowWithSuppliedFields()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        await sut.LogAsync(
            eventType:  "SignInSucceeded",
            username:   "mike.king",
            identityId: "abc-123",
            detail:     "via password");

        var row = await db.AuthEventLogs.AsNoTracking().SingleAsync();
        Assert.Equal("SignInSucceeded", row.EventType);
        Assert.Equal("mike.king",        row.Username);
        Assert.Equal("abc-123",          row.IdentityId);
        Assert.Equal("via password",     row.Detail);
        Assert.True(row.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task LogAsync_EnrichesIpAndUserAgentFromHttpContext()
    {
        await using var db = NewDb();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        http.Request.Headers.UserAgent = "TestAgent/1.0";

        var sut = NewSut(db, http);

        await sut.LogAsync("SignInFailed", "alice");

        var row = await db.AuthEventLogs.AsNoTracking().SingleAsync();
        Assert.Equal("203.0.113.7",  row.IpAddress);
        Assert.Equal("TestAgent/1.0", row.UserAgent);
    }

    [Fact]
    public async Task LogAsync_NoHttpContext_LeavesIpAndUserAgentNull()
    {
        await using var db = NewDb();
        // No HttpContext (e.g. background worker).
        var sut = NewSut(db, ctx: null);

        await sut.LogAsync("TokenIssued", "alice");

        var row = await db.AuthEventLogs.AsNoTracking().SingleAsync();
        Assert.Null(row.IpAddress);
        Assert.Null(row.UserAgent);
    }

    [Fact]
    public async Task LogAsync_OversizedUserAgent_TruncatesTo500()
    {
        await using var db = NewDb();
        var http = new DefaultHttpContext();
        http.Request.Headers.UserAgent = new string('x', 800);   // > 500
        var sut = NewSut(db, http);

        await sut.LogAsync("SignInSucceeded", "alice");

        var row = await db.AuthEventLogs.AsNoTracking().SingleAsync();
        Assert.NotNull(row.UserAgent);
        Assert.Equal(500, row.UserAgent!.Length);
    }

    [Fact]
    public async Task LogAsync_OversizedUsername_TruncatesTo256()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        await sut.LogAsync("SignInSucceeded", new string('u', 400));

        var row = await db.AuthEventLogs.AsNoTracking().SingleAsync();
        Assert.Equal(256, row.Username.Length);
    }

    [Fact]
    public async Task LogAsync_DbWriteFails_SwallowsExceptionAndLogs()
    {
        // Disposed context throws on SaveChangesAsync; the logger
        // catches and logs without propagating.
        var db = NewDb();
        await db.DisposeAsync();
        var sut = NewSut(db);

        // Must not throw.
        await sut.LogAsync("SignInFailed", "alice");
    }
}

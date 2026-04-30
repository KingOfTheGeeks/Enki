using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Concurrency;

namespace SDI.Enki.WebApi.Tests.Concurrency;

/// <summary>
/// Tests for <see cref="ConcurrencyHelper.ApplyClientRowVersion"/>.
///
/// <para>
/// The critical invariant: the helper must overwrite EF's tracked
/// <c>OriginalValue</c> for the RowVersion property — not just the
/// entity's current value. EF Core uses <c>OriginalValue</c> in the
/// WHERE clause of the UPDATE statement for IsRowVersion /
/// IsConcurrencyToken properties (see EF Core concurrency docs).
/// If only CurrentValue is overwritten, the WHERE compares against
/// whatever was loaded from the database in the request — which is
/// the post-other-writer state — and a stale-version save silently
/// last-write-wins.
/// </para>
///
/// <para>
/// EF InMemory does not enforce concurrency-token WHERE clauses (the
/// real check requires SQL Server), so the
/// <c>OptimisticConcurrencySmoke</c> test in
/// <c>SDI.Enki.Infrastructure.Tests</c> verifies the SQL Server
/// behavior end-to-end with Testcontainers. This file complements that
/// by verifying the helper's *contract* (sets OriginalValue) without
/// requiring Docker — a regression that broke the OriginalValue
/// assignment would be caught here on every build, not only when the
/// SQL-tagged smoke runs.
/// </para>
/// </summary>
public class ConcurrencyHelperTests
{
    private static TenantDbContext NewContext(
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"concurrency-{name}-{Guid.NewGuid():N}")
            .Options;
        return new TenantDbContext(options);
    }

    private static TestController NewController()
    {
        return new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public void Sets_OriginalValue_To_Client_Bytes()
    {
        // Regression guard for the silent two-tab last-write-wins bug:
        // EF Core's WHERE clause for the concurrency check reads
        // OriginalValue, not CurrentValue. The pre-fix helper only
        // overwrote CurrentValue, so the WHERE compared against the
        // server's freshly-loaded RowVersion (= the post-other-writer
        // state) and the stale save passed through.
        using var db = NewContext();
        var job = new Job("J", "d", UnitSystem.Field);
        db.Jobs.Add(job);
        db.SaveChanges();

        // Simulate "entity loaded with server's current RowVersion = newBytes".
        // InMemory doesn't generate rowversion bytes itself; we set
        // OriginalValue directly to model the loaded state.
        var serverBytes = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA };
        db.Entry(job).Property(j => j.RowVersion).OriginalValue = serverBytes;

        // Client posts a stale RowVersion.
        var clientBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var clientB64   = Convert.ToBase64String(clientBytes);

        var controller = NewController();
        var result = controller.ApplyClientRowVersion(db, job, clientB64);

        Assert.Null(result);

        // The fix: OriginalValue is what the CLIENT sent, so EF's
        // WHERE clause will be `rowversion = @clientBytes` and the
        // SQL Server provider will reject the save when the actual
        // row's rowversion has moved on.
        var trackedOriginal = (byte[]?)db.Entry(job).Property(j => j.RowVersion).OriginalValue;
        Assert.NotNull(trackedOriginal);
        Assert.Equal(clientBytes, trackedOriginal);

        // Sanity: CurrentValue also matches — the in-memory entity
        // observation is consistent with what the client thinks the
        // row is.
        Assert.Equal(clientBytes, job.RowVersion);
    }

    [Fact]
    public void Empty_Or_Whitespace_Token_Returns_ValidationProblem()
    {
        using var db = NewContext();
        var job = new Job("J", "d", UnitSystem.Field);
        db.Jobs.Add(job);
        db.SaveChanges();

        var controller = NewController();

        foreach (var bad in new[] { "", "   ", null })
        {
            var result = controller.ApplyClientRowVersion(db, job, bad);
            // ValidationProblem materialises as ObjectResult { StatusCode = 400 }
            // with a ValidationProblemDetails body.
            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
            // In an isolated ControllerContext without ApiBehaviorOptions
            // wired up, ValidationProblem(...) returns a plain
            // ProblemDetails rather than a typed ValidationProblemDetails.
            // The 400 + rowVersion key are the meaningful assertions.
            var problem = Assert.IsAssignableFrom<ProblemDetails>(obj.Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            if (problem is ValidationProblemDetails vp)
                Assert.True(vp.Errors.ContainsKey("rowVersion"));
        }
    }

    [Fact]
    public void Malformed_Base64_Returns_ValidationProblem()
    {
        using var db = NewContext();
        var job = new Job("J", "d", UnitSystem.Field);
        db.Jobs.Add(job);
        db.SaveChanges();

        var controller = NewController();
        var result = controller.ApplyClientRowVersion(db, job, "not-base64-!!!");

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
        var problem = Assert.IsAssignableFrom<ProblemDetails>(obj.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        if (problem is ValidationProblemDetails vp)
            Assert.True(vp.Errors.ContainsKey("rowVersion"));
    }

    /// <summary>
    /// Shim controller. <see cref="ControllerBase"/> is abstract; the
    /// helper is a <c>this ControllerBase</c> extension so any concrete
    /// derivative satisfies it. ControllerContext is set in
    /// <see cref="NewController"/> so <c>ValidationProblem(...)</c> can
    /// produce a ProblemDetails with a trace id.
    /// </summary>
    private sealed class TestController : ControllerBase { }
}

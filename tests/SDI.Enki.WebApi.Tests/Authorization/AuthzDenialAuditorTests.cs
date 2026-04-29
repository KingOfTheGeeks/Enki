using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Authorization;

namespace SDI.Enki.WebApi.Tests.Authorization;

/// <summary>
/// Direct tests for <see cref="AuthzDenialAuditor"/>. The auditor is
/// the single piece of code the three policy handlers call when they
/// <c>Fail()</c>; it must produce a well-shaped <see cref="SDI.Enki.Core.Master.Audit.MasterAuditLog"/>
/// row that surfaces in the master audit feed under
/// <c>EntityType=AuthzDenial</c>.
/// </summary>
public class AuthzDenialAuditorTests
{
    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"authz-denial-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static AuthzDenialAuditor NewAuditor(EnkiMasterDbContext db) =>
        new(db, NullLogger<AuthzDenialAuditor>.Instance);

    [Fact]
    public async Task RecordAsync_WritesOneMasterAuditLogRow()
    {
        await using var db = NewDb();
        var sut = NewAuditor(db);

        await sut.RecordAsync(
            policy:     EnkiPolicies.CanAccessTenant,
            tenantCode: "ACME",
            actorSub:   "11111111-1111-1111-1111-111111111111",
            reason:     "NotAMember");

        var rows = await db.MasterAuditLogs.AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);

        Assert.Equal("AuthzDenial", row.EntityType);
        Assert.Equal("ACME",        row.EntityId);   // tenantCode lands in EntityId
        Assert.Equal("Denied",      row.Action);
        Assert.Equal("11111111-1111-1111-1111-111111111111", row.ChangedBy);
        Assert.Null(row.OldValues);
        Assert.NotNull(row.NewValues);
    }

    [Fact]
    public async Task RecordAsync_NullTenantCode_FallsBackToGlobalLabel()
    {
        // EnkiAdminOnly denials don't carry a tenant — the auditor stores
        // a sentinel so the audit feed has a non-empty EntityId.
        await using var db = NewDb();
        var sut = NewAuditor(db);

        await sut.RecordAsync(
            policy:     EnkiPolicies.EnkiAdminOnly,
            tenantCode: null,
            actorSub:   "actor-sub-here",
            reason:     "NotEnkiAdmin");

        var row = await db.MasterAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("(global)", row.EntityId);
    }

    [Fact]
    public async Task RecordAsync_DetailJson_ContainsPolicyAndReason()
    {
        await using var db = NewDb();
        var sut = NewAuditor(db);

        await sut.RecordAsync(
            policy:     EnkiPolicies.CanManageTenantMembers,
            tenantCode: "BAKKEN",
            actorSub:   "actor",
            reason:     "NotATenantAdmin");

        var row = await db.MasterAuditLogs.AsNoTracking().SingleAsync();

        // The detail JSON shape lets the master audit UI render the
        // policy + reason chips. Parse + assert rather than string-match
        // so a property-order shift in the serializer doesn't break tests.
        using var doc = JsonDocument.Parse(row.NewValues!);
        Assert.Equal(EnkiPolicies.CanManageTenantMembers,
                     doc.RootElement.GetProperty("policy").GetString());
        Assert.Equal("BAKKEN", doc.RootElement.GetProperty("tenantCode").GetString());
        Assert.Equal("NotATenantAdmin", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RecordAsync_TimestampIsRecent()
    {
        await using var db = NewDb();
        var sut = NewAuditor(db);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await sut.RecordAsync(
            policy:     EnkiPolicies.CanAccessTenant,
            tenantCode: "X",
            actorSub:   "a");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var row = await db.MasterAuditLogs.AsNoTracking().SingleAsync();
        Assert.InRange(row.ChangedAt, before, after);
    }

    [Fact]
    public async Task RecordAsync_DbFailure_DoesNotThrow()
    {
        // The auditor swallows write failures so an audit-DB issue never
        // turns a 403 into a 500. Disposing the context before the call
        // forces SaveChangesAsync to throw; the contract is "log + move on."
        var db = NewDb();
        await db.DisposeAsync();
        var sut = NewAuditor(db);

        // No exception expected.
        await sut.RecordAsync(
            policy:     EnkiPolicies.EnkiAdminOnly,
            tenantCode: null,
            actorSub:   "a",
            reason:     "NotEnkiAdmin");
    }
}

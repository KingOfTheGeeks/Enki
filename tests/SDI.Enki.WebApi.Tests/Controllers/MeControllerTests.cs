using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Identity;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Coverage for <see cref="MeController.Memberships"/> across the
/// admin / Tenant-user / Team-user branches.
/// </summary>
public class MeControllerTests
{
    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"me-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static MeController NewSut(EnkiMasterDbContext db, ClaimsPrincipal user)
    {
        return new MeController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user },
            },
        };
    }

    private static ClaimsPrincipal Principal(
        Guid sub,
        bool isAdmin = false,
        UserType? userType = null,
        Guid? tenantId = null)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("sub", sub.ToString("D")));
        if (isAdmin)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.EnkiAdminRole));
            identity.AddClaim(new Claim("role", AuthConstants.EnkiAdminRole));
        }
        if (userType is not null)
            identity.AddClaim(new Claim(AuthConstants.UserTypeClaim, userType.Name));
        if (tenantId is not null)
            identity.AddClaim(new Claim(AuthConstants.TenantIdClaim, tenantId.Value.ToString("D")));
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Memberships_AdminRole_ReturnsIsAdminTrueAndEmptyCodes()
    {
        await using var db = NewDb();
        var sut = NewSut(db, Principal(sub: Guid.NewGuid(), isAdmin: true));

        var dto = await sut.Memberships(CancellationToken.None);

        Assert.True(dto.IsAdmin);
        Assert.Empty(dto.TenantCodes);
    }

    [Fact]
    public async Task Memberships_TenantUser_ResolvesBoundTenantCode()
    {
        await using var db = NewDb();
        var tenant = new Tenant("PERMIAN", "Permian Crest Corp") { Status = TenantStatus.Active };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var sut = NewSut(db, Principal(
            sub:        Guid.NewGuid(),
            userType:   UserType.Tenant,
            tenantId:   tenant.Id));

        var dto = await sut.Memberships(CancellationToken.None);

        Assert.False(dto.IsAdmin);
        Assert.Equal(new[] { "PERMIAN" }, dto.TenantCodes);
    }

    [Fact]
    public async Task Memberships_TenantUser_BoundTenantNotFound_ReturnsEmpty()
    {
        await using var db = NewDb();
        var sut = NewSut(db, Principal(
            sub:        Guid.NewGuid(),
            userType:   UserType.Tenant,
            tenantId:   Guid.NewGuid()));   // no matching Tenant row

        var dto = await sut.Memberships(CancellationToken.None);

        Assert.False(dto.IsAdmin);
        Assert.Empty(dto.TenantCodes);
    }

    [Fact]
    public async Task Memberships_TenantUser_MissingTenantIdClaim_ReturnsEmpty()
    {
        await using var db = NewDb();
        // Tenant-type user but no tenant_id claim — fall-through path.
        var sut = NewSut(db, Principal(
            sub:        Guid.NewGuid(),
            userType:   UserType.Tenant));

        var dto = await sut.Memberships(CancellationToken.None);

        Assert.False(dto.IsAdmin);
        Assert.Empty(dto.TenantCodes);
    }

    [Fact]
    public async Task Memberships_TeamUser_ReturnsAllMembershipCodesOrdered()
    {
        await using var db = NewDb();
        var identityId = Guid.NewGuid();
        var user = new User("alice", identityId);
        db.Users.Add(user);

        var alpha = new Tenant("ALPHA", "Alpha Corp") { Status = TenantStatus.Active };
        var bravo = new Tenant("BRAVO", "Bravo Corp") { Status = TenantStatus.Active };
        var charlie = new Tenant("CHARLIE", "Charlie Corp") { Status = TenantStatus.Active };
        db.Tenants.AddRange(alpha, bravo, charlie);
        await db.SaveChangesAsync();

        // Add memberships out of alphabetical order; the endpoint
        // contract is to return them sorted.
        db.TenantUsers.AddRange(
            new TenantUser(charlie.Id, user.Id),
            new TenantUser(alpha.Id,   user.Id),
            new TenantUser(bravo.Id,   user.Id));
        await db.SaveChangesAsync();

        var sut = NewSut(db, Principal(sub: identityId));

        var dto = await sut.Memberships(CancellationToken.None);

        Assert.False(dto.IsAdmin);
        Assert.Equal(new[] { "ALPHA", "BRAVO", "CHARLIE" }, dto.TenantCodes);
    }

    [Fact]
    public async Task Memberships_TeamUser_NoMemberships_ReturnsEmpty()
    {
        await using var db = NewDb();
        // Sub claim is a valid Guid but no User / TenantUser rows exist.
        var sut = NewSut(db, Principal(sub: Guid.NewGuid()));

        var dto = await sut.Memberships(CancellationToken.None);

        Assert.False(dto.IsAdmin);
        Assert.Empty(dto.TenantCodes);
    }

    [Fact]
    public async Task Memberships_TeamUser_MissingSubClaim_ReturnsEmpty()
    {
        await using var db = NewDb();
        // Identity with no sub claim — Guid.TryParse("") falls through.
        var identity = new ClaimsIdentity("test");
        var sut = NewSut(db, new ClaimsPrincipal(identity));

        var dto = await sut.Memberships(CancellationToken.None);

        Assert.False(dto.IsAdmin);
        Assert.Empty(dto.TenantCodes);
    }
}

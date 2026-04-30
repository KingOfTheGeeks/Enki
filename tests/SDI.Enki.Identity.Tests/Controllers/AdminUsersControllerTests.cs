using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SDI.Enki.Identity.Controllers;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.Identity.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="AdminUsersController"/>.
/// Exercises the four mutation paths (SetAdminRole / Lock / Unlock /
/// ResetPassword), self-protection rules, and the audit-row side
/// effects each one writes to <see cref="IdentityAuditLog"/>.
///
/// <para>
/// <b>Setup:</b> Real ASP.NET Identity stack on top of an InMemory
/// <see cref="ApplicationDbContext"/>. Building the full
/// <see cref="UserManager{TUser}"/> via the DI graph is more
/// straightforward than faking the seven-dep constructor — and
/// integrates the password hasher / lockout / security-stamp
/// behaviour the controller actually relies on.
/// </para>
/// </summary>
public class AdminUsersControllerTests
{
    /// <summary>
    /// Spin up an Identity stack with InMemory-backed
    /// <see cref="ApplicationDbContext"/> and return the DbContext +
    /// UserManager. Per-test isolated so tests can run in parallel.
    /// </summary>
    private static (ApplicationDbContext db, UserManager<ApplicationUser> userMgr, ServiceProvider sp) NewIdentityStack(
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Data-protection is a transitive dependency of AddDefaultTokenProviders
        // (DataProtectorTokenProvider needs IDataProtectionProvider). Tests
        // use ephemeral keys; production wires persistent storage.
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(opt =>
            opt.UseInMemoryDatabase($"admin-users-{name}-{Guid.NewGuid():N}"));

        services.AddIdentityCore<ApplicationUser>(opt =>
        {
            // Loosen the policy in tests — production policy is the
            // ASP.NET default; here we just need any password that
            // SeedUserAsync can mint without ceremony.
            opt.Lockout.MaxFailedAccessAttempts = 3;
            opt.Password.RequireDigit           = false;
            opt.Password.RequireNonAlphanumeric = false;
            opt.Password.RequireUppercase       = false;
            opt.Password.RequireLowercase       = false;
            opt.Password.RequiredLength         = 4;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        // Default token providers (registered for free by AddIdentity but
        // not by AddIdentityCore) — needed for GeneratePasswordResetTokenAsync.
        .AddDefaultTokenProviders();

        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
        return (db, userMgr, sp);
    }

    private static AdminUsersController NewController(
        UserManager<ApplicationUser> userMgr,
        ApplicationDbContext db,
        ApplicationUser? caller = null)
    {
        var controller = new AdminUsersController(userMgr, db);

        // Bare HttpContext so [Authorize]-style framework hooks don't
        // throw; if the test wants a specific caller for self-protection
        // checks, populate the User principal with the caller's id.
        var http = new DefaultHttpContext();
        if (caller is not null)
        {
            var identity = new System.Security.Claims.ClaimsIdentity("test");
            // The controller calls UserManager.GetUserId(User), which
            // reads the configured ClaimType (NameIdentifier by default).
            identity.AddClaim(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, caller.Id));
            http.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static async Task<ApplicationUser> SeedUserAsync(
        UserManager<ApplicationUser> userMgr,
        string userName,
        bool isAdmin = false)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            Email    = $"{userName}@test.local",
            IsEnkiAdmin = isAdmin,
        };
        var result = await userMgr.CreateAsync(user, "pass");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    // ---------- SetAdminRole ----------

    [Fact]
    public async Task SetAdminRole_FlipsColumnAndWritesAuditRow()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", isAdmin: false);
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetAdminRole(target.Id,
                new SetAdminRoleDto(IsAdmin: true, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);

            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.True(refreshed!.IsEnkiAdmin);

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("ApplicationUser", auditRow.EntityType);
            Assert.Equal(target.Id, auditRow.EntityId);
            Assert.Equal("RoleGranted", auditRow.Action);
            Assert.Equal("IsEnkiAdmin", auditRow.ChangedColumns);
            Assert.Equal(caller.Id, auditRow.ChangedBy);
        }
    }

    [Fact]
    public async Task SetAdminRole_RevokesAndWritesRoleRevokedAuditRow()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", isAdmin: true);
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            await sut.SetAdminRole(target.Id,
                new SetAdminRoleDto(IsAdmin: false, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.False(refreshed!.IsEnkiAdmin);

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("RoleRevoked", auditRow.Action);
        }
    }

    [Fact]
    public async Task SetAdminRole_SameRole_IsNoOp_NoAuditRow()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", isAdmin: true);
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetAdminRole(target.Id,
                new SetAdminRoleDto(IsAdmin: true, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
            Assert.Empty(await db.IdentityAuditLogs.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task SetAdminRole_SelfDemotion_Returns409_NoAuditRow()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            // Self-demote: caller targets themselves with IsAdmin=false.
            var result = await sut.SetAdminRole(caller.Id,
                new SetAdminRoleDto(IsAdmin: false, ConcurrencyStamp: caller.ConcurrencyStamp),
                ct: default);

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);

            var refreshed = await userMgr.FindByIdAsync(caller.Id);
            Assert.True(refreshed!.IsEnkiAdmin);   // still admin
            Assert.Empty(await db.IdentityAuditLogs.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task SetAdminRole_UnknownUser_Returns404()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var sut = NewController(userMgr, db);

            var result = await sut.SetAdminRole("does-not-exist",
                new SetAdminRoleDto(IsAdmin: true, ConcurrencyStamp: "any"),
                ct: default);

            Assert.IsType<NotFoundResult>(result);
        }
    }

    // ---------- Lock / Unlock ----------

    [Fact]
    public async Task Lock_SetsLockoutAndWritesAuditRow()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.Lock(target.Id, new AdminUserActionDto(target.ConcurrencyStamp), ct: default);

            Assert.IsType<NoContentResult>(result);
            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.True(refreshed!.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50));

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("Locked", auditRow.Action);
            Assert.Equal(caller.Id, auditRow.ChangedBy);
        }
    }

    [Fact]
    public async Task Lock_Self_Returns409()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.Lock(caller.Id, new AdminUserActionDto(caller.ConcurrencyStamp), ct: default);

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
            Assert.Empty(await db.IdentityAuditLogs.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task Unlock_ClearsLockoutAndWritesAuditRow()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            // Pre-lock the user.
            await userMgr.SetLockoutEndDateAsync(target, DateTimeOffset.UtcNow.AddDays(1));
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.Unlock(target.Id, new AdminUserActionDto(target.ConcurrencyStamp), ct: default);

            Assert.IsType<NoContentResult>(result);
            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.Null(refreshed!.LockoutEnd);

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("Unlocked", auditRow.Action);
        }
    }

    // ---------- ResetPassword ----------

    [Fact]
    public async Task ResetPassword_ReturnsTempPasswordAndWritesAuditRow()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.ResetPassword(target.Id, new AdminUserActionDto(target.ConcurrencyStamp), ct: default);

            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<ResetPasswordResponseDto>(ok.Value);
            Assert.False(string.IsNullOrWhiteSpace(dto.TemporaryPassword));
            Assert.Equal(16, dto.TemporaryPassword.Length);

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("PasswordReset", auditRow.Action);
            Assert.Equal(caller.Id, auditRow.ChangedBy);
        }
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_Returns404()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var sut = NewController(userMgr, db);

            var result = await sut.ResetPassword("does-not-exist", new AdminUserActionDto("any"), ct: default);

            Assert.IsType<NotFoundResult>(result);
        }
    }
}

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
        ApplicationUser? caller = null,
        SDI.Enki.Identity.Configuration.SessionLifetimeOptions? sessionOpts = null)
    {
        var controller = new AdminUsersController(
            userMgr,
            db,
            Microsoft.Extensions.Options.Options.Create(
                sessionOpts ?? new SDI.Enki.Identity.Configuration.SessionLifetimeOptions()));

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
            // GetUserName(User) reads ClaimsIdentity.Name, which surfaces
            // the ClaimTypes.Name claim by default. Populated so audit
            // rows that record the actor's username (e.g. SetSessionLifetime
            // → SessionLifetimeUpdatedBy) get a real value rather than the
            // "system" fallback.
            if (!string.IsNullOrWhiteSpace(caller.UserName))
            {
                identity.AddClaim(new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Name, caller.UserName));
            }
            http.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static async Task<ApplicationUser> SeedUserAsync(
        UserManager<ApplicationUser> userMgr,
        string userName,
        bool isAdmin = false,
        SDI.Enki.Shared.Identity.UserType? userType = null,
        string? teamSubtype = "Office",
        Guid? tenantId = null)
    {
        var user = new ApplicationUser
        {
            UserName    = userName,
            Email       = $"{userName}@test.local",
            IsEnkiAdmin = isAdmin,
            UserType    = userType ?? SDI.Enki.Shared.Identity.UserType.Team,
            TeamSubtype = userType == SDI.Enki.Shared.Identity.UserType.Tenant ? null : teamSubtype,
            TenantId    = tenantId,
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

    // ---------- SetSessionLifetime ----------

    [Fact]
    public async Task SetSessionLifetime_AppliesValueAndRotatesStamp()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var stampBefore = target.SecurityStamp;
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetSessionLifetime(target.Id,
                new SetSessionLifetimeDto(SessionLifetimeMinutes: 480, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);

            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.Equal(480, refreshed!.SessionLifetimeMinutes);
            Assert.NotNull(refreshed.SessionLifetimeUpdatedAt);
            Assert.Equal(caller.UserName, refreshed.SessionLifetimeUpdatedBy);
            // Security-stamp rotation is the lever that invalidates any
            // refresh token issued under the old window — without it the
            // change wouldn't take effect until the prior token's natural
            // expiry. Pin it as a contract test.
            Assert.NotEqual(stampBefore, refreshed.SecurityStamp);

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("SessionLifetimeChanged", auditRow.Action);
            Assert.Equal("SessionLifetimeMinutes", auditRow.ChangedColumns);
            Assert.Equal(caller.Id, auditRow.ChangedBy);
            Assert.Contains("\"newMinutes\":480", auditRow.NewValues);
            Assert.Contains("\"previousMinutes\":null", auditRow.NewValues);
        }
    }

    [Fact]
    public async Task SetSessionLifetime_NullClearsTheOverride()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            target.SessionLifetimeMinutes = 525600;
            await userMgr.UpdateAsync(target);

            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetSessionLifetime(target.Id,
                new SetSessionLifetimeDto(SessionLifetimeMinutes: null, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);

            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.Null(refreshed!.SessionLifetimeMinutes);

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Contains("\"previousMinutes\":525600", auditRow.NewValues);
            Assert.Contains("\"newMinutes\":null", auditRow.NewValues);
        }
    }

    [Fact]
    public async Task SetSessionLifetime_RejectsValueAboveCeiling()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller,
                sessionOpts: new() { MaxRefreshTokenLifetimeMinutes = 1440 });

            var result = await sut.SetSessionLifetime(target.Id,
                new SetSessionLifetimeDto(SessionLifetimeMinutes: 525600, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<ObjectResult>(result);   // ValidationProblem
            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.Null(refreshed!.SessionLifetimeMinutes);
        }
    }

    [Fact]
    public async Task SetSessionLifetime_RejectsZeroOrNegative()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetSessionLifetime(target.Id,
                new SetSessionLifetimeDto(SessionLifetimeMinutes: 0, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<ObjectResult>(result);   // ValidationProblem
        }
    }

    [Fact]
    public async Task SetSessionLifetime_IdempotentNoOpWritesNoAudit()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            target.SessionLifetimeMinutes = 1440;
            await userMgr.UpdateAsync(target);

            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetSessionLifetime(target.Id,
                new SetSessionLifetimeDto(SessionLifetimeMinutes: 1440, ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
            // Idempotent: the column already matches, so we must not bloat
            // the audit log with a noise row.
            Assert.Equal(0, await db.IdentityAuditLogs.CountAsync());
        }
    }

    [Fact]
    public async Task SetSessionLifetime_UnknownUser_Returns404()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var sut = NewController(userMgr, db);

            var result = await sut.SetSessionLifetime("does-not-exist",
                new SetSessionLifetimeDto(SessionLifetimeMinutes: 480, ConcurrencyStamp: "any"),
                ct: default);

            Assert.IsType<NotFoundResult>(result);
        }
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_Team_user_succeeds_and_returns_temporary_password()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.Create(
                new SDI.Enki.Shared.Identity.CreateUserDto(
                    UserName:    "alice.field",
                    Email:       "alice@test.local",
                    FirstName:   "Alice",
                    LastName:    "Field",
                    UserType:    "Team",
                    TeamSubtype: "Field",
                    TenantId:    null),
                ct: default);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var dto     = Assert.IsType<SDI.Enki.Shared.Identity.CreateUserResponseDto>(created.Value);

            Assert.False(string.IsNullOrWhiteSpace(dto.TemporaryPassword));
            Assert.Equal(16, dto.TemporaryPassword.Length);

            var saved = await userMgr.FindByIdAsync(dto.Id);
            Assert.NotNull(saved);
            Assert.Equal("alice.field", saved!.UserName);
            Assert.Equal("alice@test.local", saved.Email);
            Assert.Equal("Team",  saved.UserType?.Name);
            Assert.Equal("Field", saved.TeamSubtype);
            Assert.Null(saved.TenantId);

            // Profile claims wired up so the detail endpoint resolves a friendly name.
            var claims = await userMgr.GetClaimsAsync(saved);
            Assert.Equal("Alice", claims.First(c => c.Type == "given_name").Value);
            Assert.Equal("Field", claims.First(c => c.Type == "family_name").Value);

            var auditRow = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("UserCreated", auditRow.Action);
        }
    }

    [Fact]
    public async Task Create_Tenant_user_with_tenant_id_succeeds()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);
            var tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            var result = await sut.Create(
                new SDI.Enki.Shared.Identity.CreateUserDto(
                    UserName:    "bob.acme",
                    Email:       "bob@acme.test",
                    FirstName:   "Bob",
                    LastName:    "Acme",
                    UserType:    "Tenant",
                    TeamSubtype: null,
                    TenantId:    tenantId),
                ct: default);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var dto     = Assert.IsType<SDI.Enki.Shared.Identity.CreateUserResponseDto>(created.Value);
            var saved   = await userMgr.FindByIdAsync(dto.Id);

            Assert.Equal("Tenant", saved!.UserType?.Name);
            Assert.Equal(tenantId, saved.TenantId);
            Assert.Null(saved.TeamSubtype);
        }
    }

    [Fact]
    public async Task Create_rejects_invalid_classification_combo()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            // Tenant with no TenantId — validator rejects.
            var result = await sut.Create(
                new SDI.Enki.Shared.Identity.CreateUserDto(
                    UserName:    "broken",
                    Email:       "broken@test.local",
                    FirstName:   null,
                    LastName:    null,
                    UserType:    "Tenant",
                    TeamSubtype: null,
                    TenantId:    null),
                ct: default);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.IsAssignableFrom<ValidationProblemDetails>(problem.Value);
        }
    }

    [Fact]
    public async Task Create_rejects_duplicate_username()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.Create(
                new SDI.Enki.Shared.Identity.CreateUserDto(
                    UserName:    "alice",
                    Email:       "alice2@test.local",
                    FirstName:   null,
                    LastName:    null,
                    UserType:    "Team",
                    TeamSubtype: "Office",
                    TenantId:    null),
                ct: default);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.IsAssignableFrom<ValidationProblemDetails>(problem.Value);
        }
    }

    [Fact]
    public async Task Create_rejects_duplicate_email()
    {
        // Email uniqueness — defense in depth on top of the unique
        // index. Pin that the field-keyed 400 fires before the DB
        // constraint, so the operator sees a clean error rather than
        // a SqlException.
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            await SeedUserAsync(userMgr, "alice");   // alice@test.local
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.Create(
                new SDI.Enki.Shared.Identity.CreateUserDto(
                    UserName:    "alice2",
                    Email:       "alice@test.local",   // dup of seeded user
                    FirstName:   null,
                    LastName:    null,
                    UserType:    "Team",
                    TeamSubtype: "Office",
                    TenantId:    null),
                ct: default);

            var problem = Assert.IsType<ObjectResult>(result);
            var vpd     = Assert.IsAssignableFrom<ValidationProblemDetails>(problem.Value);
            Assert.True(vpd.Errors.ContainsKey("Email"),
                "Email collision must surface as a field-keyed validation error.");
        }
    }

    [Fact]
    public async Task Update_rejects_changing_email_to_existing_one()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var alice  = await SeedUserAsync(userMgr, "alice");   // alice@test.local
            var bob    = await SeedUserAsync(userMgr, "bob");     // bob@test.local
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(bob.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         bob.UserName!,
                    Email:            "alice@test.local",   // collide with alice
                    FirstName:        null,
                    LastName:         null,
                    TeamSubtype:      "Office",
                    TenantId:         null,
                    ConcurrencyStamp: bob.ConcurrencyStamp),
                ct: default);

            var problem = Assert.IsType<ObjectResult>(result);
            var vpd     = Assert.IsAssignableFrom<ValidationProblemDetails>(problem.Value);
            Assert.True(vpd.Errors.ContainsKey("Email"));
        }
    }

    [Fact]
    public async Task Update_keeping_same_email_is_allowed()
    {
        // Self-collision shouldn't fire — the user already owns their
        // own email. Pins the existingByEmail.Id != user.Id branch.
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var alice  = await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(alice.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         "alice.renamed",
                    Email:            alice.Email!,   // same as before
                    FirstName:        "Alice",
                    LastName:         "Renamed",
                    TeamSubtype:      "Office",
                    TenantId:         null,
                    ConcurrencyStamp: alice.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
        }
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_renames_user_and_keeps_classification()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", teamSubtype: "Office");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(target.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         "alice.renamed",
                    Email:            "alice@test.local",
                    FirstName:        "Alice",
                    LastName:         "Renamed",
                    TeamSubtype:      "Office",
                    TenantId:         null,
                    ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.Equal("alice.renamed", refreshed!.UserName);
            Assert.Equal("ALICE.RENAMED", refreshed.NormalizedUserName);
        }
    }

    [Fact]
    public async Task Update_team_subtype_change_rotates_security_stamp()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", teamSubtype: "Field");
            var stampBefore = target.SecurityStamp;
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(target.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         target.UserName!,
                    Email:            target.Email!,
                    FirstName:        null,
                    LastName:         null,
                    TeamSubtype:      "Supervisor",
                    TenantId:         null,
                    ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.Equal("Supervisor", refreshed!.TeamSubtype);
            // Classification change → forced re-sign-in via stamp rotation.
            Assert.NotEqual(stampBefore, refreshed.SecurityStamp);
        }
    }

    [Fact]
    public async Task Update_rejects_clearing_subtype_on_team_user()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", teamSubtype: "Office");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(target.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         target.UserName!,
                    Email:            target.Email!,
                    FirstName:        null,
                    LastName:         null,
                    TeamSubtype:      null,    // invariant: Team requires subtype
                    TenantId:         null,
                    ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.IsAssignableFrom<ValidationProblemDetails>(problem.Value);
        }
    }

    [Fact]
    public async Task Update_rejects_setting_tenant_id_on_team_user()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", teamSubtype: "Office");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(target.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         target.UserName!,
                    Email:            target.Email!,
                    FirstName:        null,
                    LastName:         null,
                    TeamSubtype:      "Office",
                    TenantId:         Guid.NewGuid(),    // invariant: Team can't bind to a tenant
                    ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.IsAssignableFrom<ValidationProblemDetails>(problem.Value);
        }
    }

    [Fact]
    public async Task Update_moves_tenant_user_between_tenants()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var oldTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var newTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var target = await SeedUserAsync(userMgr, "bob",
                userType:   SDI.Enki.Shared.Identity.UserType.Tenant,
                teamSubtype: null,
                tenantId:    oldTenant);
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(target.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         target.UserName!,
                    Email:            target.Email!,
                    FirstName:        null,
                    LastName:         null,
                    TeamSubtype:      null,
                    TenantId:         newTenant,
                    ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
            var refreshed = await userMgr.FindByIdAsync(target.Id);
            Assert.Equal(newTenant, refreshed!.TenantId);
        }
    }

    [Fact]
    public async Task Update_idempotent_no_op_returns_204_with_no_audit()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice", teamSubtype: "Office");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update(target.Id,
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         target.UserName!,
                    Email:            target.Email!,
                    FirstName:        null,
                    LastName:         null,
                    TeamSubtype:      "Office",
                    TenantId:         null,
                    ConcurrencyStamp: target.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal(0, await db.IdentityAuditLogs.CountAsync());
        }
    }

    [Fact]
    public async Task Update_unknown_user_returns_404()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut    = NewController(userMgr, db, caller);

            var result = await sut.Update("does-not-exist",
                new SDI.Enki.Shared.Identity.UpdateUserDto(
                    UserName:         "x",
                    Email:            "x@test.local",
                    FirstName:        null,
                    LastName:         null,
                    TeamSubtype:      "Office",
                    TenantId:         null,
                    ConcurrencyStamp: "any"),
                ct: default);

            Assert.IsType<NotFoundResult>(result);
        }
    }

    [Fact]
    public async Task SetSessionLifetime_AllowsSelfEdit()
    {
        // Mike's whole motivation for #30 is "I want a longer session for
        // myself" — pin the controller's behaviour here so a future
        // self-protection rule (analogous to Lock / SetAdminRole) doesn't
        // accidentally block the canonical use case.
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var caller = await SeedUserAsync(userMgr, "mike", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetSessionLifetime(caller.Id,
                new SetSessionLifetimeDto(
                    SessionLifetimeMinutes: 525600,
                    ConcurrencyStamp:       caller.ConcurrencyStamp),
                ct: default);

            Assert.IsType<NoContentResult>(result);
            var refreshed = await userMgr.FindByIdAsync(caller.Id);
            Assert.Equal(525600, refreshed!.SessionLifetimeMinutes);
            Assert.Equal("mike", refreshed.SessionLifetimeUpdatedBy);
        }
    }

    [Fact]
    public async Task SetSessionLifetime_MissingConcurrencyStamp_ReturnsValidationProblem()
    {
        // Concurrency-stamp enforcement itself can't be tested against
        // EF InMemory (the provider ignores rowversion / token mismatches),
        // but the controller's pre-check that requires a non-empty stamp
        // CAN — pin that path here so a regression that drops the
        // ApplyClientConcurrencyStamp call surfaces in unit tests.
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var target = await SeedUserAsync(userMgr, "alice");
            var caller = await SeedUserAsync(userMgr, "admin1", isAdmin: true);
            var sut = NewController(userMgr, db, caller);

            var result = await sut.SetSessionLifetime(target.Id,
                new SetSessionLifetimeDto(
                    SessionLifetimeMinutes: 480,
                    ConcurrencyStamp:       null),
                ct: default);

            // ApplyClientConcurrencyStamp wraps the failure in
            // ValidationProblem(...). Without an injected
            // ProblemDetailsFactory the StatusCode is left null on the
            // ObjectResult and only the inner ValidationProblemDetails
            // carries the type — assert on the payload, not the wrapper
            // status code.
            var problem = Assert.IsType<ObjectResult>(result);
            Assert.IsAssignableFrom<ValidationProblemDetails>(problem.Value);
        }
    }
}

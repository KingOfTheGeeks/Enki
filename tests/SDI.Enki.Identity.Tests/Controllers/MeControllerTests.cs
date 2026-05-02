using System.Security.Claims;
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
/// Direct controller tests for the self-service endpoints on
/// <see cref="MeController"/>. The change-password path is covered
/// here; <c>GetPreferences</c> / <c>SetPreferences</c> have their
/// own happy-path coverage in the round-trip integration tests.
///
/// Same setup pattern as <see cref="AdminUsersControllerTests"/>:
/// real ASP.NET Identity stack, InMemory DbContext, per-test
/// isolation. The test policy (length=4, no class requirements) is
/// deliberately loose so we can mint passwords without ceremony;
/// the "policy violation" case overrides specific options.
/// </summary>
public class MeControllerTests
{
    private static (ApplicationDbContext db, UserManager<ApplicationUser> userMgr, ServiceProvider sp) NewIdentityStack(
        Action<IdentityOptions>? configure = null,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(opt =>
            opt.UseInMemoryDatabase($"me-{name}-{Guid.NewGuid():N}"));

        services.AddIdentityCore<ApplicationUser>(opt =>
        {
            opt.Password.RequireDigit           = false;
            opt.Password.RequireNonAlphanumeric = false;
            opt.Password.RequireUppercase       = false;
            opt.Password.RequireLowercase       = false;
            opt.Password.RequiredLength         = 4;
            configure?.Invoke(opt);
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
        return (db, userMgr, sp);
    }

    private static MeController NewController(
        UserManager<ApplicationUser> userMgr,
        ApplicationDbContext db,
        ApplicationUser? caller = null)
    {
        var controller = new MeController(userMgr, db);

        var http = new DefaultHttpContext();
        if (caller is not null)
        {
            // MeController.CurrentUser reads the "sub" claim specifically
            // — that's what OpenIddict puts on the bearer token. Match
            // that contract here rather than the NameIdentifier the admin
            // controller tests use.
            var identity = new ClaimsIdentity("test");
            identity.AddClaim(new Claim("sub", caller.Id));
            http.User = new ClaimsPrincipal(identity);
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static async Task<ApplicationUser> SeedUserAsync(
        UserManager<ApplicationUser> userMgr,
        string userName,
        string password = "pass")
    {
        var user = new ApplicationUser
        {
            UserName    = userName,
            Email       = $"{userName}@test.local",
            UserType    = UserType.Team,
            TeamSubtype = "Office",
        };
        var result = await userMgr.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    // ============================================================
    // ChangePassword — happy path
    // ============================================================

    [Fact]
    public async Task ChangePassword_HappyPath_ReturnsNoContentRotatesStampWritesAudit()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var user = await SeedUserAsync(userMgr, "alice", password: "tempXXXX");
            var stampBefore = (await userMgr.FindByIdAsync(user.Id))!.SecurityStamp;
            var sut = NewController(userMgr, db, user);

            var result = await sut.ChangePassword(
                new ChangePasswordDto(CurrentPassword: "tempXXXX", NewPassword: "newY"),
                ct: default);

            Assert.IsType<NoContentResult>(result);

            // Stamp rotates because ChangePasswordAsync calls
            // UpdateSecurityStampInternal before returning success.
            var reloaded = await userMgr.FindByIdAsync(user.Id);
            Assert.NotEqual(stampBefore, reloaded!.SecurityStamp);

            // The new password actually works.
            Assert.True(await userMgr.CheckPasswordAsync(reloaded, "newY"));
            Assert.False(await userMgr.CheckPasswordAsync(reloaded, "tempXXXX"));

            // Audit row records the operator, not "system".
            var audit = await db.IdentityAuditLogs.AsNoTracking().SingleAsync();
            Assert.Equal("PasswordChanged", audit.Action);
            Assert.Equal(user.Id, audit.EntityId);
            Assert.Equal(user.Id, audit.ChangedBy);
        }
    }

    // ============================================================
    // ChangePassword — error paths
    // ============================================================

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ReturnsValidationOnCurrentField()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var user = await SeedUserAsync(userMgr, "alice", password: "tempXXXX");
            var sut = NewController(userMgr, db, user);

            var result = await sut.ChangePassword(
                new ChangePasswordDto(CurrentPassword: "WRONG", NewPassword: "newY"),
                ct: default);

            var (problem, _) = AssertValidationProblem(result);
            Assert.True(problem.Errors.ContainsKey(nameof(ChangePasswordDto.CurrentPassword)));
            Assert.False(problem.Errors.ContainsKey(nameof(ChangePasswordDto.NewPassword)));
            Assert.Empty(await db.IdentityAuditLogs.ToListAsync());
        }
    }

    [Fact]
    public async Task ChangePassword_NewPasswordViolatesPolicy_ReturnsValidationOnNewField()
    {
        // Force a stricter policy than the default test stack so we have
        // something to violate without making the seed user unworkable.
        var (db, userMgr, sp) = NewIdentityStack(opts =>
        {
            opts.Password.RequiredLength = 8;
            opts.Password.RequireDigit   = true;
        });
        await using (sp)
        {
            // Seed with a password that ALREADY meets the strict policy
            // so SeedUserAsync's CreateAsync succeeds.
            var user = await SeedUserAsync(userMgr, "alice", password: "OldPass99");
            var sut = NewController(userMgr, db, user);

            // Try to change to one that's too short AND has no digit.
            var result = await sut.ChangePassword(
                new ChangePasswordDto(CurrentPassword: "OldPass99", NewPassword: "abc"),
                ct: default);

            var (problem, _) = AssertValidationProblem(result);
            Assert.True(problem.Errors.ContainsKey(nameof(ChangePasswordDto.NewPassword)));
            Assert.False(problem.Errors.ContainsKey(nameof(ChangePasswordDto.CurrentPassword)));
            Assert.Empty(await db.IdentityAuditLogs.ToListAsync());
        }
    }

    [Theory]
    [InlineData("", "newY")]
    [InlineData("tempXXXX", "")]
    [InlineData("", "")]
    public async Task ChangePassword_EmptyField_ReturnsValidationProblem(string current, string newPwd)
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var user = await SeedUserAsync(userMgr, "alice", password: "tempXXXX");
            var sut = NewController(userMgr, db, user);

            var result = await sut.ChangePassword(
                new ChangePasswordDto(CurrentPassword: current, NewPassword: newPwd),
                ct: default);

            var (problem, _) = AssertValidationProblem(result);
            if (string.IsNullOrEmpty(current))
                Assert.True(problem.Errors.ContainsKey(nameof(ChangePasswordDto.CurrentPassword)));
            if (string.IsNullOrEmpty(newPwd))
                Assert.True(problem.Errors.ContainsKey(nameof(ChangePasswordDto.NewPassword)));
            Assert.Empty(await db.IdentityAuditLogs.ToListAsync());
        }
    }

    [Fact]
    public async Task ChangePassword_NoSignedInUser_Returns401()
    {
        var (db, userMgr, sp) = NewIdentityStack();
        await using (sp)
        {
            var sut = NewController(userMgr, db, caller: null);

            var result = await sut.ChangePassword(
                new ChangePasswordDto(CurrentPassword: "x", NewPassword: "y"),
                ct: default);

            Assert.IsType<UnauthorizedResult>(result);
        }
    }

    // ---- helpers ----

    private static (ValidationProblemDetails problem, ObjectResult obj) AssertValidationProblem(IActionResult result)
    {
        // Skip the StatusCode check — the test Identity stack doesn't
        // register a ProblemDetailsFactory, so ObjectResult.StatusCode
        // ends up null. The shape of the value (ValidationProblemDetails
        // with field-keyed errors) is what we actually care about.
        var obj = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsAssignableFrom<ValidationProblemDetails>(obj.Value);
        return (problem, obj);
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Logging;
using OpenIddict.Abstractions;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Identity;
using Serilog;

// Enki Identity — ASP.NET Identity + OpenIddict authorization server.
// Issues OIDC auth codes + JWTs the WebApi validates. Login UI and the
// OIDC /connect/* endpoints land in Phase 5b; this pass wires the stack
// and seeds users + clients.

var builder = WebApplication.CreateBuilder(args);

// Serilog bootstrap — identical pattern across all Enki hosts.
builder.Host.UseSerilog((ctx, sp, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Enki.Identity")
    .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/enki-identity-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

// Dev-only: unmask the URLs / HTTP responses in IdentityModel errors.
if (builder.Environment.IsDevelopment())
    IdentityModelEventSource.ShowPII = true;

var identityConn = builder.Configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required (see appsettings.Development.json).");

builder.Services.AddDbContext<ApplicationDbContext>(opt =>
{
    opt.UseSqlServer(identityConn, sql =>
    {
        // Retry on transient SQL faults — same defensive default as
        // master DB. Six attempts × up to 10 s back-off so a cold dev
        // SQL Server doesn't flake startup.
        sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);
    });
    // Register OpenIddict's EF Core entity sets (applications, authorizations,
    // scopes, tokens). Required so the OpenIddict.EntityFrameworkCore stores
    // find their tables via this DbContext.
    opt.UseOpenIddict();
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Dev-friendly password rules; tighten for prod.
        options.Password.RequireDigit           = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength         = 8;
        options.Password.RequireUppercase       = true;
        options.Password.RequireLowercase       = true;

        options.User.RequireUniqueEmail = true;

        // Identity's default principal uses long URIs (ClaimTypes.NameIdentifier
        // etc.). OpenIddict expects the JWT-style short names — most importantly
        // 'sub' for the subject. Without these three mappings, SignInManager
        // .CreateUserPrincipalAsync produces a principal with no 'sub' claim
        // and OpenIddict throws "mandatory subject claim was missing" during
        // token issuance.
        options.ClaimsIdentity.UserIdClaimType   = OpenIddictConstants.Claims.Subject;
        options.ClaimsIdentity.UserNameClaimType = OpenIddictConstants.Claims.Name;
        options.ClaimsIdentity.RoleClaimType     = OpenIddictConstants.Claims.Role;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>();
    })
    .AddServer(options =>
    {
        // Standard OIDC endpoints.
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetUserInfoEndpointUris("connect/userinfo")
               .SetEndSessionEndpointUris("connect/logout");

        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow();

        // Scopes clients can request.
        options.RegisterScopes(
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Roles,
            AuthConstants.WebApiScope);

        // Dev certificates — replace with real signing/encryption certs
        // loaded from Windows Certificate Store or Key Vault for prod.
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        // Issue access tokens as plain signed JWTs (not encrypted reference
        // tokens). The encryption cert above still protects identity tokens
        // and authorization codes; only the access token is plaintext. This
        // lets the WebApi validate tokens via JWKS discovery on its own,
        // without needing introspection back to this server or shared certs.
        options.DisableAccessTokenEncryption();

        // ASP.NET Core integration — sit inside the request pipeline.
        var aspNet = options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserInfoEndpointPassthrough()
               .EnableEndSessionEndpointPassthrough();

        // Dev: OpenIddict enforces HTTPS on its endpoints by default and
        // rejects plain-http discovery / token requests with error ID2083
        // ("This server only accepts HTTPS requests."). We run everything
        // on http://localhost in dev, so disable that enforcement here.
        // Prod MUST keep this enabled — tokens cross the wire in clear.
        if (builder.Environment.IsDevelopment())
            aspNet.DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthorization(options =>
{
    // Authorization policy used by the admin controllers below
    // (/admin/users/*). Requires a bearer token with both the enki
    // scope (i.e. issued for the Blazor client requesting `enki`) and
    // the enki-admin role claim. Matches the WebApi side's
    // CanAccessTenant short-circuit.
    options.AddPolicy("EnkiAdmin", p =>
    {
        p.RequireAuthenticatedUser();
        p.RequireClaim(OpenIddictConstants.Claims.Private.Scope,
            AuthConstants.WebApiScope);
        p.RequireRole(AuthConstants.EnkiAdminRole);
    });
});

builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Wire RFC 7807 ProblemDetails for the admin / me JSON controllers.
// Without this, ControllerBase.Problem(...) still works but with a
// less rich body; AddProblemDetails populates extensions like
// traceId + the request's instance URI from the current HttpContext.
builder.Services.AddProblemDetails();

var app = builder.Build();

// Dev convenience: auto-apply EF migrations so a first-boot against a
// freshly-dropped DB lands in a working state without a manual
// `dotnet ef database update`. Prod applies migrations via the Migrator
// CLI (or the deploy pipeline) before the host starts, so this stays
// behind the Development gate.
await using (var scope = app.Services.CreateAsyncScope())
{
    if (app.Environment.IsDevelopment())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bootLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Enki.Identity.Migrate");

        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2714)
        {
            // 2714 = "There is already an object named 'X' in the
            // database". Recovery from a partial-migration state left
            // by a previous crashed startup. Dev-only — wipe and redo.
            bootLogger.LogWarning(
                "Identity DB has orphan tables — dropping and recreating from migrations. ({Message})",
                ex.Message);
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        // Idempotent seed of dev users + the OpenIddict client / scope.
        // Gated behind IsDevelopment alongside the migration auto-apply —
        // production stands up users + clients via an explicit deploy
        // step, not on host startup.
        await IdentitySeedData.SeedAsync(scope.ServiceProvider);
    }
}

// HTTPS redirect in prod only. In dev the Blazor OIDC client and WebApi
// validation both target Identity via http://localhost:5196/ — redirecting
// them to https causes OpenIddict to emit an 'issuer' claim from the https
// URL (mismatching WebApi's configured issuer) and also loses auth state
// across the redirect.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();   // serves wwwroot (Enki auth CSS, favicons)
app.MapRazorPages();
app.MapControllers();

app.Run();

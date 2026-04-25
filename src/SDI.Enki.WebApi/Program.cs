using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Infrastructure;
using SDI.Enki.Shared.Identity;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Infrastructure;
using SDI.Enki.WebApi.Multitenancy;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;

// Enki WebApi — REST + SignalR surface for the Blazor client and external callers.
//
// Endpoints fall into two families by URL shape:
//   /tenants              — master registry (list, detail, provision)
//   /tenants/{code}/...   — tenant-scoped operations; TenantRoutingMiddleware
//                           resolves {code} to a per-request TenantContext.
//
// All endpoints require a bearer token with scope=enki issued by the
// SDI.Enki.Identity OIDC server.

var builder = WebApplication.CreateBuilder(args);

// ---------- logging ----------
// Serilog as the concrete sink. Reads min-level / overrides from the
// "Serilog" config section; stacks JSON console + rolling daily files
// under ./logs with 14-day retention. FromLogContext pulls scopes set
// by the correlation middleware into each emitted event.
builder.Host.UseSerilog((ctx, sp, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Enki.WebApi")
    .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/enki-webapi-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

// ---------- configuration ----------
var masterConn = builder.Configuration.GetConnectionString("Master")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Master is required. Set it in appsettings.Development.json " +
        "or via the ConnectionStrings__Master environment variable.");

var identityIssuer = builder.Configuration["Identity:Issuer"]
    ?? throw new InvalidOperationException(
        "Identity:Issuer is required (URL of the Enki Identity server — see appsettings.Development.json).");

// ---------- services ----------
// seedSampleData = "is this a dev environment" — gates whether
// DevMasterSeeder runs at startup. The bootstrap demo tenant
// (TENANTTEST) gets demo Jobs; user-created tenants from the UI
// always come up empty regardless of this flag (the seed decision
// lives on ProvisionTenantRequest.SeedSampleData per-call).
builder.Services.AddEnkiInfrastructure(masterConn,
    seedSampleData: builder.Environment.IsDevelopment());
builder.Services.AddEnkiMultitenancy();

// Marduk's ISurveyCalculator + the ISurveyAutoCalculator wrapper are
// both registered inside AddEnkiInfrastructure now — Infrastructure
// owns the registration so the dev seeder can call the auto-calc
// after seeding a well, and the controllers in this host pick them
// up via the same container.

// Who's-making-this-request plumbing used by the audit interceptor in
// EnkiMasterDbContext. The HttpContext-backed impl wins over the
// SystemCurrentUser fallback registered by AddEnkiInfrastructure via the
// last-registration-wins rule.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// OpenIddict token validation — trusts the Identity server as the issuer
// and validates access tokens against it via the standard OIDC discovery +
// introspection / local-validation handshake. Unlike the Server component,
// Validation does not enforce transport security on incoming requests, so
// no DisableTransportSecurityRequirement() needed here — it accepts bearer
// tokens over http://localhost in dev out of the box.
builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(identityIssuer);
        options.UseSystemNetHttp();       // for remote discovery + introspection if ever used
        options.UseAspNetCore();
    });

builder.Services
    .AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

builder.Services.AddAuthorization(options =>
{
    // Every Enki endpoint requires a token with scope=enki. Role-based
    // refinement (TenantUser.Role Admin vs Contributor vs Viewer) lands in
    // a follow-up pass.
    options.AddPolicy(EnkiPolicies.EnkiApiScope, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(Claims.Private.Scope, AuthConstants.WebApiScope);
    });

    // Tenant-scoped endpoints (/tenants/{tenantCode}/...). Not applied to
    // the master-registry TenantsController; that stays on EnkiApiScope.
    options.AddPolicy(EnkiPolicies.CanAccessTenant, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(Claims.Private.Scope, AuthConstants.WebApiScope);
        policy.Requirements.Add(new CanAccessTenantRequirement());
    });

    // Tighter: tenant-Admin-or-system-admin only. Used by member
    // management endpoints (/tenants/{code}/members/...).
    options.AddPolicy(EnkiPolicies.CanManageTenantMembers, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(Claims.Private.Scope, AuthConstants.WebApiScope);
        policy.Requirements.Add(new CanManageTenantMembersRequirement());
    });

    // System-admin only — cross-tenant administrative endpoints (system
    // settings, future audit, etc). Tighter than CanAccessTenant: the
    // role must be present, no membership-as-fallback.
    //
    // Uses RequireAssertion (not RequireRole) because OpenIddict tokens
    // emit the role at claim type "role" — RequireRole would consult
    // ClaimsIdentity.RoleClaimType (default ClaimTypes.Role) and miss
    // the actual claim. Using HasEnkiAdminRole goes through the same
    // claim-vs-role helper the tenant-scoped handlers use.
    options.AddPolicy(EnkiPolicies.EnkiAdminOnly, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(Claims.Private.Scope, AuthConstants.WebApiScope);
        policy.RequireAssertion(ctx => ctx.User.HasEnkiAdminRole());
    });

    options.DefaultPolicy = options.GetPolicy(EnkiPolicies.EnkiApiScope)!;
});
builder.Services.AddScoped<IAuthorizationHandler, CanAccessTenantHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanManageTenantMembersHandler>();

// Global exception handler + ProblemDetails. Any unhandled exception or
// a thrown EnkiException subclass becomes a consistent RFC 7807 response;
// [ApiController] auto-converts non-success IActionResult returns to
// ProblemDetails bodies via AddProblemDetails.
builder.Services.AddExceptionHandler<EnkiExceptionHandler>();
builder.Services.AddProblemDetails();

// Health checks — simple liveness + DB connectivity probe. Mapped below.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SDI.Enki.Infrastructure.Data.EnkiMasterDbContext>(
        name: "master-db",
        tags: new[] { "ready" });

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Dev convenience: auto-apply master-DB migrations so a first boot after
// a clean reset lands in a working state without a manual `dotnet ef
// database update` against EnkiMasterDbContext. Prod applies
// migrations via the Migrator CLI before the host starts, so this stays
// behind the Development gate. Tenant DBs get migrated inside
// TenantProvisioningService.ProvisionAsync, which runs regardless of
// this block.
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var master = scope.ServiceProvider
        .GetRequiredService<SDI.Enki.Infrastructure.Data.EnkiMasterDbContext>();
    var bootLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Enki.WebApi.MasterMigrate");

    try
    {
        await master.Database.MigrateAsync();
    }
    catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2714)
    {
        // 2714 = "There is already an object named 'X' in the database".
        // Means the master DB is in a partial-migration state — tables
        // exist without a matching __EFMigrationsHistory entry, usually
        // because a previous startup crashed mid-migration. Recover by
        // dropping and recreating; safe because dev only and the master
        // DB is regenerated from seed data on every boot.
        bootLogger.LogWarning(
            "Master DB has orphan tables — dropping and recreating from migrations. ({Message})",
            ex.Message);
        await master.Database.EnsureDeletedAsync();
        await master.Database.MigrateAsync();
    }
}

// Dev-only auto-provision of the demo tenant (TENANTTEST) if it doesn't
// exist — gated by ProvisioningOptions.SeedSampleData inside the seeder,
// which is only set true when builder.Environment.IsDevelopment(). Safe
// to call unconditionally; it's idempotent and no-ops in prod.
await SDI.Enki.Infrastructure.Provisioning.DevMasterSeeder.SeedAsync(app.Services);

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// HTTPS redirect in prod only. In dev both Blazor and Identity run on http
// and HttpClient strips the Authorization header when it auto-follows a
// redirect — which causes bearer tokens to vanish silently between services.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseExceptionHandler();          // converts all exceptions to ProblemDetails
app.UseStatusCodePages();           // converts no-body 4xx/5xx responses too

app.UseMiddleware<RequestCorrelationMiddleware>();   // before auth so 401 responses get an id
app.UseSerilogRequestLogging();                       // one structured line per request

app.UseRouting();
app.UseAuthentication();      // establishes principal from bearer token
app.UseMiddleware<UserScopeMiddleware>();   // pushes UserId into log scope; needs an authenticated principal
app.UseAuthorization();       // applies [Authorize(...)] policies
app.UseTenantRouting();       // after auth so master lookups can be attributed to a user

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

/// <summary>
/// Marker so <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// can target the WebApi entry point. Top-level statement files compile to
/// an internal Program; this partial-class declaration exposes it for
/// testing without changing the build shape.
/// </summary>
public partial class Program;

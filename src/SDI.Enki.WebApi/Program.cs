using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Infrastructure;
using SDI.Enki.Shared.Configuration;
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
// Required-secrets validation. Fails loud at startup when a needed
// secret is missing in any non-Development environment. See
// docs/deploy.md § Secret staging.
RequiredSecretsValidator.Validate(
    builder.Configuration,
    builder.Environment,
    required:
    [
        new("ConnectionStrings:Master",
            "Master DB connection string."),
        new("Identity:Issuer",
            "URL of the Enki Identity server (used for OIDC bearer-token validation)."),
        new("Licensing:PrivateKeyPath",
            "Path to the RSA private-key PEM used to sign .lic files.",
            ProductionOnly: true),
    ]);

var masterConn = builder.Configuration.GetConnectionString("Master")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Master is required. Set it in appsettings.Development.json " +
        "or via the ConnectionStrings__Master environment variable.");

var identityIssuer = builder.Configuration["Identity:Issuer"]
    ?? throw new InvalidOperationException(
        "Identity:Issuer is required (URL of the Enki Identity server — see appsettings.Development.json).");

// ---------- services ----------
// seedSampleData = "is this a dev environment" — gates whether
// DevMasterSeeder runs at startup. The curated demo tenants
// (PERMIAN / BAKKEN / NORTHSEA / CARNARVON) get demo Jobs;
// user-created tenants from the UI always come up empty regardless
// of this flag (the seed decision lives on
// ProvisionTenantRequest.SeedSampleData per-call).
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

// Bridge for copying master Calibration rows into per-tenant snapshot
// rows when a Run gets a tool assigned. Scoped because it consumes
// the scoped EnkiMasterDbContext.
builder.Services.AddScoped<SDI.Enki.WebApi.Calibrations.CalibrationSnapshotService>();

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
    // Every Enki policy starts with the scope-+-auth precheck — if the
    // caller has no enki-scoped token they don't get in regardless of
    // role / subtype. Helper so each AddPolicy block doesn't repeat the
    // same two lines.
    static void RequireScopedAuth(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder p)
    {
        p.RequireAuthenticatedUser();
        p.RequireClaim(Claims.Private.Scope, AuthConstants.WebApiScope);
    }

    // ---- universal default ----
    options.AddPolicy(EnkiPolicies.EnkiApiScope, p => RequireScopedAuth(p));

    // ---- tenant-scoped policies ----

    options.AddPolicy(EnkiPolicies.CanAccessTenant, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(TenantScoped: true));
    });
    options.AddPolicy(EnkiPolicies.CanWriteTenantContent, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office, TenantScoped: true));
    });
    options.AddPolicy(EnkiPolicies.CanDeleteTenantContent, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office, TenantScoped: true));
    });
    options.AddPolicy(EnkiPolicies.CanManageTenantMembers, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor, TenantScoped: true));
    });

    // ---- master-scoped policies ----

    options.AddPolicy(EnkiPolicies.CanWriteMasterContent, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office));
    });
    options.AddPolicy(EnkiPolicies.CanDeleteMasterContent, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office));
    });
    options.AddPolicy(EnkiPolicies.CanManageMasterTools, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor));
    });
    options.AddPolicy(EnkiPolicies.CanProvisionTenants, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor));
    });
    options.AddPolicy(EnkiPolicies.CanManageTenantLifecycle, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor));
    });
    options.AddPolicy(EnkiPolicies.CanReadMasterRoster, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor));
    });
    options.AddPolicy(EnkiPolicies.CanManageLicensing, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(
            MinimumSubtype:    TeamSubtype.Supervisor,
            GrantingCapability: EnkiCapabilities.Licensing));
    });

    // ---- admin-only ----

    options.AddPolicy(EnkiPolicies.EnkiAdminOnly, p =>
    {
        RequireScopedAuth(p);
        p.Requirements.Add(new TeamAuthRequirement(RequireAdmin: true));
    });

    options.DefaultPolicy = options.GetPolicy(EnkiPolicies.EnkiApiScope)!;
});
// Single handler covers every TeamAuthRequirement-based policy.
builder.Services.AddScoped<IAuthorizationHandler, TeamAuthHandler>();
builder.Services.AddScoped<IAuthzDenialAuditor, AuthzDenialAuditor>();

// Global exception handler + ProblemDetails. Any unhandled exception or
// a thrown EnkiException subclass becomes a consistent RFC 7807 response;
// [ApiController] auto-converts non-success IActionResult returns to
// ProblemDetails bodies via AddProblemDetails.
builder.Services.AddExceptionHandler<EnkiExceptionHandler>();
builder.Services.AddProblemDetails();

// Health checks — split into liveness ("is the process alive?") and
// readiness ("is the process ready to serve traffic?"). The liveness
// probe must NOT depend on external services or it'll false-positive
// kill-restart the host when SQL Server has a transient hiccup. The
// readiness probe checks the master-DB connection — if it's down the
// load balancer should drain traffic, not cycle the pod.
builder.Services.AddHealthChecks()
    .AddCheck("self",
        () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("alive"),
        tags: new[] { "live" })
    .AddDbContextCheck<SDI.Enki.Infrastructure.Data.EnkiMasterDbContext>(
        name: "master-db",
        tags: new[] { "ready" });

// Audit retention — two daily sweeps. MasterAuditRetentionService
// prunes the master DB's MasterAuditLog; TenantAuditRetentionService
// fans out across every active tenant and prunes per-tenant AuditLog.
// Both share the AuditRetention config section. See
// AuditRetentionOptions for defaults (365 / 730 days).
builder.Services.Configure<SDI.Enki.WebApi.Background.AuditRetentionOptions>(
    builder.Configuration.GetSection(SDI.Enki.WebApi.Background.AuditRetentionOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SDI.Enki.WebApi.Background.MasterAuditRetentionService>();
builder.Services.AddHostedService<SDI.Enki.WebApi.Background.TenantAuditRetentionService>();

// OpenTelemetry — distributed tracing + metrics. Service identity
// + resource attributes set once on the resource builder; every span
// / metric inherits. Default exporter is the console writer (good
// enough for dev; deployment configs replace with OTLP). EF
// instrumentation rides on the SqlClient activity source — gives
// per-query timings without per-context boilerplate.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb
        .AddService(serviceName: "Enki.WebApi", serviceVersion: "0.1.0")
        .AddAttributes(new KeyValuePair<string, object>[]
        {
            new("deployment.environment", builder.Environment.EnvironmentName),
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(opts =>
        {
            // Health endpoints are noisy (probe spam) and uninteresting
            // for trace analysis — drop them at the source.
            opts.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()  // default: command text is NOT instrumented (PII safe)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddConsoleExporter());

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ---------- API versioning ----------
// Pragmatic baseline: declare v1.0 as the default and accept the
// version via either the standard `api-version` query string or an
// `X-Api-Version` header. AssumeDefaultVersionWhenUnspecified keeps
// every existing client (Blazor, browser, curl scripts) working
// without changes — they get v1.0 implicitly. New v2 endpoints land
// later by decorating individual controllers with [ApiVersion("2.0")]
// and the framework routes the call by version match.
//
// We deliberately don't move every existing route under a `/v1/`
// prefix in this pass — that's a wire-breaking change for the Blazor
// client and the test suite, and the value (forced explicit
// versioning) doesn't outweigh the disruption while v1 is the only
// shipping version. Adding the prefix becomes worthwhile when v2
// arrives; the infrastructure is ready for it.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;   // emits api-supported-versions / api-deprecated-versions response headers
    options.ApiVersionReader = Asp.Versioning.ApiVersionReader.Combine(
        new Asp.Versioning.QueryStringApiVersionReader("api-version"),
        new Asp.Versioning.HeaderApiVersionReader("X-Api-Version"));
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// ---------- request timeouts ----------
// Long-running endpoints (file import, force-recalculate, anti-
// collision scan) trip an action-level CancellationToken after the
// configured policy elapses. The handler's already-plumbed `ct` is
// the same token, so EF / Marduk / file-stream calls cancel
// cooperatively. Policy is opt-in per action via
// `[RequestTimeout("LongRunning")]`; everything else stays on the
// host's default (effectively no-timeout).
builder.Services.AddRequestTimeouts(options =>
{
    options.AddPolicy("LongRunning", new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(60),
    });
});

// ---------- rate limiting ----------
// Guards the cheap-to-call-but-expensive-to-handle endpoints —
// Provision (creates two databases, runs migrations, seeds data —
// 5–30 s per call) and Import (reads up to 20 MB, runs minimum-
// curvature on every survey row). A misbehaving client could
// otherwise queue dozens of these in seconds. Fixed-window, 5
// requests/minute, partitioned by user identity (falls back to
// remote IP for anonymous calls — though every Enki endpoint is
// authenticated, so the fallback is mostly belt-and-braces).
//
// 429 Too Many Requests is the framework's default rejection
// status; ProblemDetails surface added by AddProblemDetails.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("Expensive", httpContext =>
    {
        var partitionKey = httpContext.User?.Identity?.Name
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 5,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            });
    });
});

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

    // Per-table idempotent fleet seed: Tools + Calibrations from the
    // JSON files copied into Data/Seed/ at build time. No-ops once the
    // tables have rows, so re-runs are safe.
    await SDI.Enki.Infrastructure.Data.MasterDataSeeder.SeedAsync(master, bootLogger);
}

// Dev-only auto-provision of the curated demo tenants (PERMIAN /
// BAKKEN / NORTHSEA / CARNARVON) if they don't exist — gated by
// ProvisioningOptions.SeedSampleData inside the seeder, which is
// only set true when builder.Environment.IsDevelopment(). Safe to
// call unconditionally; it's idempotent and no-ops in prod.
//
// Note: tenant-DB migrations only run at provisioning time. Pre-
// customer dev policy is "schema change → -Reset" — drop every
// Enki_* DB and re-provision fresh. start-dev.ps1 -Reset does
// this in one command. There's no auto-tenant-migrate path on
// startup (the previous DevTenantMigrator was deliberately
// removed because it created more confusion than it prevented;
// schema changes during dev get squashed into a fresh Initial
// migration anyway, so re-provisioning is the cleaner answer).
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

// Defense-in-depth response headers. After exception handler so error
// responses also carry them. CSP intentionally omitted (covered in
// docs/deploy.md "Known gaps").
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"]        = "DENY";
    h["Referrer-Policy"]        = "strict-origin-when-cross-origin";
    h["X-XSS-Protection"]       = "0";
    await next();
});

app.UseMiddleware<RequestCorrelationMiddleware>();   // before auth so 401 responses get an id
app.UseSerilogRequestLogging();                       // one structured line per request

app.UseRouting();
app.UseAuthentication();      // establishes principal from bearer token
app.UseMiddleware<UserScopeMiddleware>();   // pushes UserId into log scope; needs an authenticated principal
app.UseAuthorization();       // applies [Authorize(...)] policies
app.UseTenantRouting();       // after auth so master lookups can be attributed to a user

// Request timeout middleware activates the per-action policy chosen
// by [RequestTimeout(...)] attributes. Sits late in the pipeline so
// auth + tenant routing aren't subject to it; only the handler body
// is. UseRouting → UseAuthentication → UseAuthorization →
// UseTenantRouting → UseRequestTimeouts is the documented order.
app.UseRequestTimeouts();

// Rate limiting activates the per-action policy chosen by
// [EnableRateLimiting(...)] attributes. Same placement reasoning
// as request timeouts — auth must run first so we can partition by
// user identity, but tenant routing happens before the handler so
// the rate-limit decision can land before any tenant DB work.
app.UseRateLimiter();

// Health check endpoints — split by purpose so orchestrators (k8s,
// load balancers, watchdogs) can pick the right shape:
//
//   /health       — full report; runs every check; useful for
//                   humans / dashboards.
//   /health/live  — liveness only; returns Healthy unless the
//                   process itself is broken. Must NOT depend on
//                   external services or a SQL transient hiccup
//                   would trigger a kill-restart loop.
//   /health/ready — readiness; runs the DB-connectivity check. If
//                   the DB is down a load balancer should drain
//                   traffic, not cycle the pod.
//
// All three are anonymous — orchestrator probes don't carry tokens.
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapControllers();

app.Run();

/// <summary>
/// Marker so <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// can target the WebApi entry point. Top-level statement files compile to
/// an internal Program; this partial-class declaration exposes it for
/// testing without changing the build shape.
/// </summary>
public partial class Program;

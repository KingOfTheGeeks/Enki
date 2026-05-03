using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SDI.Enki.Identity.Configuration;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Configuration;
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

// Required-secrets validation. Fails loud at startup when a needed
// secret is missing or a prohibited dev fallback is present in any
// non-Development environment. See docs/deploy.md § Secret staging.
RequiredSecretsValidator.Validate(
    builder.Configuration,
    builder.Environment,
    required:
    [
        new("ConnectionStrings:Identity",
            "Identity DB connection string."),
        new("Identity:SigningCertificate:Path",
            "Path to the OIDC signing/encryption PFX on disk.",
            ProductionOnly: true),
    ]);
// `Identity:Seed:BlazorClientSecret` and `Identity:Seed:DefaultUserPassword`
// were previously required / prohibited here. Both keys are now read
// only by the Migrator CLI's `bootstrap-environment` command — the
// Identity host never reads them, so neither belongs on this gate.

var identityConn = builder.Configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required (see appsettings.Development.json).");

// Bind SessionLifetimeOptions for DI — controllers + the claims factory
// read the ceiling/default values from here. Defaults in the class itself
// keep dev sane when the config section is absent.
builder.Services.Configure<SessionLifetimeOptions>(
    builder.Configuration.GetSection(SessionLifetimeOptions.SectionName));

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
    .AddDefaultTokenProviders()
    // Custom principal factory derives the role=enki-admin claim from
    // ApplicationUser.IsEnkiAdmin at sign-in. The column is the single
    // source of truth; the claim is never persisted. See
    // EnkiUserClaimsPrincipalFactory for the full rationale.
    .AddClaimsPrincipalFactory<EnkiUserClaimsPrincipalFactory>();

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

        // Signing + encryption certs.
        //
        // Dev uses OpenIddict's auto-generated development certificates —
        // ephemeral, regenerated on first run, fine for localhost.
        //
        // Production loads a PFX from the path configured at
        // Identity:SigningCertificate:Path (with an optional
        // Identity:SigningCertificate:Password). The same cert is used
        // for both signing and encryption — matches the dev shape and
        // keeps deploy + rotation to a single artefact. Split into two
        // keys later if compliance demands separate roles.
        //
        // Production deploys MUST stage the PFX + config keys before the
        // host boots; the throw below is intentional fail-loud.
        if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();
        }
        else
        {
            var pfxPath = builder.Configuration["Identity:SigningCertificate:Path"]
                ?? throw new InvalidOperationException(
                    "Identity:SigningCertificate:Path is required outside Development. " +
                    "Set it to the PFX file path in environment-specific config.");
            var pfxPassword = builder.Configuration["Identity:SigningCertificate:Password"];
            var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, pfxPassword);

            options.AddSigningCertificate(cert)
                   .AddEncryptionCertificate(cert);
        }

        // Issue access tokens as plain signed JWTs (not encrypted reference
        // tokens). The encryption cert above still protects identity tokens
        // and authorization codes; only the access token is plaintext. This
        // lets the WebApi validate tokens via JWKS discovery on its own,
        // without needing introspection back to this server or shared certs.
        options.DisableAccessTokenEncryption();

        // Token lifetimes — pulled from SessionLifetime config so a future
        // policy change is one appsetting away. Per-user overrides on
        // ApplicationUser.SessionLifetimeMinutes win over the default at
        // issuance time (see AuthorizationController.Exchange); the default
        // here is the floor for users without an override.
        var sessionLifetime = builder.Configuration
            .GetSection(SessionLifetimeOptions.SectionName)
            .Get<SessionLifetimeOptions>() ?? new SessionLifetimeOptions();
        options.SetAccessTokenLifetime (TimeSpan.FromMinutes(sessionLifetime.AccessTokenLifetimeMinutes));
        options.SetRefreshTokenLifetime(TimeSpan.FromMinutes(sessionLifetime.RefreshTokenLifetimeMinutes));

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
    // the enki-admin role claim. Mirrors the WebApi-side
    // EnkiAdminOnly policy — same admin-only audience, different
    // host's authentication infrastructure.
    //
    // The policy pins its own AuthenticationSchemes to the bearer
    // validator. Without this, when the [Authorize(Scheme=bearer)] sits
    // on a controller class and [Authorize(Policy=EnkiAdmin)] sits on
    // the action separately, the action-level attribute's policy
    // evaluator falls back to the host's default scheme (cookie) and
    // the role claim isn't found — admins get a 403 they shouldn't.
    // Pinning the scheme here makes the policy authoritative regardless
    // of how the [Authorize] attributes are split between class and
    // action.
    options.AddPolicy("EnkiAdmin", p =>
    {
        p.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        p.RequireAuthenticatedUser();
        p.RequireClaim(OpenIddictConstants.Claims.Private.Scope,
            AuthConstants.WebApiScope);
        // RequireRole consults the principal's RoleClaimType; the
        // OpenIddictValidation handler's identity uses
        // OpenIddictConstants.Claims.Role ("role"), which matches what
        // EnkiUserClaimsPrincipalFactory writes. The HasClaim fallback
        // covers schemes that surface the role under a different type.
        p.RequireAssertion(ctx =>
            ctx.User.IsInRole(AuthConstants.EnkiAdminRole)
            || ctx.User.HasClaim(OpenIddictConstants.Claims.Role, AuthConstants.EnkiAdminRole));
    });

    // Office-or-above OR system admin. Used as the policy gate on the
    // AdminUsersController actions whose action body discriminates by
    // the TARGET user's UserType (Office can manage Tenant-type users,
    // only admin can touch Team-type). Pre-check fails closed for
    // anyone below Office; the inner per-target helper enforces the
    // target-aware tightening. Same scheme-pinning rationale as
    // EnkiAdmin above.
    options.AddPolicy("EnkiAdminOrOffice", p =>
    {
        p.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        p.RequireAuthenticatedUser();
        p.RequireClaim(OpenIddictConstants.Claims.Private.Scope,
            AuthConstants.WebApiScope);
        p.RequireAssertion(ctx =>
        {
            if (ctx.User.IsInRole(AuthConstants.EnkiAdminRole) ||
                ctx.User.HasClaim(OpenIddictConstants.Claims.Role, AuthConstants.EnkiAdminRole))
                return true;
            // team_subtype claim must parse and meet the Office floor.
            var rawSubtype = ctx.User.FindFirst(AuthConstants.TeamSubtypeClaim)?.Value;
            return TeamSubtype.TryFromName(rawSubtype, out var subtype)
                   && subtype.Value >= TeamSubtype.Office.Value;
        });
    });
});

// Rate limit the OIDC /connect/* endpoints. Login lockout (Identity's
// default, opted-in by Login.cshtml.cs) covers per-account brute force;
// this covers the per-IP credential-stuffing / token-refresh-spam vector
// that lockout doesn't address.
//
// Partitioned by RemoteIpAddress because /connect/token is initially
// unauthenticated (no User identity to partition on). When the host runs
// behind a reverse proxy the prod deploy must wire ForwardedHeaders
// middleware so RemoteIpAddress reflects the real client and not the
// proxy — otherwise every request collapses into one partition.
//
// 30 req/min/IP: one full interactive sign-in flow costs ~3 hits
// (/connect/authorize twice — pre- and post-cookie — plus
// /connect/token), so an iterating dev easily hits 10/min/IP after
// ~3 cycles. 30 still throttles credential-stuffing / refresh-spam
// (the original goal) while leaving headroom for legitimate
// click-through testing. Once we have a real customer footprint
// the per-IP shape can be revisited (per-user / per-client_id).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // OAuth 2.0 error responses on the token endpoint MUST be JSON
    // (RFC 6749 §5.2). The default empty-body 429 makes downstream
    // OIDC clients explode on response parsing — see
    // OAuthRateLimitedResponse for the full rationale (issue #24).
    options.OnRejected = SDI.Enki.Identity.RateLimiting.OAuthRateLimitedResponse.WriteAsync;

    options.AddPolicy("ConnectEndpoints", ctx =>
    {
        var partitionKey = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 30,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            });
    });
});

builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Wire RFC 7807 ProblemDetails for the admin / me JSON controllers.
// Without this, ControllerBase.Problem(...) still works but with a
// less rich body; AddProblemDetails populates extensions like
// traceId + the request's instance URI from the current HttpContext.
builder.Services.AddProblemDetails();

// Auth-event logging — sign-in / sign-out / token-issuance / lockout
// rows in AuthEventLog. AuthEventLogger pulls IP + user-agent from
// the current HttpContext; the accessor is required for that to
// resolve outside an MVC pipeline (the Razor Page sign-in path
// already exposes HttpContext but the accessor keeps DI uniform).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SDI.Enki.Identity.Auditing.IAuthEventLogger,
                           SDI.Enki.Identity.Auditing.AuthEventLogger>();

// IdentityBootstrapper drives both the Dev-rig seed (via the
// IdentitySeedData shim) and the Migrator's bootstrap-environment
// command. Registering scoped here lets the dev-only startup gate
// resolve it via the host's IServiceProvider.
builder.Services.AddScoped<SDI.Enki.Identity.Bootstrap.IdentityBootstrapper>();

// Health checks — same shape as the WebApi host. /health/live is a pure
// process-up signal (no dependencies); /health/ready exercises the
// Identity-DB connection so blue-green / load-balancer probes can drain
// traffic on a real outage without cycling the host on a transient hiccup.
// Both endpoints are anonymous — orchestrator probes don't carry tokens.
builder.Services.AddHealthChecks()
    .AddCheck("self",
        () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("alive"),
        tags: new[] { "live" })
    .AddDbContextCheck<ApplicationDbContext>(
        name: "identity-db",
        tags: new[] { "ready" });

// Audit retention — daily background sweep that prunes
// AuthEventLog + IdentityAuditLog rows older than the configured
// windows. See AuditRetentionOptions for defaults (90 / 365 days).
// Tunable via the AuditRetention section in appsettings; setting
// any *Days value to 0 disables that table's prune.
builder.Services.Configure<SDI.Enki.Identity.Background.AuditRetentionOptions>(
    builder.Configuration.GetSection(SDI.Enki.Identity.Background.AuditRetentionOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SDI.Enki.Identity.Background.AuditRetentionService>();

// OpenTelemetry — distributed tracing + metrics. Mirrors the WebApi
// host's setup so a request that crosses Blazor → Identity → WebApi
// shows up as one stitched trace via W3C TraceContext propagation
// (HttpClient instrumentation handles the header for us).
//
// Default exporter is the console writer (good enough for dev). When
// OpenTelemetry:Otlp:Endpoint is set in config, traces export there
// instead — wire OTLP via the Console exporter swap below if needed.
// Health endpoints are filtered out so probe spam doesn't drown the
// real traces.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb
        .AddService(serviceName: "Enki.Identity", serviceVersion: "0.1.0")
        .AddAttributes(new KeyValuePair<string, object>[]
        {
            new("deployment.environment", builder.Environment.EnvironmentName),
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(opts =>
        {
            opts.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()  // default: command text NOT instrumented (PII safe)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

// Migrations + seed are owned by the Migrator CLI in every
// environment, including Development (start-dev.ps1 -Reset runs
// `Enki.Migrator bootstrap-environment` before launching this host).
// The host expects a fully-staged DB and crashes with a clear EF
// error otherwise — see docs/plan-migrator-bootstrap.md for the
// rationale.

// HTTPS redirect in prod only. In dev the Blazor OIDC client and WebApi
// validation both target Identity via http://localhost:5196/ — redirecting
// them to https causes OpenIddict to emit an 'issuer' claim from the https
// URL (mismatching WebApi's configured issuer) and also loses auth state
// across the redirect.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

// Defense-in-depth response headers. Sits before auth so every response
// (including 401/403/404) carries them. CSP is intentionally not set —
// the Razor login pages + OpenIddict's authorization-endpoint UI use
// inline styles/scripts; a useful policy needs nonces and is deferred
// until a WAF / reverse proxy lands. See docs/deploy.md "Known gaps".
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"]        = "DENY";
    h["Referrer-Policy"]        = "strict-origin-when-cross-origin";
    h["X-XSS-Protection"]       = "0";   // explicit opt-out — modern browsers honour CSP instead
    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Rate limiter sits after auth so policies can partition by user when
// they want to; ConnectEndpoints partitions by IP so it doesn't matter
// here, but the order matches the WebApi host for consistency.
app.UseRateLimiter();

app.MapStaticAssets();   // serves wwwroot (Enki auth CSS, favicons)
app.MapRazorPages();
app.MapControllers();

// Health endpoints. Anonymous; no auth required. /health is a roll-up
// of every check (live + ready); the split endpoints are what
// orchestrators use for liveness vs readiness probes.
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();

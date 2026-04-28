using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.BlazorServer.Components;
using Serilog;
using Syncfusion.Blazor;

var builder = WebApplication.CreateBuilder(args);

// Serilog bootstrap — identical pattern across all Enki hosts.
builder.Host.UseSerilog((ctx, sp, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Enki.BlazorServer")
    .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/enki-blazor-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

// Dev-only: unmask the URLs / HTTP responses in IdentityModel errors so
// OIDC discovery failures actually tell you what happened instead of
// '[PII of type ... is hidden]'.
if (builder.Environment.IsDevelopment())
    IdentityModelEventSource.ShowPII = true;

// Syncfusion licensing — the key lives in configuration so it never lands
// in git. Paste your SDI key into appsettings.Development.json (or set
// SYNCFUSION_LICENSEKEY env var). Without a key, Syncfusion renders a
// licensing banner on the page; with a key, silent.

    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JHaF5cWWdCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXtcdXVSR2FYUkBxV0RWYEo=");

// ---------- configuration ----------
var authority      = builder.Configuration["Identity:Authority"]
    ?? throw new InvalidOperationException("Identity:Authority is required.");
var clientId       = builder.Configuration["Identity:ClientId"]     ?? "enki-blazor";
var clientSecret   = builder.Configuration["Identity:ClientSecret"] ?? "";
var webApiBase     = builder.Configuration["WebApi:BaseAddress"]
    ?? throw new InvalidOperationException("WebApi:BaseAddress is required.");

// ---------- services ----------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSyncfusionBlazor();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// OIDC authorization-code flow against the Enki Identity server. Access
// tokens are saved into the auth ticket (SaveTokens=true) so the
// BearerTokenHandler can forward them when we call the WebApi.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    options.DefaultSignOutScheme   = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name     = "Enki.BlazorAuth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddOpenIdConnect(options =>
{
    options.Authority  = authority;
    options.ClientId   = clientId;
    options.ClientSecret = clientSecret;

    options.ResponseType = "code";
    options.UsePkce      = true;
    options.SaveTokens   = true;
    options.GetClaimsFromUserInfoEndpoint = true;

    // dev only — Identity server runs on http at 5196
    options.RequireHttpsMetadata = false;

    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "name",
        RoleClaimType = "role",
    };

    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.Scope.Add("roles");          // brings role claims onto the Blazor cookie principal
    options.Scope.Add("enki");
    options.Scope.Add("offline_access");
});

builder.Services.AddAuthorization();

// Named HttpClients. BearerTokenHandler attaches the access_token from
// the current auth ticket on every outbound call.
//
//   EnkiApi      — the master + tenant data WebApi
//   EnkiIdentity — admin endpoints on the Identity host (e.g. /admin/users/*)
//                  served by AdminUsersController; tokens validate via
//                  Identity's local-server validation.
//
// CircuitTokenCache is scoped (one instance per Blazor circuit) so the
// auth-ticket round-trip happens only once per circuit instead of
// per-request. The handler reads from the cache and invalidates it on
// 401, so a stale or revoked token doesn't keep getting re-attached.
// See CircuitTokenCache.cs for the circuit-safety rationale.
builder.Services.AddScoped<CircuitTokenCache>();
builder.Services.AddTransient<BearerTokenHandler>();

builder.Services.AddHttpClient("EnkiApi", c =>
{
    c.BaseAddress = new Uri(webApiBase);
}).AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddHttpClient("EnkiIdentity", c =>
{
    c.BaseAddress = new Uri(authority);
}).AddHttpMessageHandler<BearerTokenHandler>();

// Plain (no-auth) client for the OIDC token endpoint. Used by
// CircuitTokenCache to swap a refresh_token for a fresh access_token
// when the cached one is close to expiring. Must NOT have the
// BearerTokenHandler attached — the refresh call uses
// client_credentials in the form body, not a Bearer header, and
// pulling our own (possibly-already-stale) access_token into the
// refresh request would defeat the purpose.
builder.Services.AddHttpClient("EnkiIdentityNoAuth", c =>
{
    c.BaseAddress = new Uri(authority);
});

var app = builder.Build();

// ---------- dev: wait for upstream hosts ----------
// In Development, F5 typically races the dependent hosts: Blazor binds
// 5073 immediately while Identity (5196) is still applying migrations
// and WebApi (5107) is still provisioning the demo tenants. The user clicks
// "Sign in" and gets a 500 because OIDC discovery 503'd, or hits a
// page that calls the WebApi and gets a connection refused.
//
// Polling both endpoints with a 60 s deadline gives them time to come
// up before Blazor accepts requests. When all three hosts are already
// running (e.g. start-dev.ps1 launched them in order), the probes
// pass on the first try and add ~50 ms of startup. When deps are
// missing, Blazor logs a warning and proceeds anyway — better than
// blocking forever.
if (app.Environment.IsDevelopment())
{
    await WaitForUpstreamAsync(app, authority, webApiBase);
}

// ---------- pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// ---------- auth endpoints ----------
// Blazor Server doesn't auto-generate login/logout routes. These kick the
// OIDC handler to challenge (redirects to Identity server) or sign out
// (clears cookie + hits /connect/logout).
app.MapGet("/account/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
        [OpenIdConnectDefaults.AuthenticationScheme]));

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [OpenIdConnectDefaults.AuthenticationScheme]);
});

// ---------- tenant action endpoints ----------
// The browser has only the Blazor auth cookie; it can't POST a bearer
// token directly to the WebApi. These endpoints run server-side, let the
// BearerTokenHandler attach the access token from the current HttpContext,
// and redirect the browser back to the detail page.
app.MapPost("/tenants/{code}/deactivate", async (
    string code,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var client = httpClientFactory.CreateClient("EnkiApi");
    using var resp = await client.PostAsync($"tenants/{code}/deactivate",
        content: null, ct);
    return resp.IsSuccessStatusCode
        ? Results.LocalRedirect($"/tenants/{code}")
        : Results.LocalRedirect(
            $"/tenants/{code}?statusError=Deactivate+failed+({(int)resp.StatusCode})");
}).RequireAuthorization();

app.MapPost("/tenants/{code}/reactivate", async (
    string code,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var client = httpClientFactory.CreateClient("EnkiApi");
    using var resp = await client.PostAsync($"tenants/{code}/reactivate",
        content: null, ct);
    return resp.IsSuccessStatusCode
        ? Results.LocalRedirect($"/tenants/{code}")
        : Results.LocalRedirect(
            $"/tenants/{code}?statusError=Reactivate+failed+({(int)resp.StatusCode})");
}).RequireAuthorization();

// Job lifecycle — one generic proxy for every status transition. The
// action segment (`activate`, `archive`, and anything added later like
// `complete`) is passed through to the WebApi verbatim, so adding a new
// lifecycle endpoint on the controller requires zero change here.
// Browser has only the auth cookie; this hop is where the BearerTokenHandler
// swaps it for an access token on the outbound call to the WebApi.
app.MapPost("/tenants/{code}/jobs/{jobId:guid}/{action:regex(^(activate|archive)$)}", async (
    string code,
    Guid jobId,
    string action,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var client = httpClientFactory.CreateClient("EnkiApi");
    using var resp = await client.PostAsync(
        $"tenants/{code}/jobs/{jobId}/{action}", content: null, ct);

    var capitalized = char.ToUpperInvariant(action[0]) + action[1..];
    return resp.IsSuccessStatusCode
        ? Results.LocalRedirect($"/tenants/{code}/jobs/{jobId}")
        : Results.LocalRedirect(
            $"/tenants/{code}/jobs/{jobId}?statusError={capitalized}+failed+({(int)resp.StatusCode})");
}).RequireAuthorization();

// Run lifecycle — same proxy shape as Job. RunDetail.razor renders one
// form per legal target via RunLifecycle.TargetsFor(), each with
// action="/tenants/{code}/jobs/{jobId}/runs/{runId}/{transitionAction}".
// Without this handler the form posts hit nothing and the buttons appear
// to "do nothing" — they just refresh the page silently. Whitelist the
// five action segments so a stray URL can't be coaxed into proxying an
// arbitrary endpoint.
app.MapPost("/tenants/{code}/jobs/{jobId:guid}/runs/{runId:guid}/{action:regex(^(start|suspend|complete|cancel|restore)$)}", async (
    string code,
    Guid jobId,
    Guid runId,
    string action,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var client = httpClientFactory.CreateClient("EnkiApi");
    using var resp = await client.PostAsync(
        $"tenants/{code}/jobs/{jobId}/runs/{runId}/{action}", content: null, ct);

    var capitalized = char.ToUpperInvariant(action[0]) + action[1..];
    return resp.IsSuccessStatusCode
        ? Results.LocalRedirect($"/tenants/{code}/jobs/{jobId}/runs/{runId}")
        : Results.LocalRedirect(
            $"/tenants/{code}/jobs/{jobId}/runs/{runId}?statusError={capitalized}+failed+({(int)resp.StatusCode})");
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// ---------- helpers ----------

/// <summary>
/// Polls Identity's OIDC discovery endpoint and the WebApi's
/// <c>/health/live</c> until both respond 2xx (or a 60 s deadline
/// elapses). Used in Development so a fresh F5 of the Blazor host
/// doesn't beat the upstream hosts to first-request.
///
/// <para>
/// Probes <c>/health/live</c> rather than the aggregate <c>/health</c>:
/// the live-check is a constant-Healthy self-check by design (no DB,
/// no external deps), and the 2 s HttpClient timeout below isn't
/// large enough to absorb the cold-start cost of EF's first
/// DbContextCheck against SQL Server. A 2 s probe-cancel manifests
/// as a server-side <c>TaskCanceledException</c> → 500, which then
/// makes the Blazor wait give up and start without the upstream
/// being truly ready. <c>/health/ready</c> remains the right probe
/// target for a load-balancer / readiness check, where the DB
/// dependency matters.
/// </para>
/// </summary>
static async Task WaitForUpstreamAsync(WebApplication app, string authority, string webApiBase)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

    var probes = new (string Name, Uri Url)[]
    {
        ("Identity", new Uri(new Uri(authority),  ".well-known/openid-configuration")),
        ("WebApi",   new Uri(new Uri(webApiBase), "health/live")),
    };

    foreach (var (name, url) in probes)
    {
        if (await IsReachable(probe, url))
        {
            logger.LogInformation("{Name} reachable at {Url}.", name, url);
            continue;
        }

        logger.LogInformation("Waiting for {Name} at {Url}...", name, url);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(500);
            if (await IsReachable(probe, url))
            {
                logger.LogInformation("{Name} is up.", name);
                break;
            }
        }

        if (DateTimeOffset.UtcNow >= deadline)
        {
            logger.LogWarning(
                "{Name} did not respond within 60 s — starting Blazor anyway. " +
                "Sign-in / API calls may fail until {Name} is reachable.", name, name);
            return;   // no point hammering the second probe if the first timed out
        }
    }
}

static async Task<bool> IsReachable(HttpClient client, Uri url)
{
    try
    {
        using var resp = await client.GetAsync(url);
        return resp.IsSuccessStatusCode;
    }
    catch
    {
        // Connection refused, DNS, timeout, etc. — keep polling.
        return false;
    }
}

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
var syncfusionKey = builder.Configuration["Syncfusion:LicenseKey"];
if (!string.IsNullOrWhiteSpace(syncfusionKey))
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);

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
    options.Scope.Add("enki");
    options.Scope.Add("offline_access");
});

builder.Services.AddAuthorization();

// Named HttpClient for the WebApi. BearerTokenHandler attaches the
// access_token from the current auth ticket on every request.
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient("EnkiApi", c =>
{
    c.BaseAddress = new Uri(webApiBase);
}).AddHttpMessageHandler<BearerTokenHandler>();

var app = builder.Build();

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Logging;
using OpenIddict.Abstractions;
using SDI.Enki.Identity.Data;

// Enki Identity — ASP.NET Identity + OpenIddict authorization server.
// Issues OIDC auth codes + JWTs the WebApi validates. Login UI and the
// OIDC /connect/* endpoints land in Phase 5b; this pass wires the stack
// and seeds users + clients.

var builder = WebApplication.CreateBuilder(args);

// Dev-only: unmask the URLs / HTTP responses in IdentityModel errors.
if (builder.Environment.IsDevelopment())
    IdentityModelEventSource.ShowPII = true;

var identityConn = builder.Configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Identity is required (see appsettings.Development.json).");

builder.Services.AddDbContext<ApplicationDbContext>(opt =>
{
    opt.UseSqlServer(identityConn);
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
            IdentitySeedData.WebApiScope);

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

builder.Services.AddRazorPages();
builder.Services.AddControllers();

var app = builder.Build();

// One-time idempotent seed of users + OpenIddict client / scope.
await using (var scope = app.Services.CreateAsyncScope())
{
    await IdentitySeedData.SeedAsync(scope.ServiceProvider);
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

app.MapRazorPages();
app.MapControllers();

app.Run();

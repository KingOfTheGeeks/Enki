using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SDI.Enki.Identity.Data;

// Enki Identity — ASP.NET Identity + OpenIddict authorization server.
// Issues OIDC auth codes + JWTs the WebApi validates. Login UI and the
// OIDC /connect/* endpoints land in Phase 5b; this pass wires the stack
// and seeds users + clients.

var builder = WebApplication.CreateBuilder(args);

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

        // ASP.NET Core integration — sit inside the request pipeline.
        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserInfoEndpointPassthrough()
               .EnableEndSessionEndpointPassthrough();
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

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();

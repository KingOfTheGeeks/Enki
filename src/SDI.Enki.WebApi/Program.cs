using AMR.Core.Survey.Implementations;
using AMR.Core.Survey.Services;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Infrastructure;
using SDI.Enki.WebApi.Multitenancy;
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

var masterConn = builder.Configuration.GetConnectionString("Master")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Master is required. Set it in appsettings.Development.json " +
        "or via the ConnectionStrings__Master environment variable.");

var identityIssuer = builder.Configuration["Identity:Issuer"]
    ?? throw new InvalidOperationException(
        "Identity:Issuer is required (URL of the Enki Identity server — see appsettings.Development.json).");

builder.Services.AddEnkiInfrastructure(masterConn);
builder.Services.AddEnkiMultitenancy();

// Marduk services (backend IP). Stateless — singleton is safe.
builder.Services.AddSingleton<ISurveyCalculator, MinimumCurvature>();

// OpenIddict token validation — trusts the Identity server as the issuer
// and validates access tokens against it via the standard OIDC discovery +
// introspection / local-validation handshake.
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
    options.AddPolicy("EnkiApiScope", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(Claims.Private.Scope, IdentitySeedConstants.WebApiScope);
    });

    options.DefaultPolicy = options.GetPolicy("EnkiApiScope")!;
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();      // establishes principal from bearer token
app.UseAuthorization();       // applies [Authorize(...)] policies
app.UseTenantRouting();       // after auth so master lookups can be attributed to a user
app.MapControllers();

app.Run();

/// <summary>
/// Constants duplicated here from <c>SDI.Enki.Identity.Data.IdentitySeedData</c>
/// to avoid WebApi taking a project reference on Identity just to read a
/// string. Keep in sync by hand — if this drifts, auth fails closed
/// (policy stops matching), which is the safe direction.
/// </summary>
internal static class IdentitySeedConstants
{
    public const string WebApiScope = "enki";
}

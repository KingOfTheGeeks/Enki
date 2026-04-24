using AMR.Core.Survey.Implementations;
using AMR.Core.Survey.Services;
using SDI.Enki.Infrastructure;
using SDI.Enki.WebApi.Multitenancy;

// Enki WebApi — REST + SignalR surface for the Blazor client and external callers.
//
// Endpoints fall into two families by URL shape:
//   /tenants              — master registry (list, detail, provision)
//   /tenants/{code}/...   — tenant-scoped operations; TenantRoutingMiddleware
//                           resolves {code} to a per-request TenantContext.

var builder = WebApplication.CreateBuilder(args);

var masterConn = builder.Configuration.GetConnectionString("Master")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Master is required. Set it in appsettings.Development.json " +
        "or via the ConnectionStrings__Master environment variable.");

builder.Services.AddEnkiInfrastructure(masterConn);
builder.Services.AddEnkiMultitenancy();

// Marduk services (backend IP). Stateless — singleton is safe.
builder.Services.AddSingleton<ISurveyCalculator, MinimumCurvature>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseRouting();
app.UseTenantRouting();   // must come AFTER UseRouting so RouteValues is populated
app.MapControllers();

app.Run();

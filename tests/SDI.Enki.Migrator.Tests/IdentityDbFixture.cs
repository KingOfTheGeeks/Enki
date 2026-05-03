using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.Identity.Bootstrap;
using SDI.Enki.Identity.Data;
using Testcontainers.MsSql;

namespace SDI.Enki.Migrator.Tests;

/// <summary>
/// Boots one SQL Server 2022 container per test class collection so
/// the bootstrapper tests don't each pay the ~20 s cold-start cost.
/// Each test asks for a fresh database name; the container hosts many
/// databases simultaneously without coordination.
///
/// <para>
/// Detects Docker availability at start-up and surfaces a skip reason
/// when unavailable — every test in the collection then skips rather
/// than red-fails. Mirrors the pattern in
/// <c>tests/SDI.Enki.Infrastructure.Tests/SqlServer/SchemaConstraintsSmoke.cs</c>.
/// </para>
/// </summary>
public sealed class IdentityDbFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool   DockerAvailable { get; private set; }
    public string SkipReason      { get; private set; } = "Docker not probed yet.";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder().Build();
            await _container.StartAsync();
            DockerAvailable = true;
        }
        catch (Exception ex)
        {
            DockerAvailable = false;
            SkipReason =
                $"SQL Server container could not start: {ex.GetType().Name}: {ex.Message}. " +
                "Start Docker Desktop (or run on a CI agent with Docker available) to exercise " +
                "the Migrator bootstrap tests.";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>
    /// Create a fresh empty database on the running container and
    /// return its connection string. Each test gets a unique DB name
    /// so cross-test pollution is impossible.
    /// </summary>
    public string CreateFreshIdentityDatabase()
    {
        if (_container is null || !DockerAvailable)
            throw new InvalidOperationException(
                "SQL Server container is unavailable; tests should Skip.IfNot(DockerAvailable, ...) first.");

        var dbName = $"BootstrapIT_{Guid.NewGuid():N}";

        var masterCs = _container.GetConnectionString();
        using (var conn = new SqlConnection(masterCs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            cmd.ExecuteNonQuery();
        }

        return new SqlConnectionStringBuilder(masterCs) { InitialCatalog = dbName }.ToString();
    }

    /// <summary>
    /// Build the DI graph the <see cref="IdentityBootstrapper"/> needs
    /// (DbContext + Identity stores + OpenIddict.Core stores) wired
    /// against the supplied Identity DB connection string. The caller
    /// is responsible for migrating the DB before invoking the
    /// bootstrapper.
    /// </summary>
    public ServiceProvider BuildIdentityServices(string identityConnectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<ApplicationDbContext>(opt =>
        {
            opt.UseSqlServer(identityConnectionString);
            opt.UseOpenIddict();
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit           = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength         = 8;
                options.Password.RequireUppercase       = true;
                options.Password.RequireLowercase       = true;
                options.User.RequireUniqueEmail         = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                       .UseDbContext<ApplicationDbContext>();
            });

        services.AddScoped<IdentityBootstrapper>();
        services.AddSingleton(NullLoggerFactory.Instance);

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Forces every Migrator-bootstrapper test to share the single
/// <see cref="IdentityDbFixture"/> and run non-parallel.
/// </summary>
[CollectionDefinition("Migrator Sql", DisableParallelization = true)]
public sealed class MigratorSqlCollection : ICollectionFixture<IdentityDbFixture> { }

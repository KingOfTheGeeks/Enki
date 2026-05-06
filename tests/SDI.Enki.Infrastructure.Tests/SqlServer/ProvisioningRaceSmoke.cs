using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Infrastructure.Surveys;

namespace SDI.Enki.Infrastructure.Tests.SqlServer;

/// <summary>
/// Smoke test for the <c>IX_Tenants_Code</c> race window inside
/// <see cref="TenantProvisioningService.ProvisionAsync"/>. The pre-check
/// (<c>EnsureCodeIsUniqueAsync</c>) catches sequential duplicates, but
/// two callers that both pass the pre-check before either commits will
/// race on the master-DB unique index. The catch around
/// <c>master.SaveChangesAsync</c> translates the resulting
/// <see cref="DbUpdateException"/> into a friendly
/// <see cref="TenantProvisioningException"/> (HTTP 400) so callers see
/// "Tenant code 'X' already exists." instead of the global handler's
/// generic 500.
///
/// <para>
/// The test deterministically forces the race using a
/// <see cref="ISaveChangesInterceptor"/> that pauses the service's
/// first <c>SaveChanges</c> until the test injects a duplicate row via a
/// side connection. EF then submits the original INSERT, SQL Server
/// rejects it with error 2601/2627, and the catch fires.
/// </para>
///
/// <para>
/// Same skip-on-no-Docker pattern + <c>Sql</c> trait as the other
/// fixture members so plain <c>dotnet test</c> stays fast.
/// </para>
/// </summary>
[Collection("Sql Server")]
[Trait("Category", "Sql")]
public class ProvisioningRaceSmoke : IClassFixture<SqlServerContainerFixture>
{
    private readonly SqlServerContainerFixture _fx;

    public ProvisioningRaceSmoke(SqlServerContainerFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ProvisionAsync_DuplicateCodeRace_TranslatesToTenantProvisioningException()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var masterCs = await _fx.CreateMasterDatabaseAsync();

        // Build the service under test against a master context wired
        // with the delay interceptor. The interceptor lets the pre-check
        // query through (it only intercepts SaveChanges) but pauses the
        // first save so we can inject a duplicate before SQL Server
        // sees the original INSERT.
        var delay = new DelayUntilSignaledInterceptor();
        await using var master = NewMaster(masterCs, delay);

        var options = new ProvisioningOptions(masterCs);
        var sut = new TenantProvisioningService(
            master,
            options,
            new DatabaseAdmin(options, NullLogger<DatabaseAdmin>.Instance),
            NoopAutoCalculator.Instance,
            NullLogger<TenantProvisioningService>.Instance);

        const string raceCode = "RACECODE";

        // Kick off the provision. The interceptor will block at the
        // first SaveChanges; ProvisionAsync stays awaiting until we
        // release the signal.
        var provisionTask = sut.ProvisionAsync(new ProvisionTenantRequest(raceCode, "Race Tenant"));

        // Wait until the interceptor reports it's parked at the save
        // call, then commit a duplicate Tenant row via a separate
        // context. EF stamps audit columns + RowVersion the same way
        // the production code does.
        await delay.AwaitingSignal;

        await using (var sibling = NewMaster(masterCs))
        {
            sibling.Tenants.Add(new Core.Master.Tenants.Tenant(raceCode, "Race Sibling"));
            await sibling.SaveChangesAsync();
        }

        // Release the original SaveChanges. EF's INSERT lands second
        // and trips the IX_Tenants_Code unique index → DbUpdateException
        // wrapping a SqlException with Number 2601 → catch translates.
        delay.Release();

        var ex = await Assert.ThrowsAsync<TenantProvisioningException>(() => provisionTask);
        Assert.Equal(400, ex.HttpStatusCode);
        Assert.Contains(raceCode, ex.Message);
        Assert.Contains("already exists", ex.Message);

        // Inner exception proves the catch fired (the pre-check throws
        // with no inner; only the new catch attaches the DbUpdateException).
        Assert.IsType<DbUpdateException>(ex.InnerException);
    }

    private static EnkiMasterDbContext NewMaster(string cs, ISaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<EnkiMasterDbContext>().UseSqlServer(cs);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new EnkiMasterDbContext(builder.Options);
    }

    /// <summary>
    /// Pauses the first <c>SaveChanges</c> on the attached context until
    /// the test calls <see cref="Release"/>. <see cref="AwaitingSignal"/>
    /// completes once the save reaches the pause so the test knows the
    /// pre-check has already cleared and it's safe to inject the
    /// duplicate row.
    /// </summary>
    private sealed class DelayUntilSignaledInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _awaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hits;

        public Task AwaitingSignal => _awaiting.Task;

        public void Release() => _signal.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken ct = default)
        {
            // Only delay the FIRST save — the audit pipeline runs a
            // second SaveChanges in Phase 2; that one must proceed
            // normally so the test doesn't deadlock waiting on it.
            if (Interlocked.Increment(ref _hits) == 1)
            {
                _awaiting.TrySetResult();
                await _signal.Task;
            }
            return result;
        }
    }

    private sealed class NoopAutoCalculator : ISurveyAutoCalculator
    {
        public static readonly NoopAutoCalculator Instance = new();
        public Task RecalculateAsync(TenantDbContext db, int wellId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.Tests.SqlServer;

/// <summary>
/// End-to-end smoke for the optimistic-concurrency wire pattern.
/// EF InMemory ignores <c>IsRowVersion</c> tokens — so the
/// <c>SDI.Enki.WebApi.Tests</c> suite can only verify the
/// <i>wire format</i> (DTO field present, base64 round-trip,
/// validation on missing/malformed). The real "stale RowVersion
/// surfaces as <see cref="DbUpdateConcurrencyException"/>" path
/// requires SQL Server actually enforcing the
/// <c>WHERE rowversion = @v</c> constraint, which only the SQL
/// Server provider does. This file exercises that path against a
/// Testcontainers-managed instance.
///
/// <para>
/// Conflict translation in the WebApi
/// (<c>ConcurrencyHelper.SaveOrConflictAsync</c> → 409 ProblemDetails)
/// is a thin try/catch around EF's exception, so verifying that EF
/// raises the exception when expected is the meaningful coverage —
/// any layer above is mechanical.
/// </para>
///
/// <para>
/// Same skip-on-no-Docker pattern as
/// <see cref="SchemaConstraintsSmoke"/>; same Sql category opt-in
/// for <c>dotnet test --filter Category=Sql</c>.
/// </para>
/// </summary>
[Collection("Sql Server")]
[Trait("Category", "Sql")]
public class OptimisticConcurrencySmoke : IClassFixture<SqlServerContainerFixture>
{
    private readonly SqlServerContainerFixture _fx;

    public OptimisticConcurrencySmoke(SqlServerContainerFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Survey_StaleRowVersion_RaisesConcurrencyException()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        // Seed a job + well + survey using a first context. The save
        // populates the rowversion column with whatever SQL Server
        // assigns; remember it as v1.
        Survey survey;
        byte[] v1;
        await using (var seed = _fx.CreateContext())
        {
            var job = new Job("ConcurrencyJob", "OC smoke", UnitSystem.Field);
            seed.Jobs.Add(job);
            await seed.SaveChangesAsync();

            var well = new Well(job.Id, "ConcurrencyWell", WellType.Target);
            seed.Wells.Add(well);
            await seed.SaveChangesAsync();

            survey = new Survey(well.Id, depth: 1000, inclination: 5, azimuth: 90);
            seed.Surveys.Add(survey);
            await seed.SaveChangesAsync();

            Assert.NotNull(survey.RowVersion);
            v1 = survey.RowVersion!;
        }

        // Open a fresh context and mutate the survey via a normal
        // load → modify → save flow. SaveChanges bumps rowversion
        // server-side from v1 → v2; the in-memory entity tracks v2.
        await using (var first = _fx.CreateContext())
        {
            var s = await first.Surveys.FirstAsync(x => x.Id == survey.Id);
            s.Inclination = 12;
            await first.SaveChangesAsync();
            Assert.NotEqual(v1, s.RowVersion);
        }

        // Open a SECOND fresh context and try to save with the stale
        // v1. Mirrors what the controller does when a client posts a
        // RowVersion that's now out-of-date: load the entity, apply
        // the client's RowVersion via ConcurrencyHelper, mutate,
        // SaveChanges. EF generates UPDATE ... WHERE rowversion = v1;
        // SQL Server matches 0 rows because the row is now at v2;
        // EF raises DbUpdateConcurrencyException — which the WebApi's
        // SaveOrConflictAsync catches and translates to 409.
        await using (var second = _fx.CreateContext())
        {
            var s = await second.Surveys.FirstAsync(x => x.Id == survey.Id);
            s.RowVersion = v1;     // stale — the helper would do this from a base64 client token
            s.Inclination = 99;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => second.SaveChangesAsync());
        }

        // Verify the second save did NOT win — the row still reflects
        // the first writer's edit (Inclination = 12), not the stale
        // writer's (Inclination = 99).
        await using (var verify = _fx.CreateContext())
        {
            var s = await verify.Surveys.AsNoTracking().FirstAsync(x => x.Id == survey.Id);
            Assert.Equal(12, s.Inclination);
        }
    }

    [SkippableFact]
    public async Task Survey_FreshRowVersion_AllowsSave()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        // Companion happy-path test: the same load → apply
        // RowVersion → save flow with a *current* rowversion succeeds
        // and bumps the rowversion. Confirms the 409 above isn't
        // false-positive on the helper itself — it's specifically
        // the staleness that triggers the exception.
        Survey survey;
        await using (var seed = _fx.CreateContext())
        {
            var job = new Job("FreshConcurrencyJob", "OC happy path", UnitSystem.Field);
            seed.Jobs.Add(job);
            await seed.SaveChangesAsync();

            var well = new Well(job.Id, "FreshConcurrencyWell", WellType.Target);
            seed.Wells.Add(well);
            await seed.SaveChangesAsync();

            survey = new Survey(well.Id, depth: 2000, inclination: 10, azimuth: 180);
            seed.Surveys.Add(survey);
            await seed.SaveChangesAsync();
        }

        var currentRv = survey.RowVersion!;

        await using (var ctx = _fx.CreateContext())
        {
            var s = await ctx.Surveys.FirstAsync(x => x.Id == survey.Id);
            s.RowVersion  = currentRv;     // matches DB → save proceeds
            s.Inclination = 33;

            await ctx.SaveChangesAsync();   // no exception
            Assert.NotEqual(currentRv, s.RowVersion);   // bumped on success
            Assert.Equal(33, s.Inclination);
        }
    }
}

using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Seeds a freshly-provisioned tenant's Active DB with one sample Job
/// so dev users clicking through the UI see *something* instead of an
/// empty grid. Intentionally minimal — add more rows here as the UI
/// surfaces mature and there's something real for each new sample to
/// demonstrate.
///
/// <para>
/// Gated on <see cref="Models.ProvisioningOptions.SeedSampleData"/>:
/// WebApi turns it on in Development, Migrator CLI + prod hosts leave
/// it off.
/// </para>
/// </summary>
public static class DevTenantSeeder
{
    public static async Task SeedAsync(TenantDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        db.Jobs.Add(new Job(
            name:        "Permian-22-14H",
            description: "Seed job — horizontal lateral pilot, ~10,000 ft MD.",
            unitSystem:  UnitSystem.Field)
        {
            Status         = JobStatus.Active,
            Region         = "Permian Basin",
            WellName       = "Johnson 1H",
            CreatedAt      = now,
            StartTimestamp = now,
            EndTimestamp   = now.AddMonths(3),
        });

        await db.SaveChangesAsync(ct);
    }
}

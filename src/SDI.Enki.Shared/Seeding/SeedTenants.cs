namespace SDI.Enki.Shared.Seeding;

/// <summary>
/// Stable <c>Tenant.Id</c> GUIDs for the demo tenants seeded by
/// <c>DevMasterSeeder</c>. Pinned so that other seeders (notably
/// <see cref="SeedUsers"/> for Tenant-type users) can reference the
/// IDs without a cross-host lookup at boot time.
///
/// <para>
/// Pairs with the <c>Code</c> the master seeder uses; the convention
/// is "constant name == tenant code" (so <see cref="Permian"/> ↔
/// "PERMIAN"). Tenant users seeded with <c>SeedUser.TenantId</c>
/// reference these.
/// </para>
///
/// <para>
/// <b>Don't reuse or rotate these GUIDs once shipped.</b> Any
/// seeded Tenant user is bound to one of these by GUID; rotating it
/// would orphan the binding and the user would fail authorization
/// at next sign-in. If a tenant code retires, leave the constant in
/// place (tagged <c>// Retired</c>) rather than deleting.
/// </para>
/// </summary>
public static class SeedTenants
{
    /// <summary>PERMIAN — Permian Crest Energy. Bootstrap tenant.</summary>
    public static readonly Guid Permian  = Guid.Parse("8c2a1e90-5fb9-4c0a-9bda-1c6c43a9e1a1");

    /// <summary>NORTHSEA — Brent Atlantic Drilling.</summary>
    public static readonly Guid NorthSea = Guid.Parse("4d9b8e1c-3217-4f0d-bc2e-b5c1d7e2c4a7");

    /// <summary>BOREAL — Boreal Athabasca Energy.</summary>
    public static readonly Guid Boreal   = Guid.Parse("9f8e3d2b-6a14-4d6e-8f3c-9b1d2c5a7e6f");

    /// <summary>
    /// Lookup helper for the master seeder — translates a known demo
    /// tenant code into its pinned ID. Throws on unknown codes so a
    /// typo at the seed site fails loud instead of silently falling
    /// back to a fresh GUID.
    /// </summary>
    public static Guid IdForCode(string code) => code switch
    {
        "PERMIAN"  => Permian,
        "NORTHSEA" => NorthSea,
        "BOREAL"   => Boreal,
        _ => throw new ArgumentException(
            $"No pinned Tenant ID for code '{code}'. Add it to SeedTenants.cs " +
            $"if it's a new demo tenant, or pass null TenantId for ad-hoc tenants " +
            $"created via the provisioning UI.",
            nameof(code)),
    };
}

namespace SDI.Enki.Shared.Seeding;

/// <summary>
/// Single source of truth for the SDI team-member roster used by
/// <c>IdentitySeedData</c> (Identity DB user creation) and
/// <c>MasterSeedData</c> (master DB User HasData + template
/// assignments). See <see cref="SeedUser"/> for the contract.
///
/// <para>
/// Don't add per-user named statics for users that may come and go —
/// only the ones referenced by template assignments / role grants
/// need a name handle. Everyone else is reachable via <see cref="All"/>
/// and the foreach iteration in the seeders.
/// </para>
/// </summary>
public static class SeedUsers
{
    public static readonly SeedUser DapoAjayi = new(
        IdentityId:   Guid.Parse("8cf4b730-c619-49d0-8ed7-be0ac89de718"),
        MasterUserId: Guid.Parse("7a519cae-da73-41df-82dd-05fbc8bc73a0"),
        Username:     "dapo.ajayi",
        Email:        "dapo.ajayi@scientificdrilling.com",
        FirstName:    "Dapo",
        LastName:     "Ajayi");

    public static readonly SeedUser JamieDorey = new(
        IdentityId:   Guid.Parse("f8aff5b3-473b-436f-9592-186cb28ac848"),
        MasterUserId: Guid.Parse("e8dd0c2a-bceb-4885-a90e-8f9cf446ee5a"),
        Username:     "jamie.dorey",
        Email:        "jamie.dorey@scientificdrilling.com",
        FirstName:    "Jamie",
        LastName:     "Dorey");

    public static readonly SeedUser AdamKarabasz = new(
        IdentityId:   Guid.Parse("dafd065f-4790-4235-9db0-6f47abadf3aa"),
        MasterUserId: Guid.Parse("ab3f526a-849b-492b-91d9-f3851e978869"),
        Username:     "adam.karabasz",
        Email:        "adam.karabasz@scientificdrilling.com",
        FirstName:    "Adam",
        LastName:     "Karabasz");

    public static readonly SeedUser DouglasRidgway = new(
        IdentityId:   Guid.Parse("bd34385d-2d88-4781-bef5-e955ddaa8293"),
        MasterUserId: Guid.Parse("02c9751a-3058-4e15-b5c5-ce82adaebaeb"),
        Username:     "douglas.ridgway",
        Email:        "douglas.ridgway@scientificdrilling.com",
        FirstName:    "Douglas",
        LastName:     "Ridgway");

    public static readonly SeedUser TravisSolomon = new(
        IdentityId:   Guid.Parse("e5a7f984-688a-4904-8155-3fe724584385"),
        MasterUserId: Guid.Parse("123505bf-cd91-4e15-b583-ad1291347508"),
        Username:     "travis.solomon",
        Email:        "travis.solomon@scientificdrilling.com",
        FirstName:    "Travis",
        LastName:     "Solomon");

    public static readonly SeedUser MikeKing = new(
        IdentityId:   Guid.Parse("1e333b45-1448-4b26-a68d-b4effbbdcd9d"),
        MasterUserId: Guid.Parse("f5fd1207-1dc6-49c7-a794-b5420bd88008"),
        Username:     "mike.king",
        Email:        "mike.king@scientificdrilling.com",
        FirstName:    "Mike",
        LastName:     "King",
        IsEnkiAdmin:  true);

    public static readonly SeedUser JamesPowell = new(
        IdentityId:   Guid.Parse("a72f07d8-9a12-4825-95f4-7c5bbea6e6e5"),
        MasterUserId: Guid.Parse("ce17bb43-1eac-439e-80a5-324a3edaf373"),
        Username:     "james.powell",
        Email:        "james.powell@scientificdrilling.com",
        FirstName:    "James",
        LastName:     "Powell");

    public static readonly SeedUser JoelHarrison = new(
        IdentityId:   Guid.Parse("f8d3ceda-ce98-4825-88f9-c8e8356a61db"),
        MasterUserId: Guid.Parse("e48bacc4-4375-4445-88b0-e08c20216513"),
        Username:     "joel.harrison",
        Email:        "joel.harrison@scientificdrilling.com",
        FirstName:    "Joel",
        LastName:     "Harrison");

    public static readonly SeedUser ScottBrandel = new(
        IdentityId:   Guid.Parse("bc120086-fc2d-4f41-b76a-3f6c3536c2cc"),
        MasterUserId: Guid.Parse("050add37-54b3-4996-9bcc-8ed3cc4992b6"),
        Username:     "scott.brandel",
        Email:        "scott.brandel@scientificdrilling.com",
        FirstName:    "Scott",
        LastName:     "Brandel");

    public static readonly SeedUser JohnBorders = new(
        IdentityId:   Guid.Parse("d92be0d5-dfbe-4d1d-9823-1ca37617dade"),
        MasterUserId: Guid.Parse("0c2c609c-abb0-4009-8928-e274352caf11"),
        Username:     "john.borders",
        Email:        "john.borders@scientificdrilling.com",
        FirstName:    "John",
        LastName:     "Borders");

    public static readonly SeedUser GavinHelboe = new(
        IdentityId:   Guid.Parse("2c4f110e-adc4-4759-aa34-b73ec0954c9e"),
        MasterUserId: Guid.Parse("466ba5fd-d339-4a92-93bc-ec3354a98945"),
        Username:     "gavin.helboe",
        Email:        "gavin.helboe@scientificdrilling.com",
        FirstName:    "Gavin",
        LastName:     "Helboe");

    /// <summary>Iteration order is wire-stable — used by both seeders.</summary>
    public static readonly IReadOnlyList<SeedUser> All =
    [
        DapoAjayi, JamieDorey, AdamKarabasz, DouglasRidgway, TravisSolomon,
        MikeKing, JamesPowell, JoelHarrison, ScottBrandel, JohnBorders, GavinHelboe,
    ];
}

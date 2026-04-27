using SDI.Enki.Infrastructure.Provisioning;

namespace SDI.Enki.Infrastructure.Tests.Provisioning;

/// <summary>
/// <see cref="DatabaseAdmin.ValidateDatabaseName(string)"/> is the
/// second of two layers of injection-defense around the master-DB
/// admin connection. The first layer is
/// <see cref="Internal.DatabaseNaming.ValidateCode(string)"/> which
/// validates the tenant code at provisioning entry; this layer
/// validates the resulting database name immediately before any DDL
/// runs, so a future caller that sneaks past the first validator
/// still can't reach the admin connection with arbitrary text.
/// </summary>
public class DatabaseAdminValidationTests
{
    // ---------- valid names ----------
    //
    // Names produced by DatabaseNaming.ForKind for valid codes. The
    // overlap with DatabaseNamingTests is deliberate — these two
    // layers are deliberately redundant, and the tests for them
    // need to mirror that redundancy.

    [Theory]
    [InlineData("Enki_EXXON_Active")]
    [InlineData("Enki_EXXON_Archive")]
    [InlineData("Enki_A_Active")]
    [InlineData("Enki_SDI_01_Archive")]
    [InlineData("Enki_PERMIAN_Active")]
    [InlineData("Enki_NORTHSEA_Archive")]
    [InlineData("Enki_BOREAL_Active")]
    public void ValidateDatabaseName_AcceptsValidNames(string name)
    {
        // Doesn't throw.
        DatabaseAdmin.ValidateDatabaseName(name);
    }

    // ---------- invalid names ----------

    [Theory]
    [InlineData("")]                               // empty
    [InlineData("   ")]                            // whitespace
    [InlineData("Enki_exxon_Active")]              // lowercase code
    [InlineData("Enki_1EXXON_Active")]             // code starts with digit
    [InlineData("Enki_EX-XON_Active")]             // hyphen in code
    [InlineData("Enki_EX XON_Active")]             // space in code
    [InlineData("Enki_EXXON_Live")]                // wrong kind suffix
    [InlineData("OtherDb")]                        // missing Enki_ prefix
    [InlineData("Enki__Active")]                   // empty code
    [InlineData("Enki_EXXON_ACTIVE")]              // wrong case on suffix
    [InlineData("Enki_EXXON_Active'; DROP DATABASE master; --")]  // injection attempt
    [InlineData("Enki_EXXON_Active]; DROP DATABASE master; --[")] // bracket-escape injection attempt
    [InlineData("master")]                         // bare master DB
    [InlineData("Enki_ABCDEFGHIJKLMNOPQRSTUVWXYZ_Active")] // code >24 chars
    public void ValidateDatabaseName_RejectsInvalidNames(string name)
    {
        Assert.Throws<ArgumentException>(() => DatabaseAdmin.ValidateDatabaseName(name));
    }

    [Fact]
    public void ValidateDatabaseName_RejectsNull()
    {
        Assert.Throws<ArgumentException>(() => DatabaseAdmin.ValidateDatabaseName(null!));
    }
}

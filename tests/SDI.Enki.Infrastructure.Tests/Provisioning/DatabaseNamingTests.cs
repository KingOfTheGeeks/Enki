using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Provisioning.Internal;

namespace SDI.Enki.Infrastructure.Tests.Provisioning;

/// <summary>
/// <see cref="DatabaseNaming"/> is the single source of truth for per-tenant
/// DB names. These tests pin the naming convention so a future refactor
/// can't silently break every tenant's connection string.
/// </summary>
public class DatabaseNamingTests
{
    [Theory]
    [InlineData("EXXON",  "Enki_EXXON_Active",   "Enki_EXXON_Archive")]
    [InlineData("CVX",    "Enki_CVX_Active",     "Enki_CVX_Archive")]
    [InlineData("A",      "Enki_A_Active",       "Enki_A_Archive")]
    [InlineData("SDI_01", "Enki_SDI_01_Active",  "Enki_SDI_01_Archive")]
    public void ForKind_ProducesExpectedNames(string code, string expectedActive, string expectedArchive)
    {
        Assert.Equal(expectedActive,  DatabaseNaming.ForKind(code, TenantDatabaseKind.Active));
        Assert.Equal(expectedArchive, DatabaseNaming.ForKind(code, TenantDatabaseKind.Archive));
    }

    [Theory]
    [InlineData("")]              // empty
    [InlineData("   ")]           // whitespace
    [InlineData("exxon")]         // lowercase rejected
    [InlineData("1EXXON")]        // leading digit
    [InlineData("EX-XON")]        // hyphen
    [InlineData("EX XON")]        // space
    [InlineData("EXXON'; DROP")]  // injection attempt
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ")] // too long (>24)
    public void ValidateCode_RejectsInvalidCodes(string code)
    {
        Assert.Throws<ArgumentException>(() => DatabaseNaming.ValidateCode(code));
    }

    [Fact]
    public void ValidateCode_RejectsNull()
    {
        Assert.Throws<ArgumentException>(() => DatabaseNaming.ValidateCode(null!));
    }
}

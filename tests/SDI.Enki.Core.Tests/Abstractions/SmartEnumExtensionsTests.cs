using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.Units;

namespace SDI.Enki.Core.Tests.Abstractions;

/// <summary>
/// Pins behaviour of <see cref="SmartEnumExtensions"/>. The helper is
/// the single source of truth for "wire string → SmartEnum" parsing
/// across Enki controllers; regressions here would manifest as 400s
/// where there shouldn't be any (or worse, 200s with the wrong value).
/// </summary>
public class SmartEnumExtensionsTests
{
    [Theory]
    [InlineData("Field",  true)]
    [InlineData("metric", true)]   // case-insensitive
    [InlineData("SI",     true)]
    [InlineData("",       false)]  // whitespace handling
    [InlineData("   ",    false)]
    [InlineData("Bogus",  false)]
    public void TryFromName_UnitSystem_ParsesKnownNames(string name, bool expected)
    {
        var ok = SmartEnumExtensions.TryFromName<UnitSystem>(name, out var value, UnitSystem.Custom);

        Assert.Equal(expected, ok);
        if (expected)
            Assert.NotNull(value);
        else
            Assert.Null(value);
    }

    [Fact]
    public void TryFromName_NullName_ReturnsFalse()
    {
        var ok = SmartEnumExtensions.TryFromName<RunType>(null, out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryFromName_ExcludedValue_RejectedEvenIfNameMatches()
    {
        // UnitSystem.Custom IS in the list but reserved at the API
        // surface; passing it as `excluding` must drop it.
        var ok = SmartEnumExtensions.TryFromName<UnitSystem>(
            "Custom", out var value, UnitSystem.Custom);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void UnknownNameMessage_GeneratesExpectedListFromSmartEnum()
    {
        // RunType is the canonical "small SmartEnum" we use for this pin
        // since TenantUserRole was retired (2026-05-01). The helper's
        // shape is what's under test, not the specific enum, and
        // SDI.Enki.Core.Tests can't reach Shared (which is where
        // TeamSubtype lives) without a project-ref change.
        var msg = SmartEnumExtensions.UnknownNameMessage<RunType>("xyz");

        Assert.Contains("Unknown RunType 'xyz'", msg);
        // Lists every RunType — so a future addition flows through automatically.
        foreach (var runType in RunType.List)
            Assert.Contains(runType.Name, msg);
    }

    [Fact]
    public void UnknownNameMessage_OmitsExcludedValues()
    {
        var msg = SmartEnumExtensions.UnknownNameMessage<UnitSystem>(
            "xyz", UnitSystem.Custom);

        Assert.DoesNotContain("Custom", msg);
        Assert.Contains("Field",  msg);
        Assert.Contains("Metric", msg);
        Assert.Contains("SI",     msg);
    }
}

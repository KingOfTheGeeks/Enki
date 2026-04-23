using System.Text.RegularExpressions;
using SDI.Enki.Core.Master.Tenants.Enums;

namespace SDI.Enki.Infrastructure.Provisioning.Internal;

/// <summary>
/// Centralizes the naming convention for per-tenant databases so the name
/// is derived in exactly one place. Anything that needs to find a tenant's
/// DB by code should go through here, not construct the string inline.
/// </summary>
internal static partial class DatabaseNaming
{
    // Enki_{CODE}_Active / Enki_{CODE}_Archive
    // Code must be 1..24 chars, [A-Z0-9_], no leading digit.
    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{0,23}$")]
    private static partial Regex CodePattern();

    public static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Tenant code must be non-empty.", nameof(code));

        if (!CodePattern().IsMatch(code))
            throw new ArgumentException(
                $"Tenant code '{code}' is invalid. Must be 1–24 chars, uppercase A–Z / 0–9 / underscore, not starting with a digit.",
                nameof(code));
    }

    public static string ForKind(string code, TenantDatabaseKind kind)
    {
        ValidateCode(code);
        return $"Enki_{code}_{kind.Name}";
    }
}

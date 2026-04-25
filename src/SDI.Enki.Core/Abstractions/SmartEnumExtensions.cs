using System.Diagnostics.CodeAnalysis;
using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Abstractions;

/// <summary>
/// Utility helpers for parsing and reporting on
/// <see cref="SmartEnum{TEnum}"/> values from wire input. Replaces the
/// near-identical <c>TryParseXxx(string, out Xxx)</c> + <c>= null!</c>
/// dance that was duplicated across <see cref="JobsController"/>,
/// <see cref="RunsController"/>, <see cref="WellsController"/>, and
/// <see cref="TenantMembersController"/>.
///
/// <para>
/// Lives in <c>SDI.Enki.Core.Abstractions</c> because both Core
/// (validation, lifecycle) and the WebApi controllers reference it.
/// Only depends on <c>Ardalis.SmartEnum</c> + BCL — no EF, no MVC.
/// </para>
/// </summary>
public static class SmartEnumExtensions
{
    /// <summary>
    /// Case-insensitive parse of <paramref name="name"/> into the
    /// <typeparamref name="TEnum"/> SmartEnum. Returns <c>true</c> with
    /// <paramref name="value"/> populated on success; <c>false</c> with
    /// <paramref name="value"/> set to <c>null</c> on failure (including
    /// null / whitespace input, or a value present in
    /// <paramref name="excluding"/>). Unlike Ardalis's built-in
    /// <c>TryFromName</c>, this overload is null- and whitespace-safe at
    /// the API boundary and supports filtering reserved values.
    ///
    /// <para>
    /// <paramref name="excluding"/> is for cases like
    /// <c>UnitSystem.Custom</c>: present in <see cref="SmartEnum{TEnum}.List"/>
    /// for typing reasons but not yet accepted at the API surface. Pass
    /// the reserved values; they're rejected here and dropped from
    /// <see cref="UnknownNameMessage{TEnum}"/>'s expected list.
    /// </para>
    /// </summary>
    public static bool TryFromName<TEnum>(
        string? name,
        [NotNullWhen(true)] out TEnum? value,
        params TEnum[] excluding)
        where TEnum : SmartEnum<TEnum>
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            value = null;
            return false;
        }

        value = SmartEnum<TEnum>.List
            .FirstOrDefault(x => !excluding.Contains(x)
                              && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return value is not null;
    }

    /// <summary>
    /// Builds a "Unknown {TypeName} '{actual}'. Expected A, B, C." message
    /// suitable for a 400 ValidationProblem field error. The expected list
    /// is generated from <see cref="SmartEnum{TEnum}.List"/> minus any
    /// <paramref name="excluding"/> values — adding a new SmartEnum entry
    /// automatically widens every error message; reserving a value via
    /// <paramref name="excluding"/> automatically narrows them.
    /// </summary>
    public static string UnknownNameMessage<TEnum>(string? actual, params TEnum[] excluding)
        where TEnum : SmartEnum<TEnum>
    {
        var expected = string.Join(", ", SmartEnum<TEnum>.List
            .Where(x => !excluding.Contains(x))
            .Select(x => x.Name));
        return $"Unknown {typeof(TEnum).Name} '{actual}'. Expected {expected}.";
    }
}

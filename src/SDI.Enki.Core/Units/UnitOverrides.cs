using UnitsNet.Units;

namespace SDI.Enki.Core.Units;

/// <summary>
/// Named per-column unit overrides — for cases where a single
/// quantity reads better in a non-default unit on a specific column,
/// regardless of which preset the rest of the page is using. The
/// canonical example is tubular OD: drillers say "in" on Field and
/// "mm" on Metric / SI, even though the default Length unit is ft / m.
///
/// <para>
/// Centralising the picks here means a UI tweak ("show mm on Field
/// too") is one edit, not a sweep of every <c>&lt;UnitFormatted&gt;</c>
/// /<c>&lt;UnitInput&gt;</c> call site.
/// </para>
///
/// <para>
/// Each helper returns a tuple in the same shape both
/// <c>&lt;UnitFormatted&gt;</c> and <c>&lt;UnitInput&gt;</c> consume —
/// pass it directly into <c>OverrideUnit</c> /
/// <c>OverrideAbbreviation</c>:
/// <code>
///   var od = UnitOverrides.TubularDiameter(_units);
///   &lt;UnitFormatted ... OverrideUnit="@od.Unit"
///                       OverrideAbbreviation="@od.Abbreviation" /&gt;
/// </code>
/// </para>
/// </summary>
public static class UnitOverrides
{
    /// <summary>One column's unit pick + display abbreviation.</summary>
    public readonly record struct ColumnUnit(Enum Unit, string Abbreviation);

    /// <summary>
    /// Tubular outer diameter: <c>in</c> on Field, <c>mm</c> on Metric
    /// and SI. Stored as meters (canonical SI) regardless. Driller
    /// idiom across both hemispheres — feet would be too coarse for
    /// a 5" drillpipe ID, and meters would round 0.127 m to less
    /// useful precision than 5.0 in / 127 mm.
    /// </summary>
    public static ColumnUnit TubularDiameter(UnitSystem system) =>
        system == UnitSystem.Field
            ? new(LengthUnit.Inch,       "in")
            : new(LengthUnit.Millimeter, "mm");
}

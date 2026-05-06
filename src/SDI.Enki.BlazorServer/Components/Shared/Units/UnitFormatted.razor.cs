using System.Globalization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.Core.Units;

namespace SDI.Enki.BlazorServer.Components.Shared.Units;

public partial class UnitFormatted : ComponentBase
{
    /// <summary>
    /// The value as stored — i.e. canonical SI for the given quantity.
    /// Length in meters, Density in kg/m³, Pressure in pascals, etc.
    /// Angle is the documented exception: stored as degrees, displayed
    /// as degrees, no conversion.
    /// </summary>
    [Parameter, EditorRequired] public double Value { get; set; }

    /// <summary>What kind of quantity this value represents.</summary>
    [Parameter, EditorRequired] public EnkiQuantity Quantity { get; set; }

    /// <summary>
    /// .NET number format spec (e.g. "F2" for two decimals, "N3" for
    /// thousand-separators with three decimals). Defaults to "N2".
    /// </summary>
    [Parameter] public string Format { get; set; } = "N2";

    /// <summary>
    /// True (default) → append the unit abbreviation (e.g. " ft").
    /// False → numeric value only, useful inside grid cells where the
    /// column header already carries the unit.
    /// </summary>
    [Parameter] public bool ShowUnit { get; set; } = true;

    /// <summary>
    /// Force a specific UnitsNet unit instead of the preset for this
    /// quantity. Use cases: tubular OD always shows in inches on Field
    /// (overrides Length=ft) and millimetres on Metric / SI (overrides
    /// Length=m). Pair with <see cref="OverrideAbbreviation"/> for the
    /// label.
    /// </summary>
    [Parameter] public Enum? OverrideUnit { get; set; }

    /// <summary>The label to display when <see cref="OverrideUnit"/> is set.</summary>
    [Parameter] public string? OverrideAbbreviation { get; set; }

    /// <summary>
    /// Resolved unit-system. Picked up via cascade — pages wrap the
    /// content that uses this component in
    /// <c>&lt;CascadingValue Name="UnitSystem" Value="@_units"&gt;</c>
    /// after fetching the Job's preset. Defaults to strict SI so a
    /// component used outside any cascade doesn't silently project
    /// the wrong unit.
    /// </summary>
    [CascadingParameter(Name = "UnitSystem")]
    public UnitSystem Units { get; set; } = UnitSystem.SI;

    private bool IsAngle => Quantity == EnkiQuantity.Angle;

    private static string FormatNumber(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>
    /// Angles render as degrees universally; same number-format pipe
    /// as everything else but no UnitSystemPresets lookup so we never
    /// accidentally print radians.
    /// </summary>
    private static string FormatAngle(double degrees, string format) =>
        degrees.ToString(format, CultureInfo.InvariantCulture);
}

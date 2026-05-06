using Microsoft.AspNetCore.Components;
using SDI.Enki.Core.Units;

namespace SDI.Enki.BlazorServer.Components.Shared.Units;

public partial class UnitReadout : ComponentBase
{
    /// <summary>SI value to display.</summary>
    [Parameter, EditorRequired] public double Value { get; set; }

    /// <summary>Quantity tag for unit selection.</summary>
    [Parameter, EditorRequired] public EnkiQuantity Quantity { get; set; }

    /// <summary>.NET number format spec. Defaults to "N3".</summary>
    [Parameter] public string Format { get; set; } = "N3";

    /// <summary>
    /// True (default) → render the unit abbreviation suffix. False →
    /// just the number, useful when the surrounding label already
    /// carries the unit (e.g. <c>UnitLabel.For(...)</c> on the
    /// &lt;label&gt; element).
    /// </summary>
    [Parameter] public bool ShowUnit { get; set; } = false;

    /// <summary>Force a specific unit instead of the preset.</summary>
    [Parameter] public Enum? OverrideUnit { get; set; }

    /// <summary>Abbreviation paired with <see cref="OverrideUnit"/>.</summary>
    [Parameter] public string? OverrideAbbreviation { get; set; }

    /// <summary>
    /// Optional ARIA label override. When the surrounding form-group
    /// has a visible &lt;label&gt; with the unit + field name, this
    /// can stay null (the &lt;label&gt; / output association is
    /// handled by id linking at the call site if needed).
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>
    /// Extra CSS class on the &lt;output&gt; element. Defaults to
    /// "enki-form-input" so the readout drops into a standard form
    /// row visually identically to an InputNumber-disabled.
    /// </summary>
    [Parameter] public string CssClass { get; set; } = "enki-form-input";
}

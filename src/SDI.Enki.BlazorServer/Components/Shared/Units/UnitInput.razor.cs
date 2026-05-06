using System.Globalization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.Core.Units;

namespace SDI.Enki.BlazorServer.Components.Shared.Units;

public partial class UnitInput : ComponentBase
{
    /// <summary>
    /// The bound value in canonical SI for the given quantity. Length
    /// in meters, Density in kg/m³, Pressure in pascals, etc. Angle
    /// is the documented exception: bound as degrees because the DB
    /// stores degrees regardless of UnitSystemPresets' SI = radians.
    /// </summary>
    [Parameter, EditorRequired] public double SiValue { get; set; }

    /// <summary>
    /// Two-way binding callback. Fires once per keystroke that
    /// produces a fully-parseable double. Partial / invalid input
    /// is held in the visible string but does NOT fire this — the
    /// parent's bound field stays at the last good SI value.
    /// </summary>
    [Parameter] public EventCallback<double> SiValueChanged { get; set; }

    /// <summary>What kind of quantity this value represents.</summary>
    [Parameter, EditorRequired] public EnkiQuantity Quantity { get; set; }

    /// <summary>
    /// .NET number format spec applied to the visible value. Defaults
    /// to "G" (general) so editing doesn't lose precision — a fixed
    /// "F2" would silently round 10.555 → "10.56" the moment the user
    /// tabbed out, then bind 10.56 back into SiValue. Use a tighter
    /// format only on read-only display via <see cref="UnitFormatted"/>.
    /// </summary>
    [Parameter] public string Format { get; set; } = "G";

    /// <summary>HTML number-input step, in display units. Defaults to 0.01.</summary>
    [Parameter] public double Step { get; set; } = 0.01;

    /// <summary>Optional HTML min attribute, in display units.</summary>
    [Parameter] public double? Min { get; set; }

    /// <summary>Optional HTML max attribute, in display units.</summary>
    [Parameter] public double? Max { get; set; }

    /// <summary>Disable the input.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Extra CSS class on the input element. Defaults to "form-control"
    /// so the component drops into a Bootstrap form unchanged.
    /// </summary>
    [Parameter] public string CssClass { get; set; } = "form-control";

    /// <summary>
    /// True (default) → render the unit abbreviation suffix next to
    /// the input. False → bare input, useful when the column header
    /// or label already carries the unit.
    /// </summary>
    [Parameter] public bool ShowUnit { get; set; } = true;

    /// <summary>
    /// Force a specific UnitsNet unit instead of the preset for this
    /// quantity. Use cases: tubular OD edits in inches on Field
    /// (overrides Length=ft) and millimetres on Metric / SI (overrides
    /// Length=m). Pair with <see cref="OverrideAbbreviation"/> for
    /// the suffix label.
    /// </summary>
    [Parameter] public Enum? OverrideUnit { get; set; }

    /// <summary>The label to show when <see cref="OverrideUnit"/> is set.</summary>
    [Parameter] public string? OverrideAbbreviation { get; set; }

    /// <summary>
    /// Resolved unit-system. Picked up via cascade — pages wrap the
    /// content that uses this component in
    /// <c>&lt;CascadingValue Name="UnitSystem" Value="@_units"&gt;</c>
    /// after fetching the Job's preset. Defaults to strict SI so an
    /// orphaned input doesn't silently treat user input as the wrong
    /// unit.
    /// </summary>
    [CascadingParameter(Name = "UnitSystem")]
    public UnitSystem Units { get; set; } = UnitSystem.SI;

    // ---- internal state ------------------------------------------------

    /// <summary>
    /// Visible string. Held verbatim so partial/invalid input ("12.",
    /// "1.5e", "-") doesn't get clobbered by a re-projection from SI
    /// after every keystroke. Refreshed only when the parent changes
    /// SiValue externally (see OnParametersSet).
    /// </summary>
    private string _lastDisplayString = "";

    /// <summary>SI value that produced <see cref="_lastDisplayString"/>.</summary>
    private double _lastDisplayedSi;

    /// <summary>
    /// Cascading UnitSystem snapshot. If the cascade flips
    /// (Job preset edit → re-cascade) we re-project so the visible
    /// number switches units immediately.
    /// </summary>
    private UnitSystem _lastDisplayedUnits = UnitSystem.SI;

    /// <summary>One-shot latch for the very first render.</summary>
    private bool _initialized;

    /// <summary>Cached abbreviation for the current (Units, Quantity, Override) tuple.</summary>
    private string _abbrev = "";

    private bool IsAngle => Quantity == EnkiQuantity.Angle;

    private string StepText =>
        Step.ToString(CultureInfo.InvariantCulture);

    private string? MinText =>
        Min.HasValue ? Min.Value.ToString(CultureInfo.InvariantCulture) : null;

    private string? MaxText =>
        Max.HasValue ? Max.Value.ToString(CultureInfo.InvariantCulture) : null;

    protected override void OnParametersSet()
    {
        // Re-project only when the parent moved SiValue OR the
        // cascading UnitSystem changed. While the user types,
        // SiValue updates land here too — but we set
        // _lastDisplayedSi inside OnInput first, so this guard
        // short-circuits and the typed string is preserved.
        var needsProjection =
            !_initialized
            || _lastDisplayedSi    != SiValue
            || _lastDisplayedUnits != Units;

        if (!needsProjection) return;

        _lastDisplayString  = ProjectedDisplayValue()
            .ToString(Format, CultureInfo.InvariantCulture);
        _lastDisplayedSi    = SiValue;
        _lastDisplayedUnits = Units;
        _abbrev             = ResolveAbbreviation();
        _initialized        = true;
    }

    private double ProjectedDisplayValue()
    {
        if (IsAngle) return SiValue;                                  // degrees in / degrees out
        if (OverrideUnit is not null)
            return UnitConverter.FromSi(SiValue, Quantity, OverrideUnit);

        // Default path: route through Measurement.As so we get the
        // same unit the read-only <UnitFormatted> would render.
        var (value, _) = new Measurement(SiValue, Quantity).As(Units);
        return value;
    }

    private string ResolveAbbreviation()
    {
        if (IsAngle) return "°";
        if (OverrideUnit is not null) return OverrideAbbreviation ?? "";
        return UnitSystemPresets.Abbreviation(Units, Quantity);
    }

    private async Task OnInput(ChangeEventArgs e)
    {
        if (e.Value is not string typed) return;

        // Always preserve what the user typed, even if we can't parse
        // it yet ("12.", "-", ""). The parent's SiValue stays at the
        // last good number until the typed string parses.
        _lastDisplayString = typed;

        if (!double.TryParse(
                typed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var displayValue))
        {
            return;
        }

        var newSi = ConvertToSi(displayValue);
        if (newSi == SiValue) return;

        // Pre-record the SI before raising the callback so the
        // ensuing OnParametersSet sees _lastDisplayedSi == SiValue
        // and skips re-projection (which would clobber what the
        // user just typed with a round-tripped string).
        _lastDisplayedSi = newSi;
        SiValue          = newSi;
        await SiValueChanged.InvokeAsync(newSi);
    }

    private double ConvertToSi(double displayValue)
    {
        if (IsAngle) return displayValue;                             // degrees in / degrees out
        if (OverrideUnit is not null)
            return UnitConverter.ToSi(displayValue, Quantity, OverrideUnit);

        var choice = UnitSystemPresets.Get(Units, Quantity);
        if (choice.UnitEnum is null)
        {
            // ConvertOilfield is one-way (SI → display) by design —
            // most oilfield-specific quantities are computed or
            // measured by instruments, not typed. Fail loudly so the
            // call site is fixed rather than silently losing precision.
            throw new InvalidOperationException(
                $"<UnitInput> cannot edit oilfield-specific quantity {Quantity}. " +
                $"These quantities are read-only by design — use <UnitFormatted> " +
                $"for display, or add a bidirectional converter in UnitConverter " +
                $"if you really need to edit them.");
        }

        return UnitConverter.ToSi(displayValue, Quantity, choice.UnitEnum);
    }
}

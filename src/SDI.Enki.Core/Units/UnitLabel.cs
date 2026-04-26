namespace SDI.Enki.Core.Units;

/// <summary>
/// Builds short, unit-aware labels for grid column headers, form
/// labels, and chart axes. The motivating shape is
/// <c>"Depth (ft)"</c> / <c>"Depth (m)"</c> — same noun, different
/// abbreviation, depending on the Job's
/// <see cref="UnitSystem"/>.
///
/// <para>
/// Pages keep their resolved <c>UnitSystem</c> in a private
/// <c>_units</c> field (populated from a Job fetch in
/// <c>OnInitializedAsync</c>) and pass it in directly:
/// <code>
///   HeaderText="@UnitLabel.For("Depth", EnkiQuantity.Length, _units)"
/// </code>
/// HeaderText is evaluated at the page's @code scope rather than
/// inside the cascade, so we read <c>_units</c> directly.
/// Re-rendering on a unit-system change is handled by the host
/// component — the grid sees a new HeaderText and updates.
/// </para>
///
/// <para>
/// Angles short-circuit to "°" regardless of preset, mirroring
/// <see cref="Measurement"/> + <c>UnitFormatted</c> + <c>UnitInput</c>:
/// the DB stores degrees, the user sees degrees, never radians.
/// Quantities with an empty abbreviation
/// (<see cref="EnkiQuantity.Dimensionless"/>) render as the bare
/// label with no parens — <c>"Ratio"</c>, not <c>"Ratio ()"</c>.
/// </para>
/// </summary>
public static class UnitLabel
{
    /// <summary>
    /// Returns <c>"{label} ({abbrev})"</c>, or just <c>"{label}"</c>
    /// when the quantity has no abbreviation under the preset.
    /// </summary>
    /// <param name="label">Noun-only column / field name (e.g. "Depth", "TVD").</param>
    /// <param name="quantity">What kind of value the column holds.</param>
    /// <param name="system">The Job's UnitSystem from the cascade.</param>
    /// <param name="overrideUnit">
    /// Forces a specific UnitsNet unit instead of the preset. Mirrors
    /// the override hook on <c>UnitFormatted</c> / <c>UnitInput</c>;
    /// used by tubular OD (in / mm).
    /// </param>
    /// <param name="overrideAbbreviation">
    /// The abbreviation displayed when <paramref name="overrideUnit"/>
    /// is set. Required if the override is used.
    /// </param>
    public static string For(
        string        label,
        EnkiQuantity  quantity,
        UnitSystem    system,
        Enum?         overrideUnit         = null,
        string?       overrideAbbreviation = null)
    {
        // Angle short-circuit. UnitSystemPresets does say "rad" on
        // strict SI, but DB and UX both speak degrees universally;
        // hard-coding "°" here keeps the header in lock-step with
        // what UnitFormatted will actually render.
        if (quantity == EnkiQuantity.Angle)
            return $"{label} (°)";

        string abbrev;
        if (overrideUnit is not null)
        {
            abbrev = overrideAbbreviation ?? "";
        }
        else
        {
            abbrev = UnitSystemPresets.Abbreviation(system, quantity);
        }

        return string.IsNullOrEmpty(abbrev) ? label : $"{label} ({abbrev})";
    }
}

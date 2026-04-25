namespace SDI.Enki.Core.Units;

/// <summary>
/// A scalar measurement carrying its <see cref="EnkiQuantity"/>. The
/// stored <see cref="SiValue"/> is canonical SI for that quantity —
/// meters for Length, pascals for Pressure, kg/m³ for Density, etc.
/// Display in user-preferred units happens at the rendering edge via
/// <see cref="UnitConverter"/>; storage always speaks SI.
///
/// <para>
/// Construct from a non-SI source via <c>FromUnit</c>: it converts at
/// construction so downstream code only ever sees SI.
/// </para>
///
/// <para>
/// The struct is a <c>readonly record struct</c>: no allocation, value
/// equality by (SiValue, Quantity), debugger-friendly. Two measurements
/// with the same SI value but different quantities are NOT equal —
/// 1 (Length) ≠ 1 (Pressure), even though both are 1 in canonical units.
/// </para>
/// </summary>
public readonly record struct Measurement(double SiValue, EnkiQuantity Quantity)
{
    /// <summary>
    /// Builds a Measurement from a value in some specific unit. The
    /// unit's quantity must match <paramref name="quantity"/> — passing
    /// <c>LengthUnit.Foot</c> with <c>EnkiQuantity.Pressure</c> throws
    /// because the conversion table doesn't have a path for that pair.
    /// </summary>
    public static Measurement FromUnit(double value, EnkiQuantity quantity, Enum unit)
    {
        var si = UnitConverter.ToSi(value, quantity, unit);
        return new Measurement(si, quantity);
    }

    /// <summary>
    /// Convenience: build from a value already in SI. Same as the
    /// primary constructor; reads better at call sites that come from
    /// known-SI sources (database reads, internal math).
    /// </summary>
    public static Measurement FromSi(double siValue, EnkiQuantity quantity) =>
        new(siValue, quantity);

    /// <summary>
    /// Project this measurement into the unit a given preset uses for
    /// its quantity. Returns the numeric value plus the abbreviation —
    /// callers that just want the number can ignore the second item.
    /// </summary>
    public (double Value, string Abbreviation) As(UnitSystem system)
    {
        var choice = UnitSystemPresets.Get(system, Quantity);
        var converted = choice.UnitEnum is null
            ? UnitConverter.ConvertOilfield(SiValue, Quantity, system)
            : UnitConverter.FromSi(SiValue, Quantity, choice.UnitEnum);
        return (converted, choice.Abbreviation);
    }

    /// <summary>
    /// Standard human-friendly format: "10,000 ft", "3.45 ppg",
    /// "55,000 nT". Uses InvariantCulture so log lines don't shift on
    /// the host's locale; switch to CurrentCulture at the UI layer if
    /// number-format-by-region matters.
    /// </summary>
    public string Format(UnitSystem system, string? numberFormat = null)
    {
        var (value, abbrev) = As(system);
        var n = numberFormat is null
            ? value.ToString("N", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString(numberFormat, System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(abbrev) ? n : $"{n} {abbrev}";
    }
}

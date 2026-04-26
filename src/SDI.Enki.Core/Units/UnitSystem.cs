using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Units;

/// <summary>
/// A named bundle of preferred units — picks one unit per
/// <see cref="EnkiQuantity"/>. Three canonical presets ship with Enki;
/// <see cref="Custom"/> is reserved for per-user overrides landing in a
/// later phase (a sparse <c>UserUnitOverride</c> table layered on top of
/// one of the three base presets).
///
/// <para>
/// Storage convention, non-negotiable and assumed everywhere downstream:
/// every scalar measurement in the DB is in canonical SI. This enum is a
/// <i>display + input preference</i>, not a storage format. A Job picking
/// "Field" changes what mud weight reads as in the UI (ppg vs kg/m³); it
/// does not change the number of pascals stored in the column.
/// </para>
///
/// <para>Int values are wire-stable — do not renumber once values ship.</para>
/// </summary>
public sealed class UnitSystem : SmartEnum<UnitSystem>
{
    /// <summary>
    /// US / oilfield units — ft, psi, °F, ppg (pounds per US gallon)
    /// mud weight, bbl/min flow, klbf hookload. The norm for North
    /// American onshore operations. This is what most SDI jobs run in.
    /// </summary>
    public static readonly UnitSystem Field = new(
        nameof(Field), 1,
        "US oilfield — ft, psi, °F, ppg, bbl/min, klbf");

    /// <summary>
    /// Metric oilfield — m, bar, °C, sg (specific gravity) or kg/m³
    /// mud weight, L/min flow, kN hookload. The norm for most of the
    /// world outside North America. Practical rather than strict: uses
    /// bar instead of pascals, °C instead of kelvins.
    /// </summary>
    public static readonly UnitSystem Metric = new(
        nameof(Metric), 2,
        "Metric oilfield — m, bar, °C, sg / kg/m³, L/min, kN");

    /// <summary>
    /// Strict SI — m, Pa, K, kg/m³, m³/s, N. Engineering / academic
    /// use rather than operational. Available because the backend IP
    /// libraries (AMR.Core.Survey, etc.) mostly work in SI internally
    /// and a user importing raw SI data or reviewing unit-less
    /// calculations may prefer it. Rarely picked for a live rig.
    /// </summary>
    public static readonly UnitSystem SI = new(
        nameof(SI), 3,
        "Strict SI — m, Pa, K, kg/m³, m³/s, N");

    /// <summary>
    /// Reserved for Phase 7e. Resolves at runtime by layering a
    /// sparse <c>UserUnitOverride</c> table onto a base preset. Not
    /// selectable on Job today; rows holding this value are an error
    /// until the override plumbing ships.
    /// </summary>
    public static readonly UnitSystem Custom = new(
        nameof(Custom), 99,
        "Per-quantity overrides on top of a base preset");

    public string Description { get; }

    private UnitSystem(string name, int value, string description)
        : base(name, value)
    {
        Description = description;
    }

    /// <summary>
    /// Wire-string (e.g. JSON DTO) → SmartEnum bridge with a safe
    /// fallback. Job DTOs carry <c>UnitSystem.Name</c> as a string so
    /// the JSON contract isn't tied to SmartEnum internals; this is
    /// the inverse used by every page that needs the resolved value.
    /// Falls back to strict <see cref="SI"/> on null / unknown input
    /// rather than throwing — a brand-new system the client doesn't
    /// recognise yet should still let pages render.
    /// </summary>
    public static UnitSystem FromNameOrSi(string? name) =>
        name is not null && TryFromName(name, out var found) ? found : SI;
}

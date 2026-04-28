using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tools.Enums;

/// <summary>
/// Provenance of a Calibration row. <c>Migrated</c> covers everything seeded
/// from the Nabu fleet snapshot at first boot; <c>Imported</c> is for later
/// uploads of Nabu-exported JSON; <c>ComputedInEnki</c> is for the future
/// in-portal calibration pipeline (port of Nabu's CalibrationCreationService).
/// </summary>
public sealed class CalibrationSource : SmartEnum<CalibrationSource>
{
    public static readonly CalibrationSource Migrated       = new(nameof(Migrated),       1);
    public static readonly CalibrationSource Imported       = new(nameof(Imported),       2);
    public static readonly CalibrationSource ComputedInEnki = new(nameof(ComputedInEnki), 3);

    private CalibrationSource(string name, int value) : base(name, value) { }
}

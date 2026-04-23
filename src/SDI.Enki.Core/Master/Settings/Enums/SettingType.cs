using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Settings.Enums;

public sealed class SettingType : SmartEnum<SettingType>
{
    public static readonly SettingType GradientExport = new(nameof(GradientExport), 1);
    public static readonly SettingType RotaryExport   = new(nameof(RotaryExport),   2);
    public static readonly SettingType PassiveExport  = new(nameof(PassiveExport),  3);

    private SettingType(string name, int value) : base(name, value) { }
}

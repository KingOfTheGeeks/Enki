using Ardalis.SmartEnum;

namespace SDI.Enki.Core.TenantDb.Runs.Enums;

public sealed class RunType : SmartEnum<RunType>
{
    public static readonly RunType Gradient = new(nameof(Gradient), 1);
    public static readonly RunType Rotary   = new(nameof(Rotary),   2);
    public static readonly RunType Passive  = new(nameof(Passive),  3);

    private RunType(string name, int value) : base(name, value) { }
}

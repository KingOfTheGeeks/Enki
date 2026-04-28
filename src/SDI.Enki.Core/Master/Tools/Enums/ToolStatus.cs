using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tools.Enums;

public sealed class ToolStatus : SmartEnum<ToolStatus>
{
    public static readonly ToolStatus Active   = new(nameof(Active),   1);
    public static readonly ToolStatus Retired  = new(nameof(Retired),  2);
    public static readonly ToolStatus Lost     = new(nameof(Lost),     3);

    private ToolStatus(string name, int value) : base(name, value) { }
}

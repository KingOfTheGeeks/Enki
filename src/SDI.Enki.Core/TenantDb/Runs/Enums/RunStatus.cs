using Ardalis.SmartEnum;

namespace SDI.Enki.Core.TenantDb.Runs.Enums;

public sealed class RunStatus : SmartEnum<RunStatus>
{
    public static readonly RunStatus Planned    = new(nameof(Planned),    1);
    public static readonly RunStatus Active     = new(nameof(Active),     2);
    public static readonly RunStatus Completed  = new(nameof(Completed),  3);
    public static readonly RunStatus Suspended  = new(nameof(Suspended),  4);
    public static readonly RunStatus Cancelled  = new(nameof(Cancelled),  5);

    private RunStatus(string name, int value) : base(name, value) { }
}

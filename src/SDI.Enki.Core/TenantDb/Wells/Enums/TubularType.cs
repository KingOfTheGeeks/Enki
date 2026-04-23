using Ardalis.SmartEnum;

namespace SDI.Enki.Core.TenantDb.Wells.Enums;

public sealed class TubularType : SmartEnum<TubularType>
{
    public static readonly TubularType Casing        = new(nameof(Casing),        1);
    public static readonly TubularType Liner         = new(nameof(Liner),         2);
    public static readonly TubularType Tubing        = new(nameof(Tubing),        3);
    public static readonly TubularType DrillPipe     = new(nameof(DrillPipe),     4);
    public static readonly TubularType OpenHole      = new(nameof(OpenHole),      5);

    private TubularType(string name, int value) : base(name, value) { }
}

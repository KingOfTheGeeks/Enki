using Ardalis.SmartEnum;

namespace SDI.Enki.Core.TenantDb.Wells.Enums;

public sealed class WellType : SmartEnum<WellType>
{
    public static readonly WellType Target    = new(nameof(Target),    1);
    public static readonly WellType Injection = new(nameof(Injection), 2);
    public static readonly WellType Offset    = new(nameof(Offset),    3);

    private WellType(string name, int value) : base(name, value) { }
}

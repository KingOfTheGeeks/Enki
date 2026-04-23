using Ardalis.SmartEnum;

namespace SDI.Enki.Core.TenantDb.Jobs.Enums;

public sealed class Units : SmartEnum<Units>
{
    public static readonly Units Imperial = new(nameof(Imperial), 1);
    public static readonly Units Metric   = new(nameof(Metric),   2);

    private Units(string name, int value) : base(name, value) { }
}

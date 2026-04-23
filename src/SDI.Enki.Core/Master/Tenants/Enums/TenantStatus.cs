using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tenants.Enums;

public sealed class TenantStatus : SmartEnum<TenantStatus>
{
    public static readonly TenantStatus Active   = new(nameof(Active),   1);
    public static readonly TenantStatus Inactive = new(nameof(Inactive), 2);
    public static readonly TenantStatus Archived = new(nameof(Archived), 3);

    private TenantStatus(string name, int value) : base(name, value) { }
}

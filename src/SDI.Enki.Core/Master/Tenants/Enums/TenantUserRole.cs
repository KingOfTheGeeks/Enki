using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tenants.Enums;

public sealed class TenantUserRole : SmartEnum<TenantUserRole>
{
    public static readonly TenantUserRole Admin       = new(nameof(Admin),       1);
    public static readonly TenantUserRole Contributor = new(nameof(Contributor), 2);
    public static readonly TenantUserRole Viewer      = new(nameof(Viewer),      3);

    private TenantUserRole(string name, int value) : base(name, value) { }
}

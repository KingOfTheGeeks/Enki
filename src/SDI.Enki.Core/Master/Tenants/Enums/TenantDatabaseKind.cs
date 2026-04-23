using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tenants.Enums;

public sealed class TenantDatabaseKind : SmartEnum<TenantDatabaseKind>
{
    public static readonly TenantDatabaseKind Active  = new(nameof(Active),  1);
    public static readonly TenantDatabaseKind Archive = new(nameof(Archive), 2);

    private TenantDatabaseKind(string name, int value) : base(name, value) { }
}

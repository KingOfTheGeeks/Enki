using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tenants.Enums;

public sealed class TenantDatabaseStatus : SmartEnum<TenantDatabaseStatus>
{
    public static readonly TenantDatabaseStatus Provisioning = new(nameof(Provisioning), 1);
    public static readonly TenantDatabaseStatus Active       = new(nameof(Active),       2);
    public static readonly TenantDatabaseStatus Migrating    = new(nameof(Migrating),    3);
    public static readonly TenantDatabaseStatus Archived     = new(nameof(Archived),     4);
    public static readonly TenantDatabaseStatus Failed       = new(nameof(Failed),       5);

    private TenantDatabaseStatus(string name, int value) : base(name, value) { }
}

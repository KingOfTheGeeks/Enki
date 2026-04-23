using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Migrations.Enums;

public sealed class MigrationRunStatus : SmartEnum<MigrationRunStatus>
{
    public static readonly MigrationRunStatus Running = new(nameof(Running), 1);
    public static readonly MigrationRunStatus Success = new(nameof(Success), 2);
    public static readonly MigrationRunStatus Failed  = new(nameof(Failed),  3);

    private MigrationRunStatus(string name, int value) : base(name, value) { }
}

using Ardalis.SmartEnum;

namespace SDI.Enki.Core.TenantDb.Jobs.Enums;

public sealed class JobStatus : SmartEnum<JobStatus>
{
    public static readonly JobStatus Draft     = new(nameof(Draft),     1);
    public static readonly JobStatus Active    = new(nameof(Active),    2);
    public static readonly JobStatus Completed = new(nameof(Completed), 3);
    public static readonly JobStatus Archived  = new(nameof(Archived),  4);

    private JobStatus(string name, int value) : base(name, value) { }
}

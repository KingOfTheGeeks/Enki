using Ardalis.SmartEnum;

namespace SDI.Enki.Core.TenantDb.Jobs.Enums;

/// <summary>
/// Where a Job is in its lifecycle. Starts <see cref="Draft"/>, moves to
/// <see cref="Active"/> when work begins, ends at <see cref="Archived"/>
/// (terminal — read-only).
///
/// <para>
/// Int value <c>3</c> is deliberately reserved for a future
/// <c>Completed</c> state (workflow step between Active and Archived).
/// We don't need it yet and it's easier to add it back than to predict
/// what rules should attach to it, but leaving the slot open means
/// reintroducing it is one enum line + a couple entries in
/// <see cref="JobLifecycle.AllowedTransitions"/>, never a renumber.
/// </para>
///
/// <para>
/// Int values are wire-stable and persisted — do not renumber once values
/// ship. <see cref="Archived"/> stays at 4 even with the gap at 3.
/// </para>
/// </summary>
public sealed class JobStatus : SmartEnum<JobStatus>
{
    public static readonly JobStatus Draft    = new(nameof(Draft),    1);
    public static readonly JobStatus Active   = new(nameof(Active),   2);
    // 3 — reserved for Completed. See class doc.
    public static readonly JobStatus Archived = new(nameof(Archived), 4);

    private JobStatus(string name, int value) : base(name, value) { }
}

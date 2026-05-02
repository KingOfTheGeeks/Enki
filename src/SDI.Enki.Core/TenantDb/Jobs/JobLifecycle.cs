using SDI.Enki.Core.TenantDb.Jobs.Enums;

namespace SDI.Enki.Core.TenantDb.Jobs;

/// <summary>
/// Single source of truth for what <see cref="JobStatus"/> transitions
/// are allowed. Both the controller (enforcement) and the Blazor detail
/// page (button rendering) read from this map, so there's exactly one
/// place to edit when the workflow grows.
///
/// <para>
/// Current ruleset:
/// <list type="bullet">
///   <item><c>Draft</c>: activate or archive.</item>
///   <item><c>Active</c>: archive.</item>
///   <item><c>Archived</c>: restore (back to Active). Mirrors Tenant's
///   reversible Active↔Inactive lifecycle — Archive on a Job behaves
///   as a "soft put-aside" rather than a hard terminal state.
///   Surfaces in the UI as a <b>Restore</b> button on Archived jobs.</item>
/// </list>
/// </para>
///
/// <para>
/// Adding a <c>Completed</c> state later is:
/// <list type="number">
///   <item>Uncomment the <c>Completed</c> entry in <see cref="JobStatus"/>.</item>
///   <item>Add it to the <c>Active</c> row here, and add a <c>Completed</c> row
///   pointing at <c>Archived</c>.</item>
///   <item>Add a two-line <c>Complete</c> endpoint on <c>JobsController</c>
///   that delegates to the same transition helper as <c>Activate</c> /
///   <c>Archive</c>.</item>
/// </list>
/// Blazor picks the new button up automatically — its rendering iterates
/// this map rather than hard-coding the current targets.
/// </para>
///
/// <para>
/// Entries are ordered intentionally — the first target in a row is the
/// "primary" action the UI surfaces (Draft's primary is Activate, not
/// Archive); secondary actions render as regular buttons.
/// </para>
/// </summary>
public static class JobLifecycle
{
    public static readonly IReadOnlyDictionary<JobStatus, IReadOnlyList<JobStatus>> AllowedTransitions =
        new Dictionary<JobStatus, IReadOnlyList<JobStatus>>
        {
            [JobStatus.Draft]    = new[] { JobStatus.Active, JobStatus.Archived },
            [JobStatus.Active]   = new[] { JobStatus.Archived },
            // Archived → Active = "Restore". Resolves issue #25 — Archive was
            // originally one-way terminal, but users expected the reversible
            // pattern Tenant ships (Active↔Inactive). Same TargetsFor iteration
            // surfaces the Restore button automatically; (from, to) helpers in
            // JobDetail.razor distinguish the label/URL from a fresh Activate.
            [JobStatus.Archived] = new[] { JobStatus.Active },
        };

    /// <summary>
    /// True if a job in <paramref name="from"/> status can legally be
    /// moved to <paramref name="to"/>. Same-status is <c>true</c> — the
    /// transition endpoints treat "set to current status" as an
    /// idempotent no-op rather than an error.
    /// </summary>
    public static bool CanTransition(JobStatus from, JobStatus to)
    {
        if (from == to) return true;
        return AllowedTransitions.TryGetValue(from, out var targets)
            && targets.Contains(to);
    }

    /// <summary>
    /// Allowed non-self transitions out of <paramref name="from"/>.
    /// Consumers (Blazor) read this for button rendering.
    /// </summary>
    public static IReadOnlyList<JobStatus> TargetsFor(JobStatus from) =>
        AllowedTransitions.TryGetValue(from, out var targets)
            ? targets
            : Array.Empty<JobStatus>();
}

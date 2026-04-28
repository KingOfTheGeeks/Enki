using SDI.Enki.Core.TenantDb.Runs.Enums;

namespace SDI.Enki.Core.TenantDb.Runs;

/// <summary>
/// Single source of truth for what <see cref="RunStatus"/> transitions
/// are allowed. Both <c>RunsController</c> (enforcement) and the
/// Blazor detail page (button rendering) read from this map, so
/// there's exactly one place to edit when the workflow grows.
///
/// <para>
/// Drilling-domain shape (richer than <c>JobLifecycle</c>'s simple
/// Draft→Active→Archived because a run is an operational thing —
/// rigs pause, suspend, cancel mid-flight):
/// <list type="bullet">
///   <item><c>Planned</c>: → Active (start the run), → Cancelled (abandon before start).</item>
///   <item><c>Active</c>: → Suspended (pause), → Completed (finish normally), → Cancelled (abandon mid-run).</item>
///   <item><c>Suspended</c>: → Active (resume), → Cancelled (abandon while paused).</item>
///   <item><c>Completed</c>: terminal (run finished; archive via soft-delete if you want it out of the list).</item>
///   <item><c>Cancelled</c>: terminal (run abandoned; archive via soft-delete to clean up the list).</item>
/// </list>
/// </para>
///
/// <para>
/// Soft-delete (archive) is orthogonal to lifecycle — both terminal
/// states (Completed, Cancelled) keep the row visible until an admin
/// archives it via <c>RunsController.Delete</c>. Restore is via the
/// matching endpoint; the lifecycle status is preserved through the
/// archive/restore cycle.
/// </para>
/// </summary>
public static class RunLifecycle
{
    public static readonly IReadOnlyDictionary<RunStatus, IReadOnlyList<RunStatus>> AllowedTransitions =
        new Dictionary<RunStatus, IReadOnlyList<RunStatus>>
        {
            [RunStatus.Planned]   = new[] { RunStatus.Active,    RunStatus.Cancelled },
            [RunStatus.Active]    = new[] { RunStatus.Completed, RunStatus.Suspended, RunStatus.Cancelled },
            [RunStatus.Suspended] = new[] { RunStatus.Active,    RunStatus.Cancelled },
            [RunStatus.Completed] = Array.Empty<RunStatus>(),
            [RunStatus.Cancelled] = Array.Empty<RunStatus>(),
        };

    /// <summary>
    /// True if a run in <paramref name="from"/> status can legally be
    /// moved to <paramref name="to"/>. Same-status is <c>true</c> — the
    /// transition endpoints treat "set to current status" as an
    /// idempotent no-op rather than an error.
    /// </summary>
    public static bool CanTransition(RunStatus from, RunStatus to)
    {
        if (from == to) return true;
        return AllowedTransitions.TryGetValue(from, out var targets)
            && targets.Contains(to);
    }

    /// <summary>
    /// Allowed non-self transitions out of <paramref name="from"/>.
    /// Consumers (Blazor) read this for button rendering — adding a
    /// new target here surfaces a button automatically without UI
    /// template changes.
    /// </summary>
    public static IReadOnlyList<RunStatus> TargetsFor(RunStatus from) =>
        AllowedTransitions.TryGetValue(from, out var targets)
            ? targets
            : Array.Empty<RunStatus>();
}

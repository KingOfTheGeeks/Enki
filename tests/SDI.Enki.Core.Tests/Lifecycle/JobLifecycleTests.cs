using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;

namespace SDI.Enki.Core.Tests.Lifecycle;

/// <summary>
/// Domain-level contract tests for <see cref="JobLifecycle"/>. Lives in
/// Core.Tests because the rules belong to the domain, not the controller
/// — the same tests should pin the behaviour regardless of how many
/// controllers, command handlers, or background workers consume the
/// transition map.
///
/// <para>
/// The most important test is <see cref="EveryJobStatus_HasAnAllowedTransitionsEntry"/>
/// — it fails the build the moment a new <see cref="JobStatus"/> is
/// added without a matching row in the dictionary, which would
/// otherwise be a silent "TryGetValue returns false → CanTransition
/// returns false → every transition out of the new state is denied"
/// failure mode. Mirrors the
/// <c>UnitSystemPresetsTests.EveryPresetCoversEveryQuantity</c> guard.
/// </para>
/// </summary>
public class JobLifecycleTests
{
    [Fact]
    public void EveryJobStatus_HasAnAllowedTransitionsEntry()
    {
        var missing = JobStatus.List
            .Where(s => !JobLifecycle.AllowedTransitions.ContainsKey(s))
            .Select(s => s.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "JobStatus values missing from JobLifecycle.AllowedTransitions:\n  " +
            string.Join("\n  ", missing));
    }

    public static IEnumerable<object[]> AllStatuses =>
        JobStatus.List.Select(s => new object[] { s.Value });

    public static IEnumerable<object[]> ListedTransitions =>
        from kv in JobLifecycle.AllowedTransitions
        from to in kv.Value
        select new object[] { kv.Key.Value, to.Value };

    public static IEnumerable<object[]> UnlistedNonSelfTransitions =>
        from from_ in JobStatus.List
        from to    in JobStatus.List
        where from_ != to
        where !(JobLifecycle.AllowedTransitions.TryGetValue(from_, out var allowed)
              && allowed.Contains(to))
        select new object[] { from_.Value, to.Value };

    [Theory]
    [MemberData(nameof(ListedTransitions))]
    public void AllowsListedTransitions(int fromValue, int toValue)
    {
        var from_ = JobStatus.FromValue(fromValue);
        var to    = JobStatus.FromValue(toValue);
        Assert.True(JobLifecycle.CanTransition(from_, to),
            $"Expected {from_.Name} → {to.Name} to be allowed (it's listed in AllowedTransitions).");
    }

    [Theory]
    [MemberData(nameof(UnlistedNonSelfTransitions))]
    public void DeniesUnlistedTransitions(int fromValue, int toValue)
    {
        var from_ = JobStatus.FromValue(fromValue);
        var to    = JobStatus.FromValue(toValue);
        Assert.False(JobLifecycle.CanTransition(from_, to),
            $"Expected {from_.Name} → {to.Name} to be denied (it's not in AllowedTransitions).");
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void SelfTransitionIsAlwaysAllowed(int statusValue)
    {
        // Idempotency contract — controller treats same-target as a
        // 204 no-op rather than 409, so CanTransition must agree.
        var s = JobStatus.FromValue(statusValue);
        Assert.True(JobLifecycle.CanTransition(s, s),
            $"Self-transition for {s.Name} must always be allowed.");
    }

    [Fact]
    public void TargetsFor_Archived_OffersRestoreToActive()
    {
        // Archived → Active is the "Restore" transition (issue #25).
        // Mirrors Tenant's reversible Inactive→Active reactivation.
        // Surfaces in the UI as a Restore button on Archived jobs;
        // the controller exposes /restore as a distinct verb so the
        // audit row carries "Restored" rather than "Activated".
        var targets = JobLifecycle.TargetsFor(JobStatus.Archived);
        Assert.Single(targets);
        Assert.Equal(JobStatus.Active, targets[0]);
    }
}

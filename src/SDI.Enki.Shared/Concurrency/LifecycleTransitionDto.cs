using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Concurrency;

/// <summary>
/// Body shape for state-machine lifecycle endpoints — Activate / Archive
/// (Job), Start / Suspend / Complete / Cancel / Restore (Run), Deactivate
/// / Reactivate (Tenant), Retire / Reactivate (Tool), Restore (Well),
/// and equivalents elsewhere. Carries only the caller's last-seen
/// <see cref="RowVersion"/> so the same optimistic-concurrency pattern
/// used by <c>Update*Dto</c> applies to lifecycle transitions too.
///
/// <para>
/// <b>Why RowVersion on a lifecycle action:</b> a state-machine check
/// alone (e.g. <c>JobLifecycle.CanTransition</c>) only refuses
/// structurally invalid moves (Archived → Draft → 409). It does not
/// catch the case where a different writer has *already moved through*
/// the state the caller observed — Tab A activates Draft → Active,
/// Tab B (still showing Draft) clicks Archive, the request lands on
/// the now-Active row, Active → Archived is structurally valid, the
/// archive proceeds without the caller knowing the activate happened.
/// Pinning RowVersion makes that case a 409, with the same
/// reload-and-retry copy as field edits.
/// </para>
/// </summary>
public sealed record LifecycleTransitionDto(
    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion = null);

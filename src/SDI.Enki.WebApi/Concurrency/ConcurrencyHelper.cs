using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Concurrency;

/// <summary>
/// Helpers for the optimistic-concurrency wire pattern. Every PUT /
/// PATCH-style endpoint accepts the client's last-seen
/// <c>RowVersion</c> (base64-encoded byte sequence on the wire),
/// applies it to the loaded entity before SaveChanges so EF generates
/// a <c>WHERE rowversion = @v</c> constraint, and surfaces a conflict
/// as RFC 7807 409 instead of letting EF's
/// <see cref="DbUpdateConcurrencyException"/> bubble to the global
/// handler.
///
/// <para>
/// <b>Wire format:</b> base64-encoded <see cref="byte"/>[]. SQL Server
/// <c>rowversion</c> is 8 bytes; base64 encodes that as 12 ASCII
/// characters. JSON-on-the-wire treats <c>byte[]</c> as base64
/// automatically with <c>System.Text.Json</c>, but the DTOs declare
/// it as <c>string</c> so the form-post path (Blazor static-SSR
/// EditForm + <c>[SupplyParameterFromForm]</c>) round-trips through a
/// hidden text field cleanly. Both shapes resolve to the same bytes
/// after the controller does the <see cref="Convert.FromBase64String"/>.
/// </para>
///
/// <para>
/// <b>Trade-off acknowledged:</b> when a controller's auto-recalc
/// path (e.g. <c>SurveysController.Update</c> →
/// <c>MardukSurveyAutoCalculator</c>) touches sibling rows on the
/// same parent, those siblings' rowversions also bump. A second user
/// editing one of the siblings then sees a 409 on save — even though
/// they didn't directly collide with the first user's edit. This
/// is <i>still</i> a meaningful conflict, not a spurious one: the
/// second user's view of the trajectory was invalidated by the
/// first user's edit (every downstream computed column is stale).
/// The reload-and-retry UX prompt is correct.
/// </para>
/// </summary>
internal static class ConcurrencyHelper
{
    /// <summary>
    /// Apply the client's last-seen RowVersion to a loaded entity,
    /// returning a 400 ValidationProblem result on a malformed or
    /// missing token (the caller should propagate via
    /// <c>return earlyResult;</c>).
    ///
    /// <para>
    /// On success both the entity's <see cref="IAuditable.RowVersion"/>
    /// (current value) and EF's tracked <c>OriginalValue</c> for the
    /// RowVersion property are overwritten with the supplied bytes,
    /// so EF's subsequent SaveChanges generates the WHERE clause as
    /// <c>rowversion = @client_value</c>, not <c>rowversion = @loaded_value</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Why setting both matters:</b> EF Core's concurrency check uses
    /// <c>OriginalValue</c> in the WHERE clause for IsRowVersion / IsConcurrencyToken
    /// properties (see https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
    /// If only the entity's CurrentValue is overwritten — as the original
    /// version of this helper did — the WHERE compares against whatever was
    /// freshly loaded from the database in the controller's request-scoped
    /// DbContext. That value is the post-other-writer state, so the
    /// concurrency check passes and a stale-version save silently wins.
    /// Setting <c>OriginalValue</c> is what actually pins the WHERE to the
    /// client's last-seen version.
    /// </para>
    ///
    /// <para>
    /// Empty / whitespace token rejects with 400 rather than
    /// silently degrading to last-write-wins. Every Update DTO
    /// declares the field <c>[Required]</c> so this branch only
    /// fires when a programmer bypassed the model validator.
    /// </para>
    /// </summary>
    public static IActionResult? ApplyClientRowVersion(
        this ControllerBase controller,
        DbContext db,
        IAuditable entity,
        string? clientRowVersion)
    {
        if (string.IsNullOrWhiteSpace(clientRowVersion))
        {
            return controller.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rowVersion"] = ["RowVersion is required for optimistic concurrency."],
            });
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(clientRowVersion);
        }
        catch (FormatException)
        {
            return controller.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rowVersion"] = ["RowVersion must be a base64-encoded byte sequence."],
            });
        }

        // Pin both — OriginalValue is what EF compares in the WHERE clause
        // for the concurrency check; CurrentValue keeps the in-memory entity
        // observation consistent with what the client thinks the row is.
        db.Entry(entity).Property(nameof(IAuditable.RowVersion)).OriginalValue = bytes;
        entity.RowVersion = bytes;
        return null;
    }

    /// <summary>
    /// SaveChanges with concurrency-conflict translation. On
    /// <see cref="DbUpdateConcurrencyException"/> returns a 409
    /// ConflictProblem with a reload-and-retry message. <c>null</c>
    /// on success.
    ///
    /// <para>
    /// <paramref name="entityKind"/> appears in the conflict
    /// message; pass the user-facing noun (Survey, Tie-on, Well, …)
    /// rather than the internal class name.
    /// </para>
    /// </summary>
    public static async Task<IActionResult?> SaveOrConflictAsync(
        this DbContext db,
        ControllerBase controller,
        string entityKind,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return controller.ConflictProblem(
                $"The {entityKind} was modified by another user since you loaded it. " +
                $"Reload to see the latest values, then re-apply your edit.");
        }
    }

    /// <summary>
    /// Encode an entity's RowVersion bytes as the wire-format base64
    /// string clients should round-trip back on the next mutation.
    /// Returns <c>null</c> if the entity hasn't been saved yet (no
    /// RowVersion assigned).
    /// </summary>
    public static string? EncodeRowVersion(this IAuditable entity) =>
        EncodeRowVersion(entity.RowVersion);

    /// <summary>
    /// byte[] overload — useful in two-stage projections where the
    /// EF query yields anonymous types carrying the raw RowVersion
    /// column, and the post-query map encodes it without an
    /// IAuditable handle.
    /// </summary>
    public static string? EncodeRowVersion(byte[]? rowVersion) =>
        rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : null;
}

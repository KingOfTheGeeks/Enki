using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Concurrency;

/// <summary>
/// Identity-host equivalent of <c>SDI.Enki.WebApi.Concurrency.ConcurrencyHelper</c>
/// for the <see cref="ApplicationUser.ConcurrencyStamp"/> token (rotated on every
/// save by ASP.NET Identity's <c>UserStore</c>).
///
/// <para>
/// Same OriginalValue-vs-CurrentValue trap as the WebApi side: EF Core's
/// concurrency check on a configured token reads the entity entry's
/// <c>OriginalValue</c>, not the property's current value. Setting only
/// <c>user.ConcurrencyStamp = client</c> would be silently no-op'd when
/// the request-scoped DbContext loaded a fresh stamp from the database
/// (= the post-other-writer state) — the stale-stamp save would then
/// pass the WHERE check and last-writer-win.
/// </para>
///
/// <para>
/// We pin <c>OriginalValue</c> directly. <c>UserManager.UpdateAsync</c>
/// rotates the stamp's <c>CurrentValue</c> on the way through, so
/// setting CurrentValue here would just be overwritten and is omitted.
/// </para>
/// </summary>
internal static class IdentityConcurrencyHelper
{
    /// <summary>
    /// Apply the client's last-seen <c>ConcurrencyStamp</c> to a tracked
    /// <see cref="ApplicationUser"/> so the next <c>UserManager</c>
    /// save's WHERE clause is <c>ConcurrencyStamp = @client</c>, not
    /// <c>= @loaded</c>.
    ///
    /// <para>
    /// Returns a 400 ValidationProblem on missing / whitespace input.
    /// Caller should propagate via <c>return earlyResult;</c>.
    /// </para>
    /// </summary>
    public static IActionResult? ApplyClientConcurrencyStamp(
        this ControllerBase controller,
        ApplicationDbContext db,
        ApplicationUser user,
        string? clientStamp)
    {
        if (string.IsNullOrWhiteSpace(clientStamp))
        {
            var modelState = new ModelStateDictionary();
            modelState.AddModelError(
                "concurrencyStamp",
                "ConcurrencyStamp is required for optimistic concurrency.");
            return controller.ValidationProblem(modelState);
        }

        // Pin OriginalValue — that's the value EF compares in the UPDATE's
        // WHERE clause. UserManager.UpdateAsync rotates CurrentValue on
        // the way through, so we don't bother setting it here.
        db.Entry(user).Property(u => u.ConcurrencyStamp).OriginalValue = clientStamp;
        return null;
    }
}

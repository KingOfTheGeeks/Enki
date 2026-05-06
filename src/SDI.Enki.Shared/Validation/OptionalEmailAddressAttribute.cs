using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Validation;

/// <summary>
/// <see cref="EmailAddressAttribute"/> that treats null AND empty / whitespace
/// strings as valid. Use on optional email properties bound from HTML forms,
/// where Blazor SSR's FormDataMapper sets the property to <c>""</c> (empty
/// string) for an unfilled input rather than null — and the stock
/// <see cref="EmailAddressAttribute"/> rejects <c>""</c>.
///
/// <para>
/// Defers to <see cref="EmailAddressAttribute"/> for actual format validation
/// once the value is non-empty, so the accepted formats stay identical to the
/// framework default.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class OptionalEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute Inner = new();

    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || Inner.IsValid(s);
}

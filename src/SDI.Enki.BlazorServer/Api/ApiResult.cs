using System.Diagnostics.CodeAnalysis;

namespace SDI.Enki.BlazorServer.Api;

/// <summary>
/// Success-or-error envelope for an Enki WebApi call that doesn't
/// return a body (POST returning 201 / 204, PUT returning 204,
/// DELETE returning 204). Use <see cref="ApiResult{T}"/> for calls
/// that return a typed body.
///
/// <para>
/// Pages branch on <see cref="IsSuccess"/>; on failure, render
/// <see cref="Error"/> via <c>AsAlertText()</c> or
/// <c>FieldErrors</c>.
/// </para>
/// </summary>
public sealed record ApiResult(ApiError? Error)
{
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static ApiResult Success { get; } = new((ApiError?)null);
    public static ApiResult Failure(ApiError error) => new(error);
}

/// <summary>
/// Success-or-error envelope for an Enki WebApi call that returns
/// a typed body. <see cref="Value"/> is the parsed body on success
/// and <c>default</c> on failure; <see cref="Error"/> is populated
/// on failure and <c>null</c> on success.
///
/// <para>
/// The <see cref="MemberNotNullWhenAttribute"/> hints let the
/// flow analyser narrow on <c>IsSuccess</c>:
/// <code>
/// if (result.IsSuccess) { use(result.Value); }   // Value not-null
/// else                  { show(result.Error);  } // Error not-null
/// </code>
/// </para>
/// </summary>
public sealed record ApiResult<T>(T? Value, ApiError? Error)
{
    [MemberNotNullWhen(true,  nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static ApiResult<T> Ok(T value)        => new(value,  null);
    public static ApiResult<T> Failure(ApiError error) => new(default, error);
}

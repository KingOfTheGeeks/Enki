using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;

namespace SDI.Enki.BlazorServer.Components.Shared;

public partial class ApiErrorAlert : ComponentBase
{
    /// <summary>
    /// Error to render. Null means no error — the component renders nothing.
    /// </summary>
    [Parameter] public ApiError? Error { get; set; }

    /// <summary>
    /// Per-field error keys come back from the API in the DTO property
    /// name's casing — e.g. "RowVersion", "Name", "FromVertical". Strip
    /// the leading "$." some serializers prefix on the root member,
    /// otherwise pass through verbatim — pages already use these casings
    /// in their labels.
    /// </summary>
    private static string HumanizeField(string key) =>
        key.StartsWith("$.") ? key[2..] : key;
}

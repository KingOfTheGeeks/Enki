using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Settings;

public sealed record SystemSettingDto(
    string  Key,
    string  Value,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

public sealed record SetSystemSettingDto(
    [Required] string Value);

/// <summary>
/// Authentication-light wire shape for the regions list. Returned by
/// <c>GET /jobs/region-suggestions</c> for any signed-in caller (the
/// list isn't sensitive — it's just suggestions).
/// </summary>
public sealed record RegionSuggestionsDto(IReadOnlyList<string> Regions);

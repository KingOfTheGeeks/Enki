using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tools.Enums;

/// <summary>
/// Why a tool left the active fleet. Drives the
/// <see cref="ToolStatus"/> mapping (only <see cref="Lost"/> flips a tool to
/// <see cref="ToolStatus.Lost"/>; every other value flips it to
/// <see cref="ToolStatus.Retired"/>) and lets reports filter retired tools
/// by reason without parsing free-text Notes.
/// </summary>
public sealed class ToolDisposition : SmartEnum<ToolDisposition>
{
    /// <summary>End-of-life retirement: tool reached the end of its useful life and is out of service.</summary>
    public static readonly ToolDisposition Retired         = new(nameof(Retired),         1);

    /// <summary>Lost in the field — last known location is downhole / customer site / unknown.</summary>
    public static readonly ToolDisposition Lost            = new(nameof(Lost),            2);

    /// <summary>Physically destroyed and disposed of — beyond economical repair.</summary>
    public static readonly ToolDisposition Scrapped        = new(nameof(Scrapped),        3);

    /// <summary>Sold to a third party. Final location captures the buyer.</summary>
    public static readonly ToolDisposition Sold            = new(nameof(Sold),            4);

    /// <summary>Transferred to a sister company / subsidiary / different SDI region.</summary>
    public static readonly ToolDisposition Transferred     = new(nameof(Transferred),     5);

    /// <summary>Returned to its owner — the tool was on loan / consignment, not SDI property.</summary>
    public static readonly ToolDisposition ReturnedToOwner = new(nameof(ReturnedToOwner), 6);

    private ToolDisposition(string name, int value) : base(name, value) { }
}

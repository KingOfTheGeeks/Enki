using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Tools.Enums;

/// <summary>
/// Hardware generation of an SDI downhole tool. Inferred from firmware
/// version + tool size at first sight (see Nabu's heuristic, ported into
/// <c>MasterDataSeeder</c>); persisted on the Tool row so the heuristic
/// doesn't need to re-run on every read and so an operator can override
/// the inferred value when a tool's calibration name disagrees with the
/// firmware-derived guess.
/// </summary>
public sealed class ToolGeneration : SmartEnum<ToolGeneration>
{
    public static readonly ToolGeneration Unknown = new(nameof(Unknown), 0);
    public static readonly ToolGeneration G1      = new(nameof(G1),      1);
    public static readonly ToolGeneration G2      = new(nameof(G2),      2);
    public static readonly ToolGeneration G4      = new(nameof(G4),      4);

    private ToolGeneration(string name, int value) : base(name, value) { }
}

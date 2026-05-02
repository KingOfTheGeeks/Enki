using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tools.Enums;

namespace SDI.Enki.Core.Master.Tools;

/// <summary>
/// Physical downhole tool owned by SDI. Backs Marduk's <c>IToolRegistry</c>.
/// Fleet-wide — the same tool serves many tenants across its lifetime,
/// so Tool lives in the master DB.
///
/// Implements <see cref="IAuditable"/> — CreatedBy / UpdatedBy /
/// RowVersion are managed by <c>EnkiMasterDbContext.SaveChangesAsync</c>;
/// don't set them from business code.
/// </summary>
public class Tool(int serialNumber, string firmwareVersion, int magnetometerCount, int accelerometerCount) : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int SerialNumber { get; set; } = serialNumber;

    public string FirmwareVersion { get; set; } = firmwareVersion;

    /// <summary>Marduk ToolConfiguration enum value.</summary>
    public int Configuration { get; set; }

    /// <summary>Marduk ToolSize enum value.</summary>
    public int Size { get; set; }

    public int MagnetometerCount { get; set; } = magnetometerCount;
    public int AccelerometerCount { get; set; } = accelerometerCount;

    /// <summary>
    /// Hardware generation. Inferred from firmware + size at seed time
    /// (see Nabu's heuristic). Stored as a column so we don't re-derive
    /// it on every read and so an operator can override the inferred
    /// value when the calibration record disagrees with the firmware
    /// guess.
    /// </summary>
    public ToolGeneration Generation { get; set; } = ToolGeneration.Unknown;

    public ToolStatus Status { get; set; } = ToolStatus.Active;

    public string? Notes { get; set; }

    // ---- Retirement (set when Status flips off Active; cleared on Reactivate). ----

    /// <summary>
    /// When the tool actually left service. Operator-supplied via the Retire
    /// dialog (defaults to today); separate from <see cref="UpdatedAt"/> so a
    /// tool retired weeks after the fact still records the real out-of-service
    /// date.
    /// </summary>
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>
    /// Operator who recorded the retirement. Frozen at retire time — later
    /// edits to other fields don't overwrite this.
    /// </summary>
    public string? RetiredBy { get; set; }

    /// <summary>
    /// Why the tool left service. Drives the UI pill, the
    /// <see cref="ToolStatus"/> mapping, and downstream "all tools sold in
    /// 2025" reports. Null on Active tools.
    /// </summary>
    public ToolDisposition? Disposition { get; set; }

    /// <summary>
    /// Operator-supplied free-text reason. Required by the UI; nullable here
    /// only because Active tools have nothing to say about their retirement.
    /// </summary>
    public string? RetirementReason { get; set; }

    /// <summary>Where the physical tool went (yard, customer site, scrap heap). Optional.</summary>
    public string? RetirementLocation { get; set; }

    /// <summary>
    /// FK to the tool that took this tool's place in the fleet. Optional —
    /// only set when retirement is a like-for-like swap-out. <c>Restrict</c>
    /// on delete (SQL Server rejects SET NULL on a self-FK when Tool already
    /// has a cascade FK from Calibration; the multi-cascade-path error). In
    /// practice Tool has no Delete endpoint anyway, so the constraint just
    /// guards the audit row.
    /// </summary>
    public Guid? ReplacementToolId { get; set; }

    /// <summary>EF nav for <see cref="ReplacementToolId"/>.</summary>
    public Tool? ReplacementTool { get; set; }

    // IAuditable — managed by the DbContext interceptor; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public ICollection<Calibration> Calibrations { get; set; } = new List<Calibration>();

    /// <summary>
    /// Heuristic for the hardware <see cref="ToolGeneration"/> derived from
    /// firmware + config + size — ported from Nabu's
    /// <c>ToolMetadata.Generation</c>. Used at seed time and when an operator
    /// creates a new tool without supplying an explicit override. Returns
    /// <see cref="ToolGeneration.Unknown"/> for combinations that don't
    /// match any known generation.
    /// </summary>
    public static ToolGeneration InferGeneration(int firmwareMajor, int firmwareMinor, int configuration, int size)
    {
        if (configuration == 3 || firmwareMajor == 0)
            return ToolGeneration.G1;
        if (firmwareMinor >= 90)
            return ToolGeneration.G4;
        if (firmwareMinor is >= 50 and <= 55 && size <= 114300)
            return ToolGeneration.G2;
        return ToolGeneration.Unknown;
    }

    /// <summary>
    /// String-firmware overload — parses "1.55" / "1.90" into major/minor
    /// then delegates. Returns Unknown if the firmware string isn't in the
    /// expected "{int}.{int}" shape.
    /// </summary>
    public static ToolGeneration InferGeneration(string firmwareVersion, int configuration, int size)
    {
        var parts = firmwareVersion.Split('.', 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor))
            return ToolGeneration.Unknown;
        return InferGeneration(major, minor, configuration, size);
    }
}

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

    // IAuditable — managed by the DbContext interceptor; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public ICollection<Calibration> Calibrations { get; set; } = new List<Calibration>();
}

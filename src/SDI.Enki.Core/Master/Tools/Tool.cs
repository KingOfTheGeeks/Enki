namespace SDI.Enki.Core.Master.Tools;

/// <summary>
/// Physical downhole tool owned by SDI. Backs Marduk's <c>IToolRegistry</c>.
/// Fleet-wide — the same tool serves many tenants across its lifetime,
/// so Tool lives in the master DB.
/// </summary>
public class Tool(int serialNumber, string firmwareVersion, int magnetometerCount, int accelerometerCount)
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

    // EF nav
    public ICollection<Calibration> Calibrations { get; set; } = new List<Calibration>();
}

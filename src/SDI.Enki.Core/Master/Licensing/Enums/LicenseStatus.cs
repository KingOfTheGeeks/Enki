using Ardalis.SmartEnum;

namespace SDI.Enki.Core.Master.Licensing.Enums;

/// <summary>
/// Lifecycle of a master <see cref="License"/> row. <c>Expired</c> is set
/// lazily by background sweep + at-render-time when <c>ExpiresAt</c> has
/// passed; <c>Revoked</c> is an admin action with a reason recorded.
/// </summary>
public sealed class LicenseStatus : SmartEnum<LicenseStatus>
{
    public static readonly LicenseStatus Active   = new(nameof(Active),   1);
    public static readonly LicenseStatus Revoked  = new(nameof(Revoked),  2);
    public static readonly LicenseStatus Expired  = new(nameof(Expired),  3);

    private LicenseStatus(string name, int value) : base(name, value) { }
}

namespace SDI.Enki.Shared.Calibrations;

/// <summary>
/// Field-for-field mirror of Marduk's <c>AMR.Core.Calibration.Models.ToolCalibration</c>
/// for client-side deserialization of <c>Calibration.PayloadJson</c>. We don't
/// reference Marduk from BlazorServer or Shared, so this is a local copy of
/// the JSON shape.
///
/// Per-magnetometer values are flat arrays interleaved by magnetometer
/// index; use the slicing helpers below to read a single mag's row.
/// </summary>
public sealed record ToolCalibrationPayload(
    Guid     Id,
    Guid     ToolId,
    string   Name,
    int      MagnetometerCount,
    DateTime CalibrationDate,
    string?  CalibratedBy,

    // Accelerometer.
    // Nabu's JSON serialises the permutation matrix entries as doubles
    // (e.g. [[0.0, -1.0, 0.0], …]) even though they're always integral.
    // Marduk's ToolCalibration.AccelerometerAxisPermutation is int[][];
    // we deserialise as double[][] here so System.Text.Json doesn't reject
    // the float tokens, and never round-trip back into Marduk from this
    // record (page is read-only).
    double[][] AccelerometerAxisPermutation,          // 3x3
    double[]   AccelerometerBias,                     // length 3
    double[]   AccelerometerScaleFactor,              // length 6 (3 diag + 3 cross)
    double[]   AccelerometerAlignmentAngles,          // length 3

    // Magnetometer (flat arrays — N mags * stride)
    double[][][] MagnetometerAxisPermutation,         // [nMags][3][3]
    double[]     MagnetometerBias,                    // length nMags*3
    double[]     MagnetometerScaleFactor,             // length nMags*6
    double[]     MagnetometerAlignmentAngles,         // length nMags*3
    double[]     MagnetometerLocations);              // length nMags*3

/// <summary>
/// Per-magnetometer slicing helpers — match Nabu's
/// <c>CalibrationData.GetMag*</c> helpers so the JSON layout stays
/// authoritative and pages don't have to remember the strides.
/// </summary>
public static class ToolCalibrationPayloadExtensions
{
    public static double[] GetMagBias(this ToolCalibrationPayload p, int magIndex) =>
        Slice(p.MagnetometerBias, magIndex * 3, 3);

    /// <summary>Diagonal scale factors (Sx, Sy, Sz) — first 3 of the 6-vector.</summary>
    public static double[] GetMagScaleDiagonal(this ToolCalibrationPayload p, int magIndex) =>
        Slice(p.MagnetometerScaleFactor, magIndex * 6, 3);

    /// <summary>Cross-coupling scale (Sxy, Sxz, Syz) — last 3 of the 6-vector.</summary>
    public static double[] GetMagScaleCrossCoupling(this ToolCalibrationPayload p, int magIndex) =>
        Slice(p.MagnetometerScaleFactor, magIndex * 6 + 3, 3);

    public static double[] GetMagAlignment(this ToolCalibrationPayload p, int magIndex) =>
        Slice(p.MagnetometerAlignmentAngles, magIndex * 3, 3);

    public static double[] GetMagLocation(this ToolCalibrationPayload p, int magIndex) =>
        Slice(p.MagnetometerLocations, magIndex * 3, 3);

    private static double[] Slice(double[] src, int start, int length)
    {
        if (start + length > src.Length) return Array.Empty<double>();
        var result = new double[length];
        Array.Copy(src, start, result, 0, length);
        return result;
    }
}

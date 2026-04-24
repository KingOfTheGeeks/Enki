namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Computed position + orientation for a single <see cref="Gradient"/>
/// measurement. Produced by Marduk's <c>IGradientProcessor.CreateSolution</c>;
/// Enki persists the result. Multiple solutions per Gradient are allowed
/// (iterative refinement / sign-flipped alternates).
/// </summary>
public class GradientSolution
{
    public int Id { get; set; }

    public int GradientId { get; set; }

    public double MeasuredDepth { get; set; }
    public double Inclination { get; set; }
    public double Azimuth { get; set; }
    public double Toolface { get; set; }

    public double MdToTarget { get; set; }
    public double AziToTarget { get; set; }
    public double TfToTarget { get; set; }

    public bool SignFlipped { get; set; }

    // EF nav
    public Gradient? Gradient { get; set; }
}

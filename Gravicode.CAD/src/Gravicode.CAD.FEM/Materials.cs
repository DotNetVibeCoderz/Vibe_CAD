namespace Gravicode.CAD.FEM;

/// <summary>Isotropic linear-elastic material. Units: N, mm, MPa (N/mm²) by convention.</summary>
public sealed record Material(string Name, double YoungsModulus, double PoissonRatio, double Density = 0, double YieldStrength = double.PositiveInfinity, double Conductivity = 0)
{
    /// <summary>Structural steel S235: E = 210 GPa.</summary>
    public static readonly Material Steel = new("Steel S235", 210_000, 0.30, 7.85e-9, 235, 0.05);
    public static readonly Material StainlessSteel = new("Stainless 304", 193_000, 0.29, 8.0e-9, 215, 0.016);
    public static readonly Material Aluminum = new("Aluminium 6061-T6", 68_900, 0.33, 2.7e-9, 276, 0.167);
    public static readonly Material Titanium = new("Titanium Ti-6Al-4V", 113_800, 0.342, 4.43e-9, 880, 0.0067);
    public static readonly Material Pla = new("PLA", 3_500, 0.36, 1.24e-9, 50, 0.00013);
    public static readonly Material Abs = new("ABS", 2_300, 0.35, 1.05e-9, 40, 0.00017);
    public static readonly Material Concrete = new("Concrete C30", 33_000, 0.2, 2.4e-9, 30, 0.0017);
    public static readonly Material Wood = new("Wood (pine, along grain)", 11_000, 0.3, 0.5e-9, 40, 0.00012);
}

namespace Gravicode.CAD;

/// <summary>Axis-aligned 3D bounding box.</summary>
public readonly record struct BoundingBox(Vec3 Min, Vec3 Max)
{
    public static readonly BoundingBox Empty = new(
        new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vec3 Size => IsEmpty ? Vec3.Zero : Max - Min;
    public Vec3 Center => IsEmpty ? Vec3.Zero : (Min + Max) * 0.5;
    public double Diagonal => Size.Length;

    public BoundingBox Include(Vec3 p) => new(Vec3.Min(Min, p), Vec3.Max(Max, p));
    public BoundingBox Union(BoundingBox o) => o.IsEmpty ? this : IsEmpty ? o : new(Vec3.Min(Min, o.Min), Vec3.Max(Max, o.Max));
    public bool Contains(Vec3 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
    public bool Intersects(BoundingBox o) => !(o.Min.X > Max.X || o.Max.X < Min.X || o.Min.Y > Max.Y || o.Max.Y < Min.Y || o.Min.Z > Max.Z || o.Max.Z < Min.Z);
    public BoundingBox Inflate(double d) => new(Min - new Vec3(d, d, d), Max + new Vec3(d, d, d));

    public static BoundingBox FromPoints(IEnumerable<Vec3> pts)
    {
        var b = Empty;
        foreach (var p in pts) b = b.Include(p);
        return b;
    }

    public BoundingBox Transform(Mat4 m)
    {
        if (IsEmpty) return this;
        var b = Empty;
        for (int i = 0; i < 8; i++)
            b = b.Include(m.TransformPoint(new Vec3((i & 1) == 0 ? Min.X : Max.X, (i & 2) == 0 ? Min.Y : Max.Y, (i & 4) == 0 ? Min.Z : Max.Z)));
        return b;
    }

    public override string ToString() => IsEmpty ? "Empty" : $"[{Min} .. {Max}] size {Size}";
}

/// <summary>Infinite plane n·p = d.</summary>
public readonly record struct Plane(Vec3 Normal, double D)
{
    public static Plane FromPointNormal(Vec3 p, Vec3 n) { var nn = n.Normalized(); return new(nn, Vec3.Dot(nn, p)); }
    public static Plane FromPoints(Vec3 a, Vec3 b, Vec3 c) => FromPointNormal(a, Vec3.Cross(b - a, c - a));
    public static readonly Plane XY = new(Vec3.UnitZ, 0);
    public static readonly Plane XZ = new(Vec3.UnitY, 0);
    public static readonly Plane YZ = new(Vec3.UnitX, 0);
    public double SignedDistance(Vec3 p) => Vec3.Dot(Normal, p) - D;
    public Vec3 Project(Vec3 p) => p - Normal * SignedDistance(p);
    public Plane Flipped() => new(-Normal, -D);
}

/// <summary>Length units understood by the importers/exporters.</summary>
public enum LengthUnit { Millimeter, Centimeter, Meter, Inch, Foot, Micrometer }

public static class Units
{
    /// <summary>Millimeters per unit.</summary>
    public static double ToMillimeters(this LengthUnit u) => u switch
    {
        LengthUnit.Millimeter => 1,
        LengthUnit.Centimeter => 10,
        LengthUnit.Meter => 1000,
        LengthUnit.Inch => 25.4,
        LengthUnit.Foot => 304.8,
        LengthUnit.Micrometer => 0.001,
        _ => 1
    };

    /// <summary>Factor converting a value in <paramref name="from"/> into <paramref name="to"/>.</summary>
    public static double Factor(LengthUnit from, LengthUnit to) => from.ToMillimeters() / to.ToMillimeters();
    public static double Deg(double degrees) => degrees * Math.PI / 180.0;
    public static double ToDegrees(double radians) => radians * 180.0 / Math.PI;

    public static string Symbol(this LengthUnit u) => u switch
    {
        LengthUnit.Millimeter => "mm", LengthUnit.Centimeter => "cm", LengthUnit.Meter => "m",
        LengthUnit.Inch => "in", LengthUnit.Foot => "ft", LengthUnit.Micrometer => "um", _ => "?"
    };
}

/// <summary>Linear RGBA color with components in [0,1].</summary>
public readonly record struct CadColor(double R, double G, double B, double A = 1)
{
    public static CadColor FromRgb(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);

    public static CadColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        var v = Convert.ToUInt32(hex[..6], 16);
        double a = hex.Length >= 8 ? Convert.ToByte(hex.Substring(6, 2), 16) / 255.0 : 1;
        return new(((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0, a);
    }

    public string ToHex() => $"#{B2(R):X2}{B2(G):X2}{B2(B):X2}";
    private static byte B2(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    public CadColor Scale(double f) => new(R * f, G * f, B * f, A);
    public static CadColor Lerp(CadColor a, CadColor b, double t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    public static readonly CadColor Steel = FromHex("#9AA7B4");
    public static readonly CadColor Aluminum = FromHex("#C9D1D9");
    public static readonly CadColor Brass = FromHex("#D4A84B");
    public static readonly CadColor Copper = FromHex("#C8734A");
    public static readonly CadColor Blueprint = FromHex("#2F6FDE");
    public static readonly CadColor Orange = FromHex("#FF7A1A");
    public static readonly CadColor Graphite = FromHex("#3A3F47");
    public static readonly CadColor White = new(1, 1, 1);
    public static readonly CadColor Black = new(0, 0, 0);
}

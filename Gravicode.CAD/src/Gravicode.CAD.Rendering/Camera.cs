namespace Gravicode.CAD.Rendering;

/// <summary>Orbit camera around a target (Z-up). Shared by the software renderer and the ThreeNet / Three.js adapters.</summary>
public sealed class OrbitCamera
{
    public Vec3 Target { get; set; }
    public double Distance { get; set; } = 100;
    /// <summary>Azimuth in degrees (0 = looking along +Y from -Y).</summary>
    public double Yaw { get; set; } = -35;
    /// <summary>Elevation in degrees.</summary>
    public double Pitch { get; set; } = 28;
    public double FieldOfView { get; set; } = 35;
    public bool Orthographic { get; set; }
    /// <summary>Visible height in model units for orthographic projection.</summary>
    public double OrthoHeight { get; set; } = 100;

    public Vec3 Position
    {
        get
        {
            double y = Units.Deg(Yaw), p = Units.Deg(Pitch);
            var dir = new Vec3(Math.Sin(y) * Math.Cos(p), -Math.Cos(y) * Math.Cos(p), Math.Sin(p));
            return Target + dir * Distance;
        }
    }

    /// <summary>Frames a bounding box (keeps yaw/pitch).</summary>
    public OrbitCamera Fit(BoundingBox bb, double margin = 1.15)
    {
        if (bb.IsEmpty) return this;
        Target = bb.Center;
        double r = Math.Max(bb.Diagonal / 2, 1e-6) * margin;
        Distance = r / Math.Sin(Units.Deg(FieldOfView) / 2);
        OrthoHeight = r * 2;
        return this;
    }

    public static OrbitCamera Isometric(BoundingBox bb) => new OrbitCamera { Yaw = -45, Pitch = 35.264 }.Fit(bb);

    /// <summary>World → camera basis (right, up, forward).</summary>
    public (Vec3 Right, Vec3 Up, Vec3 Forward) Basis()
    {
        var f = (Target - Position).Normalized();
        var r = Vec3.Cross(f, Vec3.UnitZ);
        if (r.LengthSquared < 1e-12) r = Vec3.UnitX;
        r = r.Normalized();
        var u = Vec3.Cross(r, f).Normalized();
        return (r, u, f);
    }
}

/// <summary>Look settings for renders.</summary>
public sealed record RenderOptions
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 800;
    /// <summary>Supersampling factor (2 = 4 samples per pixel).</summary>
    public int Supersample { get; init; } = 2;
    public OrbitCamera? Camera { get; init; }
    public CadColor BackgroundTop { get; init; } = CadColor.FromHex("#1B2230");
    public CadColor BackgroundBottom { get; init; } = CadColor.FromHex("#0B0E14");
    public bool TransparentBackground { get; init; }
    public bool DrawEdges { get; init; } = true;
    public CadColor EdgeColor { get; init; } = CadColor.FromHex("#0E1116");
    public double EdgeAngle { get; init; } = 30;
    public bool GroundShadow { get; init; } = true;
    public bool Grid { get; init; } = true;
    public CadColor GridColor { get; init; } = CadColor.FromHex("#2A3445");
    /// <summary>Overrides all body colors (e.g. for monochrome icons).</summary>
    public CadColor? OverrideColor { get; init; }
    public double Exposure { get; init; } = 1.0;

    public static RenderOptions Light => new()
    {
        BackgroundTop = CadColor.FromHex("#FFFFFF"), BackgroundBottom = CadColor.FromHex("#E8ECF2"),
        GridColor = CadColor.FromHex("#D5DBE5"), EdgeColor = CadColor.FromHex("#20252C")
    };
}

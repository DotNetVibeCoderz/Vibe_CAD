using System.Numerics;
using Avalonia;
using Gravicode.CAD.ThreeNet;

namespace Gravicode.CAD.Studio.Desktop.Controls;

/// <summary>
/// Snapshot of the orbit camera that drives the ThreeNet view, with the same conventions (Y-up world, vertical
/// field of view, camera on a sphere around <see cref="Target"/>). Projects CAD points to view pixels and turns
/// pixels into picking rays, so overlays and picking line up with what ThreeNet draws.
/// </summary>
public readonly record struct ViewCamera(Vector3 Target, float Distance, float Yaw, float Pitch, float FovY, Size Viewport)
{
    public Vector3 Position
    {
        get
        {
            float cp = MathF.Cos(Pitch);
            return Target + new Vector3(MathF.Sin(Yaw) * cp, MathF.Sin(Pitch), MathF.Cos(Yaw) * cp) * Distance;
        }
    }

    private (Vector3 F, Vector3 R, Vector3 U) Basis()
    {
        var f = Vector3.Normalize(Target - Position);
        var r = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
        return (f, r, Vector3.Cross(r, f));
    }

    /// <summary>Pixels per world unit at depth 1.</summary>
    private double Focal => Viewport.Height / 2 / Math.Tan(FovY / 2);

    /// <summary>View pixel of a CAD point, and its depth (≤ 0 = behind the camera).</summary>
    public (Point P, double Depth) Project(Vec3 cad)
    {
        var (f, r, u) = Basis();
        var v = CadScene.ToWorld(cad) - Position;
        double z = Vector3.Dot(v, f);
        if (z <= 1e-9) return (new Point(double.NaN, double.NaN), z);
        double k = Focal / z;
        return (new Point(Viewport.Width / 2 + Vector3.Dot(v, r) * k, Viewport.Height / 2 - Vector3.Dot(v, u) * k), z);
    }

    /// <summary>World units covered by <paramref name="pixels"/> at the depth of <paramref name="cad"/>.</summary>
    public double UnitsForPixels(Vec3 cad, double pixels)
    {
        var (f, _, _) = Basis();
        double z = Math.Max(Vector3.Dot(CadScene.ToWorld(cad) - Position, f), 1e-6);
        return pixels * z / Focal;
    }

    /// <summary>Picking ray through a view pixel, in CAD space.</summary>
    public (Vec3 Origin, Vec3 Direction) Ray(Point p)
    {
        var (f, r, u) = Basis();
        double k = Focal;
        var d = f + r * (float)((p.X - Viewport.Width / 2) / k) + u * (float)((Viewport.Height / 2 - p.Y) / k);
        return (CadScene.ToCad(Position), CadScene.ToCad(Vector3.Normalize(d)));
    }

    /// <summary>True when a CAD direction points towards the viewer.</summary>
    public bool FacesViewer(Vec3 cadDir) => Vector3.Dot(CadScene.ToWorld(cadDir), Basis().F) < 0;
}

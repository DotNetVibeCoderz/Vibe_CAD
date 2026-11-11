using Gravicode.CAD.Studio.IO;
using Gravicode.CAD.Topology;

namespace Gravicode.CAD.Studio;

/// <summary>
/// A drawing plane in 3D (sketch on a face). Objects drawn while it is active live in its local 2D coordinates;
/// <see cref="Matrix"/> maps them into the model. <see cref="SolidId"/> is the solid the face belongs to (EXTRUDE
/// adds to or cuts it).
/// </summary>
public sealed record WorkPlane(string Key, string Name, Vec3 Origin, Vec3 XAxis, Vec3 YAxis, Guid? SolidId)
{
    public Vec3 Normal => Vec3.Cross(XAxis, YAxis).Normalized();
    public Mat4 Matrix => Mat4.FromBasis(Origin, XAxis, YAxis, Normal);
    public Vec3 ToWorld(Vec2 p) => Origin + XAxis * p.X + YAxis * p.Y;
    public Vec2 ToLocal(Vec3 p) { var d = p - Origin; return new Vec2(Vec3.Dot(d, XAxis), Vec3.Dot(d, YAxis)); }
    public double Distance(Vec3 p) => Vec3.Dot(p - Origin, Normal);

    /// <summary>Plane on a solid's face: Y points "up" (world Z projected), X completes a right-handed frame, and
    /// the origin sits at the lower-left corner of the face's edges.</summary>
    public static WorkPlane OnFace(string key, string name, StudioSolid solid, Vec3 point, Vec3 normal)
    {
        var n = normal.Normalized();
        var up = Vec3.UnitZ - n * Vec3.Dot(Vec3.UnitZ, n);
        Vec3 y, x;
        if (up.Length < 0.1) { x = Vec3.UnitX - n * Vec3.Dot(Vec3.UnitX, n); x = x.Normalized(); y = Vec3.Cross(n, x).Normalized(); }
        else { y = up.Normalized(); x = Vec3.Cross(y, n).Normalized(); }
        var plane = new WorkPlane(key, name, point, x, y, solid.Id);
        var edges = FaceEdges(plane, solid.Mesh);
        if (edges.Count > 0)
        {
            double minX = edges.Min(e => Math.Min(e.A.X, e.B.X)), minY = edges.Min(e => Math.Min(e.A.Y, e.B.Y));
            plane = plane with { Origin = plane.ToWorld(new Vec2(minX, minY)) };
        }
        return plane;
    }

    /// <summary>Feature edges of a mesh lying in this plane, in local 2D coordinates (the face outline).</summary>
    public static List<(Vec2 A, Vec2 B)> FaceEdges(WorkPlane plane, Mesh mesh)
    {
        var tol = Math.Max(mesh.Bounds.Diagonal * 1e-6, 1e-6);
        var result = new List<(Vec2, Vec2)>();
        foreach (var (a, b) in new HalfEdgeMesh(mesh).FeatureEdges(20))
            if (Math.Abs(plane.Distance(a)) < tol && Math.Abs(plane.Distance(b)) < tol)
                result.Add((plane.ToLocal(a), plane.ToLocal(b)));
        return result;
    }
}

/// <summary>A window on paper showing model space at a scale.</summary>
/// <param name="Min">Lower-left corner on paper (mm).</param>
/// <param name="Max">Upper-right corner on paper (mm).</param>
/// <param name="ModelCenter">Model point shown at the window centre.</param>
/// <param name="Scale">Denominator of 1:N (paper mm per model millimetre equivalent).</param>
public sealed record LayoutViewport(Vec2 Min, Vec2 Max, Vec2 ModelCenter, double Scale);

/// <summary>Paper-space sheet: size, title block and viewports onto model space. Annotation objects drawn while
/// it is active are in paper millimetres.</summary>
public sealed record Layout(string Key, string Name, string Paper, bool Landscape, bool TitleBlock, IReadOnlyList<LayoutViewport> Viewports)
{
    public (double W, double H) Size
    {
        get
        {
            var (pw, ph) = Plotter.Papers.TryGetValue(Paper, out var s) ? s : Plotter.Papers["A3"];
            return Landscape ? (Math.Max(pw, ph), Math.Min(pw, ph)) : (Math.Min(pw, ph), Math.Max(pw, ph));
        }
    }
}

/// <summary>Space names and the non-editable reference geometry each space shows.</summary>
public static class Spaces
{
    public const string PlanePrefix = "plane:", LayoutPrefix = "layout:";

    /// <summary>Reference geometry for the active space (paper space: sheet, title block and the model seen
    /// through each viewport; face planes: the face outline). Empty in model space.</summary>
    public static List<(DrawingEntity Geometry, string Color)> Reference(StudioDocument doc)
    {
        var list = new List<(DrawingEntity, string)>();
        if (doc.ActivePlane is { } plane)
        {
            if (plane.SolidId is { } sid && doc.Solids.FirstOrDefault(s => s.Id == sid) is { } solid)
                foreach (var (a, b) in WorkPlane.FaceEdges(plane, solid.Mesh)) list.Add((new LineEntity(a, b), "#6E7F99"));
        }
        else if (doc.ActiveLayout is { } layout)
        {
            foreach (var g in Plotter.LayoutSheet(doc, layout, forScreen: true)) list.Add((g, g.Layer switch
            {
                "PAPER" => "#6E7F99",
                "VPORT" => "#7FD1FF",
                "BORDER" or "TITLE" => "#E6EDF3",
                "DIM" => "#7FD1FF",
                _ => g.Color is { } c ? c.ToHex() : "#C9D6E6"
            }));
        }
        return list;
    }

    /// <summary>Cuts a primitive to an axis-aligned window (Liang–Barsky on its tessellation); text is kept when its
    /// anchor is inside.</summary>
    public static IEnumerable<DrawingEntity> Clip(DrawingEntity e, Vec2 min, Vec2 max, double tol = 0.2)
    {
        if (e is TextEntity t)
        {
            if (t.Position.X >= min.X && t.Position.X <= max.X && t.Position.Y >= min.Y && t.Position.Y <= max.Y) yield return t;
            yield break;
        }
        var pts = e.Tessellate(tol);
        for (int i = 0; i + 1 < pts.Count; i++)
            if (ClipSegment(pts[i], pts[i + 1], min, max) is { } s) yield return new LineEntity(s.A, s.B) { Layer = e.Layer, Color = e.Color };
    }

    private static (Vec2 A, Vec2 B)? ClipSegment(Vec2 a, Vec2 b, Vec2 min, Vec2 max)
    {
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        bool Edge(double p, double q)
        {
            if (Math.Abs(p) < 1e-15) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
        if (!Edge(-dx, a.X - min.X) || !Edge(dx, max.X - a.X) || !Edge(-dy, a.Y - min.Y) || !Edge(dy, max.Y - a.Y)) return null;
        return (new Vec2(a.X + dx * t0, a.Y + dy * t0), new Vec2(a.X + dx * t1, a.Y + dy * t1));
    }
}

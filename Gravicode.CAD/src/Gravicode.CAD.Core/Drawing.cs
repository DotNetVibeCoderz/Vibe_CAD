namespace Gravicode.CAD;

/// <summary>Base class for 2D drafting entities (DXF/SVG/PDF drawings, sketches, toolpaths preview).</summary>
public abstract class DrawingEntity
{
    public string Layer { get; set; } = "0";
    public CadColor? Color { get; set; }

    /// <summary>Approximates the entity by a polyline (for exporters/renderers without native curve support).</summary>
    public abstract IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05);
    public abstract DrawingEntity Transformed(Func<Vec2, Vec2> map);

    public (Vec2 Min, Vec2 Max) Extents()
    {
        var pts = Tessellate(0.5);
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        return (new(x0, y0), new(x1, y1));
    }

    internal static int Segments(double radius, double sweep, double tol)
    {
        if (radius <= tol) return 4;
        double step = 2 * Math.Acos(Math.Clamp(1 - tol / radius, -1, 1));
        return Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / Math.Max(step, 1e-3)), 4, 720);
    }
}

public sealed class LineEntity(Vec2 a, Vec2 b) : DrawingEntity
{
    public Vec2 A { get; set; } = a;
    public Vec2 B { get; set; } = b;
    public double Length => Vec2.Distance(A, B);
    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05) => [A, B];
    public override DrawingEntity Transformed(Func<Vec2, Vec2> map) => new LineEntity(map(A), map(B)) { Layer = Layer, Color = Color };
}

public sealed class CircleEntity(Vec2 center, double radius) : DrawingEntity
{
    public Vec2 Center { get; set; } = center;
    public double Radius { get; set; } = radius;
    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05)
    {
        int n = Segments(Radius, 2 * Math.PI, chordTolerance);
        var r = new Vec2[n + 1];
        for (int i = 0; i <= n; i++) r[i] = Center + Vec2.FromAngle(2 * Math.PI * i / n, Radius);
        return r;
    }
    public override DrawingEntity Transformed(Func<Vec2, Vec2> map)
    {
        var c = map(Center);
        var r = Vec2.Distance(map(Center + new Vec2(Radius, 0)), c);
        return new CircleEntity(c, r) { Layer = Layer, Color = Color };
    }
}

/// <summary>Circular arc, counter-clockwise from <see cref="StartAngle"/> to <see cref="EndAngle"/> (radians).</summary>
public sealed class ArcEntity(Vec2 center, double radius, double startAngle, double endAngle) : DrawingEntity
{
    public Vec2 Center { get; set; } = center;
    public double Radius { get; set; } = radius;
    public double StartAngle { get; set; } = startAngle;
    public double EndAngle { get; set; } = endAngle;

    public double Sweep
    {
        get { var s = EndAngle - StartAngle; while (s <= 0) s += 2 * Math.PI; while (s > 2 * Math.PI + 1e-12) s -= 2 * Math.PI; return s; }
    }
    public Vec2 StartPoint => Center + Vec2.FromAngle(StartAngle, Radius);
    public Vec2 EndPoint => Center + Vec2.FromAngle(EndAngle, Radius);

    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05)
    {
        double sweep = Sweep;
        int n = Segments(Radius, sweep, chordTolerance);
        var r = new Vec2[n + 1];
        for (int i = 0; i <= n; i++) r[i] = Center + Vec2.FromAngle(StartAngle + sweep * i / n, Radius);
        return r;
    }
    public override DrawingEntity Transformed(Func<Vec2, Vec2> map)
    {
        // General maps may not keep arcs circular; fall back to a polyline when they don't.
        var c = map(Center);
        var s = map(StartPoint); var e = map(EndPoint);
        var rs = Vec2.Distance(s, c);
        var mid = map(Center + Vec2.FromAngle(StartAngle + Sweep / 2, Radius));
        bool ccw = Vec2.Cross(s - c, mid - c) > 0;
        if (Math.Abs(rs - Vec2.Distance(mid, c)) > 1e-6 * Math.Max(1, rs))
            return new PolylineEntity([.. Tessellate().Select(map)], false) { Layer = Layer, Color = Color };
        var a0 = (s - c).Angle; var a1 = (e - c).Angle;
        return ccw ? new ArcEntity(c, rs, a0, a1) { Layer = Layer, Color = Color } : new ArcEntity(c, rs, a1, a0) { Layer = Layer, Color = Color };
    }
}

public sealed class PolylineEntity(IEnumerable<Vec2> points, bool closed) : DrawingEntity
{
    public List<Vec2> Points { get; } = [.. points];
    public bool Closed { get; set; } = closed;
    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05) =>
        Closed && Points.Count > 0 ? [.. Points, Points[0]] : Points;
    public override DrawingEntity Transformed(Func<Vec2, Vec2> map) => new PolylineEntity(Points.Select(map), Closed) { Layer = Layer, Color = Color };
}

public sealed class TextEntity(Vec2 position, string text, double height) : DrawingEntity
{
    public Vec2 Position { get; set; } = position;
    public string Text { get; set; } = text;
    public double Height { get; set; } = height;
    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05) =>
        [Position, Position + new Vec2(Text.Length * Height * 0.6, Height)];
    public override DrawingEntity Transformed(Func<Vec2, Vec2> map) => new TextEntity(map(Position), Text, Height) { Layer = Layer, Color = Color };
}

public sealed class PointEntity(Vec2 position) : DrawingEntity
{
    public Vec2 Position { get; set; } = position;
    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05) => [Position];
    public override DrawingEntity Transformed(Func<Vec2, Vec2> map) => new PointEntity(map(Position)) { Layer = Layer, Color = Color };
}

/// <summary>3D polyline (wireframe edges from IGES/DXF, toolpaths, slices).</summary>
public sealed class Polyline3(IEnumerable<Vec3> points, bool closed = false)
{
    public List<Vec3> Points { get; } = [.. points];
    public bool Closed { get; set; } = closed;
    public string Layer { get; set; } = "0";
    public CadColor? Color { get; set; }
}

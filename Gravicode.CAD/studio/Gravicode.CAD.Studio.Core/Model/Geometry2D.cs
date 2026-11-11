using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Studio;

/// <summary>Aligned linear dimension between two points, drawn offset by <see cref="Offset"/>.</summary>
public sealed class DimensionEntity(Vec2 a, Vec2 b, double offset, double textHeight = 3.5) : DrawingEntity
{
    public Vec2 A { get; } = a;
    public Vec2 B { get; } = b;
    public double Offset { get; } = offset;
    public double TextHeight { get; } = textHeight;
    public double Value => Vec2.Distance(A, B);

    private Vec2 Dir => (B - A).Normalized();
    private Vec2 Normal => Dir.Perp();
    public Vec2 DimA => A + Normal * Offset;
    public Vec2 DimB => B + Normal * Offset;
    public Vec2 TextPosition => (DimA + DimB) / 2 + Normal * (TextHeight * 0.6) - Dir * (Label.Length * TextHeight * 0.3);
    public string Label => Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Line work: extension lines, dimension line and arrowheads.</summary>
    public IEnumerable<(Vec2 A, Vec2 B)> Segments()
    {
        var n = Normal; var d = Dir;
        double gap = TextHeight * 0.3, ext = TextHeight * 0.5, arrow = TextHeight * 0.8;
        yield return (A + n * Math.Sign(Offset) * gap, DimA + n * Math.Sign(Offset) * ext);
        yield return (B + n * Math.Sign(Offset) * gap, DimB + n * Math.Sign(Offset) * ext);
        yield return (DimA, DimB);
        foreach (var (p, s) in new[] { (DimA, 1.0), (DimB, -1.0) })
        {
            yield return (p, p + d * s * arrow + n * arrow * 0.3);
            yield return (p, p + d * s * arrow - n * arrow * 0.3);
        }
    }

    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05) => [DimA, DimB];
    public override DrawingEntity Transformed(Func<Vec2, Vec2> map)
    {
        var a = map(A); var b = map(B);
        var dimA = map(DimA);
        // keep the offset's signed distance relative to the new A->B direction
        var nd = (b - a).Normalized().Perp();
        return new DimensionEntity(a, b, Vec2.Dot(dimA - a, nd), TextHeight) { Layer = Layer, Color = Color };
    }
}

/// <summary>Snap kinds shown as markers in the viewport.</summary>
public enum SnapKind { None, Grid, Endpoint, Midpoint, Center, Quadrant, Intersection, Perpendicular, Nearest }

public readonly record struct SnapResult(Vec2 Point, SnapKind Kind);

/// <summary>2D geometry services for drafting: hit testing, snapping, transforms and offsets.</summary>
public static class Geometry2D
{
    public static IReadOnlyList<Vec2> Polyline(DrawingEntity e, double tol = 0.05) => e switch
    {
        DimensionEntity d => [d.DimA, d.DimB],
        _ => e.Tessellate(tol)
    };

    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var d = b - a;
        double t = d.LengthSquared < 1e-24 ? 0 : Math.Clamp(Vec2.Dot(p - a, d) / d.LengthSquared, 0, 1);
        return Vec2.Distance(p, a + d * t);
    }

    public static double Distance(DrawingEntity e, Vec2 p)
    {
        if (e is BlockRefEntity br)
        {
            double bd = Vec2.Distance(br.Position, p);
            foreach (var c in br.Children()) bd = Math.Min(bd, Distance(c.Geometry, p));
            return bd;
        }
        if (e is HatchEntity h)
        {
            if (h.Contains(p)) return 0;
            double hd = double.MaxValue;
            foreach (var loop in h.Loops)
                for (int i = 0; i < loop.Count; i++) hd = Math.Min(hd, DistanceToSegment(p, loop[i], loop[(i + 1) % loop.Count]));
            return hd;
        }
        if (e is TextEntity t)
        {
            var (mn, mx) = t.Extents();
            return p.X >= mn.X && p.X <= mx.X && p.Y >= mn.Y && p.Y <= mx.Y ? 0 : double.MaxValue;
        }
        if (e is PointEntity pe) return Vec2.Distance(pe.Position, p);
        var pts = Polyline(e, 0.1);
        double best = double.MaxValue;
        for (int i = 0; i + 1 < pts.Count; i++) best = Math.Min(best, DistanceToSegment(p, pts[i], pts[i + 1]));
        if (e is DimensionEntity dim) best = Math.Min(best, Vec2.Distance(p, dim.TextPosition));
        return best;
    }

    public static double Distance(IReadOnlyList<(Vec2 A, Vec2 B)> segs, Vec2 p)
    {
        double best = double.MaxValue;
        foreach (var (a, b) in segs) best = Math.Min(best, DistanceToSegment(p, a, b));
        return best;
    }

    /// <summary>Characteristic points for object snap.</summary>
    public static IEnumerable<(Vec2 P, SnapKind Kind)> SnapPoints(DrawingEntity e)
    {
        switch (e)
        {
            case LineEntity l:
                yield return (l.A, SnapKind.Endpoint); yield return (l.B, SnapKind.Endpoint);
                yield return ((l.A + l.B) / 2, SnapKind.Midpoint); break;
            case CircleEntity c:
                yield return (c.Center, SnapKind.Center);
                for (int i = 0; i < 4; i++) yield return (c.Center + Vec2.FromAngle(i * Math.PI / 2, c.Radius), SnapKind.Quadrant);
                break;
            case ArcEntity a:
                yield return (a.Center, SnapKind.Center); yield return (a.StartPoint, SnapKind.Endpoint); yield return (a.EndPoint, SnapKind.Endpoint);
                yield return (a.Center + Vec2.FromAngle(a.StartAngle + a.Sweep / 2, a.Radius), SnapKind.Midpoint); break;
            case PolylineEntity p:
                for (int i = 0; i < p.Points.Count; i++)
                {
                    yield return (p.Points[i], SnapKind.Endpoint);
                    if (i + 1 < p.Points.Count || p.Closed)
                        yield return ((p.Points[i] + p.Points[(i + 1) % p.Points.Count]) / 2, SnapKind.Midpoint);
                }
                break;
            case DimensionEntity d:
                yield return (d.A, SnapKind.Endpoint); yield return (d.B, SnapKind.Endpoint); break;
            case BlockRefEntity b:
                yield return (b.Position, SnapKind.Endpoint);
                foreach (var c in b.Children().Take(500))
                    foreach (var sp in SnapPoints(c.Geometry)) yield return sp;
                break;
            case TextEntity t: yield return (t.Position, SnapKind.Endpoint); break;
            case PointEntity pt: yield return (pt.Position, SnapKind.Endpoint); break;
        }
    }

    /// <summary>Line segments of an entity (for intersection snap).</summary>
    public static IEnumerable<(Vec2 A, Vec2 B)> Segments(DrawingEntity e)
    {
        if (e is BlockRefEntity b) { foreach (var c in b.Children()) foreach (var s in Segments(c.Geometry)) yield return s; yield break; }
        if (e is HatchEntity h)
        {
            foreach (var loop in h.Loops) for (int i = 0; i < loop.Count; i++) yield return (loop[i], loop[(i + 1) % loop.Count]);
            yield break;
        }
        var pts = Polyline(e, 0.5);
        for (int i = 0; i + 1 < pts.Count; i++) yield return (pts[i], pts[i + 1]);
    }

    public static bool SegmentIntersection(Vec2 a, Vec2 b, Vec2 c, Vec2 d, out Vec2 p)
    {
        var r = b - a; var s = d - c;
        double den = Vec2.Cross(r, s);
        p = default;
        if (Math.Abs(den) < 1e-12) return false;
        double t = Vec2.Cross(c - a, s) / den, u = Vec2.Cross(c - a, r) / den;
        if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return false;
        p = a + r * t;
        return true;
    }

    /// <summary>Parallel copy at signed distance (positive = left of the drawing direction / outward for CCW shapes).</summary>
    public static DrawingEntity? Offset(DrawingEntity e, double distance, Vec2 towards)
    {
        switch (e)
        {
            case LineEntity l:
                {
                    var n = (l.B - l.A).Normalized().Perp();
                    double side = Math.Sign(Vec2.Dot(towards - l.A, n));
                    if (side == 0) side = 1;
                    return new LineEntity(l.A + n * distance * side, l.B + n * distance * side);
                }
            case CircleEntity c:
                {
                    double r = Vec2.Distance(towards, c.Center) > c.Radius ? c.Radius + distance : c.Radius - distance;
                    return r > 0 ? new CircleEntity(c.Center, r) : null;
                }
            case ArcEntity a:
                {
                    double r = Vec2.Distance(towards, a.Center) > a.Radius ? a.Radius + distance : a.Radius - distance;
                    return r > 0 ? new ArcEntity(a.Center, r, a.StartAngle, a.EndAngle) : null;
                }
            case PolylineEntity p when p.Closed && p.Points.Count >= 3:
                {
                    var poly = new Polygon2(p.Points);
                    bool outward = !poly.Contains(towards);
                    var o = poly.AsCounterClockwise().Offset(outward ? distance : -distance);
                    return new PolylineEntity(o.Points, true);
                }
            case PolylineEntity p when p.Points.Count >= 2:
                {
                    // open polyline: offset each segment and join with miter intersections
                    var n0 = (p.Points[1] - p.Points[0]).Normalized().Perp();
                    double side = Math.Sign(Vec2.Dot(towards - p.Points[0], n0));
                    if (side == 0) side = 1;
                    var lines = new List<(Vec2, Vec2)>();
                    for (int i = 0; i + 1 < p.Points.Count; i++)
                    {
                        var n = (p.Points[i + 1] - p.Points[i]).Normalized().Perp() * distance * side;
                        lines.Add((p.Points[i] + n, p.Points[i + 1] + n));
                    }
                    var pts = new List<Vec2> { lines[0].Item1 };
                    for (int i = 0; i + 1 < lines.Count; i++)
                    {
                        var (a1, b1) = lines[i]; var (a2, b2) = lines[i + 1];
                        var r = b1 - a1; var s = b2 - a2; double den = Vec2.Cross(r, s);
                        pts.Add(Math.Abs(den) < 1e-12 ? b1 : a1 + r * (Vec2.Cross(a2 - a1, s) / den));
                    }
                    pts.Add(lines[^1].Item2);
                    return new PolylineEntity(pts, false);
                }
        }
        return null;
    }

    /// <summary>
    /// Breaks Studio-only objects (dimensions, hatches, block references) into library primitives — lines, arcs,
    /// circles, polylines and text — for exporters, plotting and EXPLODE. Colour is the object's own colour, or null
    /// to inherit from its parent.
    /// </summary>
    public static IEnumerable<(DrawingEntity Geometry, string? Layer, string? Color)> Primitives(DrawingEntity e)
    {
        switch (e)
        {
            case DimensionEntity d:
                foreach (var (a, b) in d.Segments()) yield return (new LineEntity(a, b), null, null);
                yield return (new TextEntity(d.TextPosition, d.Label, d.TextHeight), null, null);
                break;
            case HatchEntity h:
                if (h.IsSolid) foreach (var loop in h.Loops) yield return (new PolylineEntity(loop, true), null, null);
                else foreach (var (a, b) in h.Lines) yield return (new LineEntity(a, b), null, null);
                break;
            case BlockRefEntity br:
                foreach (var c in br.Children())
                    foreach (var (g, layer, color) in Primitives(c.Geometry))
                        yield return (g, layer ?? (c.Layer == "0" ? null : c.Layer), color ?? c.Color);
                break;
            default:
                yield return (e, null, null);
                break;
        }
    }

    /// <summary>Closed loops of selected entities (for extrude/revolve and area).</summary>
    public static Polygon2? AsLoop(DrawingEntity e, double tol = 0.05) => e switch
    {
        CircleEntity c => Polygon2.Circle(c.Radius, 0, c.Center, tol),
        PolylineEntity p when p.Closed && p.Points.Count >= 3 => new Polygon2(p.Points),
        PolylineEntity p when p.Points.Count >= 4 && p.Points[0].ApproxEquals(p.Points[^1], 1e-6) => new Polygon2(p.Points),
        _ => null
    };

}

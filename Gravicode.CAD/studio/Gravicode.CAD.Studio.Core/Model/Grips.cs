using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Studio;

/// <summary>
/// Grip editing (AutoCAD-style blue squares on selected objects). <see cref="Of"/> lists the grip points of an
/// object; <see cref="Move"/> returns the object with one grip moved.
/// </summary>
public static class Grips
{
    public static IReadOnlyList<Vec2> Of(DrawingEntity e) => e switch
    {
        LineEntity l => [l.A, l.B, (l.A + l.B) / 2],
        CircleEntity c => [c.Center, .. Enumerable.Range(0, 4).Select(i => c.Center + Vec2.FromAngle(i * Math.PI / 2, c.Radius))],
        ArcEntity a => [a.StartPoint, a.EndPoint, ArcMid(a), a.Center],
        PolylineEntity p => [.. GripVertices(p).Select(i => p.Points[i])],
        TextEntity t => [t.Position],
        PointEntity pt => [pt.Position],
        DimensionEntity d => [d.A, d.B, (d.DimA + d.DimB) / 2],
        BlockRefEntity b => [b.Position],
        HatchEntity h => h.Loops.Count > 0 ? [Centroid(h.Loops[0])] : [],
        _ => []
    };

    /// <summary>
    /// Vertices that get a grip: real corners (turning ≥ 25°) and open ends. Tessellated arcs and splines are made
    /// of many shallow vertices; gripping each one would bury the shape, so smooth runs get a few evenly spaced grips.
    /// </summary>
    public static IReadOnlyList<int> GripVertices(PolylineEntity p)
    {
        var pts = p.Points;
        int n = pts.Count;
        if (n <= 4) return [.. Enumerable.Range(0, n)];
        var keep = new List<int>();
        for (int i = 0; i < n; i++)
        {
            bool end = !p.Closed && (i == 0 || i == n - 1);
            if (end) { keep.Add(i); continue; }
            var a = pts[i] - pts[(i - 1 + n) % n]; var b = pts[(i + 1) % n] - pts[i];
            if (a.Length < 1e-12 || b.Length < 1e-12) continue;
            double turn = Math.Acos(Math.Clamp(Vec2.Dot(a.Normalized(), b.Normalized()), -1, 1));
            if (turn >= Units.Deg(25)) keep.Add(i);
        }
        if (keep.Count < 3)
        {
            int step = Math.Max(1, n / 8);
            for (int i = 0; i < n; i += step) if (!keep.Contains(i)) keep.Add(i);
            keep.Sort();
        }
        return keep;
    }

    private static Vec2 ArcMid(ArcEntity a) => a.Center + Vec2.FromAngle(a.StartAngle + a.Sweep / 2, a.Radius);
    private static Vec2 Centroid(IReadOnlyList<Vec2> pts) { var s = Vec2.Zero; foreach (var p in pts) s += p; return pts.Count == 0 ? s : s / pts.Count; }

    /// <summary>Moves grip <paramref name="index"/> to <paramref name="to"/>; null when the result would be degenerate.</summary>
    public static DrawingEntity? Move(DrawingEntity e, int index, Vec2 to)
    {
        var grips = Of(e);
        if (index < 0 || index >= grips.Count) return null;
        var delta = to - grips[index];
        switch (e)
        {
            case LineEntity l:
                return index switch { 0 => new LineEntity(to, l.B), 1 => new LineEntity(l.A, to), _ => new LineEntity(l.A + delta, l.B + delta) };
            case CircleEntity c:
                if (index == 0) return new CircleEntity(to, c.Radius);
                var r = Vec2.Distance(c.Center, to);
                return r > 1e-9 ? new CircleEntity(c.Center, r) : null;
            case ArcEntity a:
                {
                    if (index == 3) return new ArcEntity(a.Center + delta, a.Radius, a.StartAngle, a.EndAngle);
                    Vec2 s = a.StartPoint, m = ArcMid(a), en = a.EndPoint;
                    if (index == 0) s = to; else if (index == 1) en = to; else m = to;
                    try { var arc = Arc2.ThroughPoints(s, m, en); return new ArcEntity(arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle); }
                    catch { return null; }
                }
            case PolylineEntity p:
                {
                    var pts = p.Points.ToList();
                    pts[GripVertices(p)[index]] = to;
                    return new PolylineEntity(pts, p.Closed);
                }
            case DimensionEntity d:
                {
                    if (index == 0) return d.A.ApproxEquals(to, 1e-9) || d.B.ApproxEquals(to, 1e-9) ? null : new DimensionEntity(to, d.B, d.Offset, d.TextHeight);
                    if (index == 1) return d.A.ApproxEquals(to, 1e-9) ? null : new DimensionEntity(d.A, to, d.Offset, d.TextHeight);
                    var n = (d.B - d.A).Normalized().Perp();
                    return new DimensionEntity(d.A, d.B, Vec2.Dot(to - d.A, n), d.TextHeight);
                }
            default:
                return e.Transformed(q => q + delta);
        }
    }
}

namespace Gravicode.CAD.Studio;

/// <summary>
/// Curve editing for drafting: intersections, TRIM, EXTEND, FILLET and CHAMFER on lines, arcs, circles and
/// polylines. Pure functions — callers replace entities inside an undoable edit.
/// </summary>
public static class CurveOps
{
    private const double Eps = 1e-9;
    private const double TwoPi = 2 * Math.PI;

    // ------------------------------------------------------------------ primitives

    /// <summary>Segment or counter-clockwise circular arc (a full circle has sweep 2π).</summary>
    internal abstract record Prim;
    internal sealed record Seg(Vec2 A, Vec2 B) : Prim;
    internal sealed record Circ(Vec2 C, double R, double Start, double Sweep) : Prim;

    internal static IEnumerable<Prim> Prims(DrawingEntity e)
    {
        switch (e)
        {
            case LineEntity l: yield return new Seg(l.A, l.B); break;
            case CircleEntity c: yield return new Circ(c.Center, c.Radius, 0, TwoPi); break;
            case ArcEntity a: yield return new Circ(a.Center, a.Radius, a.StartAngle, a.Sweep); break;
            case PolylineEntity p:
                {
                    var pts = p.Tessellate();
                    for (int i = 0; i + 1 < pts.Count; i++) yield return new Seg(pts[i], pts[i + 1]);
                    break;
                }
            case BlockRefEntity b:
                foreach (var child in b.Children()) foreach (var pr in Prims(child.Geometry)) yield return pr;
                break;
            // text, points, dimensions and hatches are not cutting edges
        }
    }

    internal static double Norm(double a) { a %= TwoPi; return a < 0 ? a + TwoPi : a; }
    private static bool AngleIn(double theta, double start, double sweep) => sweep >= TwoPi - Eps || Norm(theta - start) <= sweep + 1e-9;

    /// <summary>Intersections of a target primitive with a bounded primitive. When <paramref name="unbounded"/> the
    /// target is treated as an infinite line / full circle (used by EXTEND).</summary>
    private static IEnumerable<Vec2> Intersect(Prim target, Prim other, bool unbounded)
    {
        switch (target, other)
        {
            case (Seg s, Seg o):
                {
                    var r = s.B - s.A; var q = o.B - o.A;
                    double den = Vec2.Cross(r, q);
                    if (Math.Abs(den) < 1e-14) yield break;
                    double t = Vec2.Cross(o.A - s.A, q) / den, u = Vec2.Cross(o.A - s.A, r) / den;
                    if (u < -1e-9 || u > 1 + 1e-9) yield break;
                    if (!unbounded && (t < -1e-9 || t > 1 + 1e-9)) yield break;
                    yield return s.A + r * t;
                    break;
                }
            case (Seg s, Circ c):
                foreach (var (p, t) in LineCircle(s.A, s.B, c.C, c.R))
                    if ((unbounded || (t >= -1e-9 && t <= 1 + 1e-9)) && AngleIn((p - c.C).Angle, c.Start, c.Sweep)) yield return p;
                break;
            case (Circ c, Seg s):
                foreach (var (p, t) in LineCircle(s.A, s.B, c.C, c.R))
                    if (t >= -1e-9 && t <= 1 + 1e-9 && (unbounded || AngleIn((p - c.C).Angle, c.Start, c.Sweep))) yield return p;
                break;
            case (Circ c, Circ o):
                foreach (var p in CircleCircle(c.C, c.R, o.C, o.R))
                    if (AngleIn((p - o.C).Angle, o.Start, o.Sweep) && (unbounded || AngleIn((p - c.C).Angle, c.Start, c.Sweep))) yield return p;
                break;
        }
    }

    private static IEnumerable<(Vec2 P, double T)> LineCircle(Vec2 a, Vec2 b, Vec2 c, double r)
    {
        var d = b - a; var f = a - c;
        double A = Vec2.Dot(d, d), B = 2 * Vec2.Dot(f, d), C = Vec2.Dot(f, f) - r * r;
        if (A < 1e-24) yield break;
        double disc = B * B - 4 * A * C;
        if (disc < -1e-12 * Math.Max(1, B * B)) yield break;
        disc = Math.Sqrt(Math.Max(0, disc));
        double t1 = (-B - disc) / (2 * A), t2 = (-B + disc) / (2 * A);
        yield return (a + d * t1, t1);
        if (disc > 1e-12) yield return (a + d * t2, t2);
    }

    private static IEnumerable<Vec2> CircleCircle(Vec2 c0, double r0, Vec2 c1, double r1)
    {
        double d = Vec2.Distance(c0, c1);
        if (d < 1e-12 || d > r0 + r1 + 1e-9 || d < Math.Abs(r0 - r1) - 1e-9) yield break;
        double a = (r0 * r0 - r1 * r1 + d * d) / (2 * d);
        double h = Math.Sqrt(Math.Max(0, r0 * r0 - a * a));
        var dir = (c1 - c0) / d;
        var m = c0 + dir * a;
        yield return m + dir.Perp() * h;
        if (h > 1e-12) yield return m - dir.Perp() * h;
    }

    /// <summary>All intersection points of <paramref name="target"/> with the given cutting objects.</summary>
    public static List<Vec2> Intersections(DrawingEntity target, IEnumerable<DrawingEntity> cutters, bool unbounded = false)
    {
        var result = new List<Vec2>();
        var tp = Prims(target).ToList();
        foreach (var cut in cutters)
            foreach (var o in Prims(cut))
                foreach (var t in tp)
                    result.AddRange(Intersect(t, o, unbounded));
        return result;
    }

    // ------------------------------------------------------------------ parameters along a target

    /// <summary>Line: t∈[0,1]. Arc: angle offset from the start. Circle: absolute angle. Polyline: arc length.</summary>
    private static double Param(DrawingEntity e, Vec2 p) => e switch
    {
        LineEntity l => (l.B - l.A).LengthSquared < 1e-24 ? 0 : Vec2.Dot(p - l.A, l.B - l.A) / (l.B - l.A).LengthSquared,
        ArcEntity a => Norm((p - a.Center).Angle - a.StartAngle),
        CircleEntity c => Norm((p - c.Center).Angle),
        PolylineEntity pl => PolyParam(pl.Tessellate(), p),
        _ => 0
    };

    private static double PolyParam(IReadOnlyList<Vec2> pts, Vec2 p)
    {
        double best = double.MaxValue, at = 0, run = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var d = pts[i + 1] - pts[i]; double len = d.Length;
            double t = len < 1e-12 ? 0 : Math.Clamp(Vec2.Dot(p - pts[i], d) / (len * len), 0, 1);
            double dist = Vec2.Distance(p, pts[i] + d * t);
            if (dist < best) { best = dist; at = run + t * len; }
            run += len;
        }
        return at;
    }

    private static double PolyLength(IReadOnlyList<Vec2> pts) { double l = 0; for (int i = 0; i + 1 < pts.Count; i++) l += Vec2.Distance(pts[i], pts[i + 1]); return l; }

    /// <summary>Portion of a polyline between two arc-length positions (s0 &lt; s1).</summary>
    private static List<Vec2> SubPolyline(IReadOnlyList<Vec2> pts, double s0, double s1)
    {
        var r = new List<Vec2>();
        double run = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i]; var b = pts[i + 1]; double len = Vec2.Distance(a, b);
            double e0 = run, e1 = run + len;
            if (e1 >= s0 - 1e-12 && e0 <= s1 + 1e-12 && len > 1e-12)
            {
                var p0 = a + (b - a) * Math.Clamp((s0 - e0) / len, 0, 1);
                var p1 = a + (b - a) * Math.Clamp((s1 - e0) / len, 0, 1);
                if (r.Count == 0 || !r[^1].ApproxEquals(p0, 1e-9)) r.Add(p0);
                if (!r[^1].ApproxEquals(p1, 1e-9)) r.Add(p1);
            }
            run = e1;
        }
        return r;
    }

    // ------------------------------------------------------------------ TRIM

    /// <summary>
    /// Removes the piece of <paramref name="target"/> around <paramref name="pick"/> bounded by the nearest
    /// intersections with <paramref name="cutters"/>. Returns the replacement pieces (empty = delete the object,
    /// which is what happens when nothing crosses it), or null when the object type cannot be trimmed.
    /// </summary>
    public static List<DrawingEntity>? Trim(DrawingEntity target, IEnumerable<DrawingEntity> cutters, Vec2 pick) =>
        TrimDetailed(target, cutters, pick)?.Kept;

    /// <summary>Pieces kept by a trim and the piece removed (for previews).</summary>
    public sealed record TrimResult(List<DrawingEntity> Kept, DrawingEntity? Removed);

    public static TrimResult? TrimDetailed(DrawingEntity target, IEnumerable<DrawingEntity> cutters, Vec2 pick)
    {
        var hits = Intersections(target, cutters);
        switch (target)
        {
            case LineEntity l:
                {
                    var ts = hits.Select(p => Param(l, p)).Where(t => t > 1e-7 && t < 1 - 1e-7).ToList();
                    // (pieces are measured in the line's 0..1 parameter)
                    double tp = Param(l, pick);
                    double lo = ts.Where(t => t < tp).DefaultIfEmpty(0).Max(), hi = ts.Where(t => t > tp).DefaultIfEmpty(1).Min();
                    var r = new List<DrawingEntity>();
                    Vec2 At(double t) => l.A + (l.B - l.A) * t;
                    if (lo > 0) r.Add(new LineEntity(l.A, At(lo)));
                    if (hi < 1) r.Add(new LineEntity(At(hi), l.B));
                    return new(r, new LineEntity(At(lo), At(hi)));
                }
            case ArcEntity a:
                {
                    double sw = a.Sweep;
                    var ts = hits.Select(p => Param(a, p)).Where(t => t > 1e-7 && t < sw - 1e-7).ToList();
                    double tp = Math.Min(Param(a, pick), sw);
                    double lo = ts.Where(t => t < tp).DefaultIfEmpty(0).Max(), hi = ts.Where(t => t > tp).DefaultIfEmpty(sw).Min();
                    var r = new List<DrawingEntity>();
                    if (lo > 0) r.Add(new ArcEntity(a.Center, a.Radius, a.StartAngle, a.StartAngle + lo));
                    if (hi < sw) r.Add(new ArcEntity(a.Center, a.Radius, a.StartAngle + hi, a.EndAngle));
                    return new(r, new ArcEntity(a.Center, a.Radius, a.StartAngle + lo, a.StartAngle + hi));
                }
            case CircleEntity c:
                {
                    var angs = Distinct(hits.Select(p => Param(c, p)), TwoPi);
                    if (angs.Count == 0) return new([], c);
                    if (angs.Count == 1) return null;            // one touching point cannot split a circle
                    double tp = Param(c, pick);
                    double lo = angs.Where(t => t < tp).DefaultIfEmpty(angs.Max() - TwoPi).Max();
                    double hi = angs.Where(t => t > tp).DefaultIfEmpty(angs.Min() + TwoPi).Min();
                    return new([new ArcEntity(c.Center, c.Radius, hi, lo)], new ArcEntity(c.Center, c.Radius, lo, hi));
                }
            case PolylineEntity pl:
                {
                    var pts = pl.Tessellate();
                    double L = PolyLength(pts);
                    var ss = Distinct(hits.Select(p => PolyParam(pts, p)).Where(s => pl.Closed || (s > 1e-7 && s < L - 1e-7)), pl.Closed ? L : double.MaxValue);
                    double sp = PolyParam(pts, pick);
                    if (!pl.Closed)
                    {
                        double lo = ss.Where(s => s < sp).DefaultIfEmpty(0).Max(), hi = ss.Where(s => s > sp).DefaultIfEmpty(L).Min();
                        var r = new List<DrawingEntity>();
                        if (lo > 0) r.Add(new PolylineEntity(SubPolyline(pts, 0, lo), false));
                        if (hi < L) r.Add(new PolylineEntity(SubPolyline(pts, hi, L), false));
                        return new([.. r.Where(e => ((PolylineEntity)e).Points.Count >= 2)], new PolylineEntity(SubPolyline(pts, lo, hi), false));
                    }
                    if (ss.Count == 0) return new([], pl);
                    if (ss.Count == 1) return null;
                    double clo = ss.Where(s => s < sp).DefaultIfEmpty(ss.Max() - L).Max();
                    double chi = ss.Where(s => s > sp).DefaultIfEmpty(ss.Min() + L).Min();
                    // keep chi → clo going forward, wrapping through the start
                    var keep = chi >= L ? SubPolyline(pts, chi - L, clo < 0 ? clo + L : clo)
                        : clo < 0 ? SubPolyline(pts, chi, clo + L)
                        : [.. SubPolyline(pts, chi, L), .. SubPolyline(pts, 0, clo).Skip(1)];
                    List<Vec2> removed = clo < 0 ? [.. SubPolyline(pts, clo + L, L), .. SubPolyline(pts, 0, chi).Skip(1)]
                        : chi >= L ? [.. SubPolyline(pts, clo, L), .. SubPolyline(pts, 0, chi - L).Skip(1)]
                        : SubPolyline(pts, clo, chi);
                    return new(keep.Count >= 2 ? [new PolylineEntity(keep, false)] : [], new PolylineEntity(removed, false));
                }
        }
        return null;
    }

    private static List<double> Distinct(IEnumerable<double> values, double period)
    {
        var r = new List<double>();
        foreach (var v in values.OrderBy(x => x))
            if (r.Count == 0 || v - r[^1] > 1e-7) r.Add(v);
        if (period < double.MaxValue && r.Count > 1 && r[0] + period - r[^1] < 1e-7) r.RemoveAt(r.Count - 1);
        return r;
    }

    // ------------------------------------------------------------------ EXTEND

    /// <summary>
    /// Extends the end of <paramref name="target"/> nearest to <paramref name="pick"/> to the next boundary.
    /// Returns null when nothing lies in the way.
    /// </summary>
    public static DrawingEntity? Extend(DrawingEntity target, IEnumerable<DrawingEntity> boundaries, Vec2 pick)
    {
        var bounds = boundaries.ToList();
        switch (target)
        {
            case LineEntity l:
                {
                    var ts = Intersections(l, bounds, unbounded: true).Select(p => Param(l, p)).ToList();
                    bool atEnd = Param(l, pick) >= 0.5;
                    var cand = atEnd ? ts.Where(t => t > 1 + 1e-7).ToList() : ts.Where(t => t < -1e-7).ToList();
                    if (cand.Count == 0) return null;
                    double t = atEnd ? cand.Min() : cand.Max();
                    var p = l.A + (l.B - l.A) * t;
                    return atEnd ? new LineEntity(l.A, p) : new LineEntity(p, l.B);
                }
            case ArcEntity a:
                {
                    double sw = a.Sweep;
                    var angs = Intersections(a, bounds, unbounded: true).Select(p => (p - a.Center).Angle).ToList();
                    bool atEnd = Param(a, pick) >= sw / 2 && Param(a, pick) <= sw + (TwoPi - sw) / 2;
                    if (atEnd)
                    {
                        var d = angs.Select(t => Norm(t - a.StartAngle)).Where(d => d > sw + 1e-7 && d < TwoPi - 1e-7).ToList();
                        return d.Count == 0 ? null : new ArcEntity(a.Center, a.Radius, a.StartAngle, a.StartAngle + d.Min());
                    }
                    var back = angs.Select(t => Norm(a.StartAngle - t)).Where(d => d > 1e-7 && d < TwoPi - sw - 1e-7).ToList();
                    return back.Count == 0 ? null : new ArcEntity(a.Center, a.Radius, a.StartAngle - back.Min(), a.EndAngle);
                }
            case PolylineEntity pl when !pl.Closed && pl.Points.Count >= 2:
                {
                    var pts = pl.Points;
                    bool atEnd = PolyParam(pts, pick) >= PolyLength(pts) / 2;
                    var seg = atEnd ? new LineEntity(pts[^2], pts[^1]) : new LineEntity(pts[0], pts[1]);
                    var ext = Extend(seg, bounds.Where(b => !ReferenceEquals(b, target)), atEnd ? seg.B : seg.A) as LineEntity;
                    if (ext is null) return null;
                    var np = pts.ToList();
                    if (atEnd) np[^1] = ext.B; else np[0] = ext.A;
                    return new PolylineEntity(np, false, pl.Bulges);
                }
        }
        return null;
    }

    // ------------------------------------------------------------------ FILLET / CHAMFER

    /// <summary>Result of a corner operation on two lines: the trimmed lines plus the new arc / chamfer line.</summary>
    public sealed record CornerResult(LineEntity First, LineEntity Second, DrawingEntity? Joint);

    /// <summary>Corner geometry shared by fillet and chamfer: intersection, kept directions and far endpoints.</summary>
    private static string? Corner(LineEntity l1, Vec2 pick1, LineEntity l2, Vec2 pick2,
        out Vec2 x, out Vec2 u1, out Vec2 u2, out Vec2 far1, out Vec2 far2)
    {
        x = u1 = u2 = far1 = far2 = default;
        var d1 = l1.B - l1.A; var d2 = l2.B - l2.A;
        double den = Vec2.Cross(d1, d2);
        if (Math.Abs(den) < 1e-12 * d1.Length * d2.Length) return "Lines are parallel.";
        x = l1.A + d1 * (Vec2.Cross(l2.A - l1.A, d2) / den);
        (u1, far1) = KeptSide(l1, pick1, x);
        (u2, far2) = KeptSide(l2, pick2, x);
        return u1 == Vec2.Zero || u2 == Vec2.Zero ? "Lines meet at their picked end — pick away from the corner." : null;
    }

    /// <summary>Direction from the corner towards the part of the line the user picked, and that part's far end.</summary>
    private static (Vec2 Dir, Vec2 Far) KeptSide(LineEntity l, Vec2 pick, Vec2 x)
    {
        var d = (l.B - l.A).Normalized();
        var proj = l.A + d * Vec2.Dot(pick - l.A, d);
        var side = proj - x;
        if (side.Length < 1e-9) side = (Vec2.Distance(l.A, x) > Vec2.Distance(l.B, x) ? l.A : l.B) - x;
        var u = Vec2.Dot(side, d) >= 0 ? d : -1 * d;
        double ea = Vec2.Dot(l.A - x, u), eb = Vec2.Dot(l.B - x, u);
        var far = ea >= eb ? l.A : l.B;
        return Math.Max(ea, eb) <= 1e-9 ? (Vec2.Zero, far) : (u, far);
    }

    public static CornerResult Fillet(LineEntity l1, Vec2 pick1, LineEntity l2, Vec2 pick2, double radius)
    {
        if (Corner(l1, pick1, l2, pick2, out var x, out var u1, out var u2, out var f1, out var f2) is { } err) throw new InvalidOperationException(err);
        if (radius <= 1e-12) return new(new LineEntity(f1, x), new LineEntity(f2, x), null);
        double theta = Math.Acos(Math.Clamp(Vec2.Dot(u1, u2), -1, 1));
        if (theta < 1e-6 || Math.PI - theta < 1e-6) throw new InvalidOperationException("Lines are collinear.");
        double d = radius / Math.Tan(theta / 2);
        if (d > Vec2.Dot(f1 - x, u1) + 1e-9 || d > Vec2.Dot(f2 - x, u2) + 1e-9) throw new InvalidOperationException("Radius is too large for these lines.");
        var t1 = x + u1 * d; var t2 = x + u2 * d;
        var center = x + (u1 + u2).Normalized() * (radius / Math.Sin(theta / 2));
        double a1 = (t1 - center).Angle, a2 = (t2 - center).Angle;
        var arc = Norm(a2 - a1) <= Math.PI ? new ArcEntity(center, radius, a1, a2) : new ArcEntity(center, radius, a2, a1);
        return new(new LineEntity(f1, t1), new LineEntity(f2, t2), arc);
    }

    public static CornerResult Chamfer(LineEntity l1, Vec2 pick1, LineEntity l2, Vec2 pick2, double dist1, double dist2)
    {
        if (Corner(l1, pick1, l2, pick2, out var x, out var u1, out var u2, out var f1, out var f2) is { } err) throw new InvalidOperationException(err);
        if (dist1 <= 1e-12 && dist2 <= 1e-12) return new(new LineEntity(f1, x), new LineEntity(f2, x), null);
        if (dist1 > Vec2.Dot(f1 - x, u1) + 1e-9 || dist2 > Vec2.Dot(f2 - x, u2) + 1e-9) throw new InvalidOperationException("Chamfer distance is longer than the line.");
        var t1 = x + u1 * dist1; var t2 = x + u2 * dist2;
        return new(new LineEntity(f1, t1), new LineEntity(f2, t2), new LineEntity(t1, t2));
    }

    /// <summary>Fillets (radius &gt; 0) or chamfers every corner of a polyline. Fillets become real arc segments
    /// (bulges); straight corners only — existing arc segments are kept as they are.</summary>
    public static PolylineEntity RoundCorners(PolylineEntity pl, double size, bool chamfer, double chordTolerance = 0.05)
    {
        var p = pl.Points;
        int n = p.Count;
        if (n < 3 || size <= 0) return new PolylineEntity(p, pl.Closed, pl.Bulges);
        var pts = new List<Vec2>();
        var bulges = new List<double>();
        for (int i = 0; i < n; i++)
        {
            bool endpoint = !pl.Closed && (i == 0 || i == n - 1);
            int prevSeg = (i - 1 + n) % n;
            bool arcAround = Math.Abs(pl.Bulges[prevSeg]) > 1e-12 || Math.Abs(pl.Bulges[i]) > 1e-12;
            var prev = p[(i - 1 + n) % n]; var cur = p[i]; var next = p[(i + 1) % n];
            var a = prev - cur; var b = next - cur;
            double la = a.Length, lb = b.Length;
            double theta = la < 1e-12 || lb < 1e-12 ? 0 : Math.Acos(Math.Clamp(Vec2.Dot(a / la, b / lb), -1, 1));
            if (endpoint || arcAround || theta < 1e-6 || Math.PI - theta < 1e-6)
            {
                pts.Add(cur); bulges.Add(pl.Bulges[i]);
                continue;
            }
            var ua = a / la; var ub = b / lb;
            // never consume more than half of either adjacent edge
            double d = chamfer ? size : size / Math.Tan(theta / 2);
            d = Math.Min(d, Math.Min(la, lb) / 2);
            var t1 = cur + ua * d; var t2 = cur + ub * d;
            double turn = Math.PI - theta;                                 // direction change at the corner
            double sign = Vec2.Cross(cur - prev, next - cur) >= 0 ? 1 : -1;  // left turn = counter-clockwise arc
            pts.Add(t1); bulges.Add(chamfer ? 0 : PolylineEntity.BulgeFromSweep(sign * turn));
            pts.Add(t2); bulges.Add(pl.Bulges[i]);
        }
        return new PolylineEntity(pts, pl.Closed, bulges);
    }
}

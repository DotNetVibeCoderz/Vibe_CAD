namespace Gravicode.CAD.Geometry;

/// <summary>Simple closed 2D polygon (no repeated closing vertex).</summary>
public sealed class Polygon2
{
    public List<Vec2> Points { get; }

    public Polygon2(IEnumerable<Vec2> points)
    {
        Points = [.. points];
        if (Points.Count > 1 && Points[0].ApproxEquals(Points[^1], 1e-12)) Points.RemoveAt(Points.Count - 1);
    }

    public int Count => Points.Count;
    public Vec2 this[int i] => Points[((i % Count) + Count) % Count];

    /// <summary>Signed area (positive = counter-clockwise).</summary>
    public double SignedArea
    {
        get
        {
            double a = 0;
            for (int i = 0, j = Count - 1; i < Count; j = i++) a += Vec2.Cross(Points[j], Points[i]);
            return a * 0.5;
        }
    }
    public double Area => Math.Abs(SignedArea);
    public bool IsCounterClockwise => SignedArea > 0;
    public double Perimeter { get { double l = 0; for (int i = 0; i < Count; i++) l += Vec2.Distance(this[i], this[i + 1]); return l; } }

    public Vec2 Centroid
    {
        get
        {
            double a = 0, cx = 0, cy = 0;
            for (int i = 0, j = Count - 1; i < Count; j = i++)
            {
                double f = Vec2.Cross(Points[j], Points[i]);
                a += f; cx += (Points[j].X + Points[i].X) * f; cy += (Points[j].Y + Points[i].Y) * f;
            }
            return Math.Abs(a) < 1e-300 ? Points.Aggregate(Vec2.Zero, (s, p) => s + p) / Count : new Vec2(cx, cy) / (3 * a);
        }
    }

    public Polygon2 Reversed() { var p = new List<Vec2>(Points); p.Reverse(); return new Polygon2(p); }
    public Polygon2 AsCounterClockwise() => IsCounterClockwise ? this : Reversed();
    public Polygon2 AsClockwise() => IsCounterClockwise ? Reversed() : this;
    public Polygon2 Transformed(Func<Vec2, Vec2> f) => new(Points.Select(f));
    public Polygon2 Translated(double dx, double dy) => Transformed(p => p + new Vec2(dx, dy));

    /// <summary>Even-odd point-in-polygon test.</summary>
    public bool Contains(Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = Count - 1; i < Count; j = i++)
        {
            var a = Points[i]; var b = Points[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Offsets the polygon by <paramref name="distance"/> (positive grows a CCW polygon) using miter joins
    /// limited by <paramref name="miterLimit"/>; corners beyond the limit are beveled.
    /// Suited to tool-radius compensation on simple profiles.
    /// </summary>
    public Polygon2 Offset(double distance, double miterLimit = 4)
    {
        var poly = AsCounterClockwise();
        int n = poly.Count;
        var r = new List<Vec2>(n * 2);
        for (int i = 0; i < n; i++)
        {
            var prev = poly[i - 1]; var cur = poly[i]; var next = poly[i + 1];
            var d0 = (cur - prev).Normalized(); var d1 = (next - cur).Normalized();
            var n0 = new Vec2(d0.Y, -d0.X); var n1 = new Vec2(d1.Y, -d1.X); // outward for CCW
            var bis = (n0 + n1);
            double bl = bis.Length;
            if (bl < 1e-12) { r.Add(cur + n0 * distance); continue; }
            bis /= bl;
            double cosHalf = Vec2.Dot(bis, n0);
            double miter = distance / Math.Max(cosHalf, 1e-9);
            if (Math.Abs(miter / distance) > miterLimit)
            {
                r.Add(cur + n0 * distance);
                r.Add(cur + n1 * distance);
            }
            else r.Add(cur + bis * miter);
        }
        var res = new Polygon2(r);
        return IsCounterClockwise ? res : res.Reversed();
    }

    /// <summary>Removes collinear and duplicate vertices.</summary>
    public Polygon2 Simplified(double eps = 1e-9)
    {
        var pts = new List<Vec2>();
        for (int i = 0; i < Count; i++)
        {
            var a = this[i - 1]; var b = this[i]; var c = this[i + 1];
            if (b.ApproxEquals(a, eps)) continue;
            if (Math.Abs(Vec2.Cross(b - a, c - b)) <= eps * Math.Max(1, (c - a).Length)) continue;
            pts.Add(b);
        }
        return new Polygon2(pts);
    }

    // ---------- factories ----------
    public static Polygon2 Rectangle(double width, double height, bool centered = true)
    {
        double x0 = centered ? -width / 2 : 0, y0 = centered ? -height / 2 : 0;
        return new([new(x0, y0), new(x0 + width, y0), new(x0 + width, y0 + height), new(x0, y0 + height)]);
    }

    public static Polygon2 RoundedRectangle(double width, double height, double radius, double tolerance = 0.05)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        if (radius <= 0) return Rectangle(width, height);
        double hx = width / 2 - radius, hy = height / 2 - radius;
        var pts = new List<Vec2>();
        Vec2[] centers = [new(hx, -hy), new(hx, hy), new(-hx, hy), new(-hx, -hy)];
        int seg = Math.Max(2, CurveMath.ArcSegments(radius, Math.PI / 2, tolerance));
        for (int c = 0; c < 4; c++)
        {
            double a0 = -Math.PI / 2 + c * Math.PI / 2;
            for (int i = 0; i <= seg; i++) pts.Add(centers[c] + Vec2.FromAngle(a0 + Math.PI / 2 * i / seg, radius));
        }
        return new Polygon2(pts).Simplified(1e-12);
    }

    public static Polygon2 Circle(double radius, int segments = 0, Vec2? center = null, double tolerance = 0.05)
    {
        if (segments <= 0) segments = Math.Max(12, CurveMath.ArcSegments(radius, 2 * Math.PI, tolerance));
        var c = center ?? Vec2.Zero;
        return new(Enumerable.Range(0, segments).Select(i => c + Vec2.FromAngle(2 * Math.PI * i / segments, radius)));
    }

    public static Polygon2 RegularPolygon(int sides, double circumradius, double rotation = 0) =>
        new(Enumerable.Range(0, sides).Select(i => Vec2.FromAngle(rotation + 2 * Math.PI * i / sides, circumradius)));

    public static Polygon2 Slot(double length, double width, double tolerance = 0.05)
    {
        double r = width / 2, h = length / 2 - r;
        int seg = Math.Max(4, CurveMath.ArcSegments(r, Math.PI, tolerance));
        var pts = new List<Vec2>();
        for (int i = 0; i <= seg; i++) pts.Add(new Vec2(h, 0) + Vec2.FromAngle(-Math.PI / 2 + Math.PI * i / seg, r));
        for (int i = 0; i <= seg; i++) pts.Add(new Vec2(-h, 0) + Vec2.FromAngle(Math.PI / 2 + Math.PI * i / seg, r));
        return new Polygon2(pts);
    }

    public static Polygon2 Star(int points, double outer, double inner) =>
        new(Enumerable.Range(0, points * 2).Select(i => Vec2.FromAngle(Math.PI / 2 + Math.PI * i / points, i % 2 == 0 ? outer : inner)));
}

/// <summary>A planar region: CCW outer boundary plus CW holes.</summary>
public sealed class Profile2
{
    public Polygon2 Outer { get; }
    public List<Polygon2> Holes { get; } = [];

    public Profile2(Polygon2 outer, IEnumerable<Polygon2>? holes = null)
    {
        Outer = outer.AsCounterClockwise();
        if (holes != null) foreach (var h in holes) Holes.Add(h.AsClockwise());
    }

    public Profile2 WithHole(Polygon2 hole) { Holes.Add(hole.AsClockwise()); return this; }
    public double Area => Outer.Area - Holes.Sum(h => h.Area);

    public static implicit operator Profile2(Polygon2 p) => new(p);

    /// <summary>Builds profiles from arbitrary loops by nesting depth (even depth = outer, odd = hole).</summary>
    public static List<Profile2> FromLoops(IEnumerable<Polygon2> loops)
    {
        var list = loops.Where(l => l.Count >= 3 && l.Area > 1e-12).OrderByDescending(l => l.Area).ToList();
        var depth = new int[list.Count];
        var parent = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            parent[i] = -1;
            var probe = list[i].Points[0];
            for (int j = i - 1; j >= 0; j--)
                if (list[j].Contains(probe)) { parent[i] = j; depth[i] = depth[j] + 1; break; }
        }
        var result = new Dictionary<int, Profile2>();
        for (int i = 0; i < list.Count; i++)
        {
            if (depth[i] % 2 == 0) result[i] = new Profile2(list[i]);
            else if (parent[i] >= 0 && result.TryGetValue(parent[i], out var p)) p.WithHole(list[i]);
        }
        return [.. result.Values];
    }
}

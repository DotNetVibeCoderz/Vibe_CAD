using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.CAM;

public enum ContourSide { Outside, Inside, On }

/// <summary>2.5D milling strategies. Z = 0 is the stock top; depths are positive numbers cut downward.</summary>
public static class Milling
{
    static IEnumerable<double> Levels(double depth, double stepDown)
    {
        int n = Math.Max(1, (int)Math.Ceiling(depth / Math.Max(stepDown, 1e-6) - 1e-9));
        for (int i = 1; i <= n; i++) yield return -depth * i / n;
    }

    /// <summary>Profile contour with tool-radius compensation and multiple step-downs.</summary>
    public static Toolpath Contour(Polygon2 profile, Tool tool, double depth, double stepDown, ContourSide side = ContourSide.Outside, double safeZ = 5)
    {
        var path = new Toolpath($"Contour ({side})", tool) { SafeZ = safeZ };
        var ccw = profile.AsCounterClockwise();
        var loop = side switch
        {
            ContourSide.Outside => ccw.Offset(tool.Radius),
            ContourSide.Inside => ccw.Offset(-tool.Radius),
            _ => ccw
        };
        // climb milling: outside contours CW, inside contours CCW
        var pts = side == ContourSide.Outside ? loop.Reversed().Points : loop.Points;
        foreach (var z in Levels(depth, stepDown))
        {
            path.Rapid(new Vec3(pts[0].X, pts[0].Y, safeZ));
            path.Feed(new Vec3(pts[0].X, pts[0].Y, z), tool.PlungeRate);
            foreach (var p in pts.Skip(1)) path.Feed(new Vec3(p.X, p.Y, z), tool.FeedRate);
            path.Feed(new Vec3(pts[0].X, pts[0].Y, z), tool.FeedRate);
            path.Rapid(new Vec3(pts[0].X, pts[0].Y, safeZ));
        }
        return path;
    }

    /// <summary>
    /// Zig-zag pocket of a region (outer boundary minus islands) followed by a finishing contour pass.
    /// <paramref name="stepover"/> is a fraction of the tool diameter (0.4 = 40 %).
    /// </summary>
    public static Toolpath Pocket(Profile2 region, Tool tool, double depth, double stepDown, double stepover = 0.45, double angleDegrees = 0, double safeZ = 5, bool finishingPass = true)
    {
        var path = new Toolpath("Pocket", tool) { SafeZ = safeZ };
        var boundary = region.Outer.AsCounterClockwise().Offset(-tool.Radius);
        var islands = region.Holes.Select(h => h.AsCounterClockwise().Offset(tool.Radius)).ToList();
        var loops = new List<Polygon2> { boundary };
        loops.AddRange(islands);
        double step = Math.Max(tool.Diameter * Math.Clamp(stepover, 0.05, 0.95), 1e-3);
        double ang = Units.Deg(angleDegrees);
        // rotate into raster frame
        Vec2 R(Vec2 p) => p.Rotate(-ang);
        Vec2 RI(Vec2 p) => p.Rotate(ang);
        var rl = loops.Select(l => l.Points.Select(R).ToList()).ToList();
        double y0 = rl[0].Min(p => p.Y), y1 = rl[0].Max(p => p.Y);

        var rows = new List<List<(double X0, double X1)>>();
        for (double y = y0 + step * 0.5; y < y1; y += step)
        {
            var xs = new List<double>();
            foreach (var l in rl)
                for (int i = 0; i < l.Count; i++)
                {
                    var a = l[i]; var b = l[(i + 1) % l.Count];
                    if ((a.Y > y) != (b.Y > y)) xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
            xs.Sort();
            var spans = new List<(double, double)>();
            for (int i = 0; i + 1 < xs.Count; i += 2) if (xs[i + 1] - xs[i] > 1e-9) spans.Add((xs[i], xs[i + 1]));
            rows.Add(spans);
        }

        foreach (var z in Levels(depth, stepDown))
        {
            bool forward = true;
            Vec2? last = null;
            for (int r = 0; r < rows.Count; r++)
            {
                double y = y0 + step * 0.5 + r * step;
                var spans = forward ? rows[r] : [.. Enumerable.Reverse(rows[r])];
                foreach (var (x0, x1) in spans)
                {
                    var s = RI(new Vec2(forward ? x0 : x1, y)); var e = RI(new Vec2(forward ? x1 : x0, y));
                    bool link = last is { } lp && Vec2.Distance(lp, s) <= step * 1.5 && spans.Count == 1;
                    if (!link)
                    {
                        path.Rapid(new Vec3(path.Moves.Count > 0 ? path.Moves[^1].To.X : s.X, path.Moves.Count > 0 ? path.Moves[^1].To.Y : s.Y, safeZ));
                        path.Rapid(new Vec3(s.X, s.Y, safeZ));
                        path.Feed(new Vec3(s.X, s.Y, z), tool.PlungeRate);
                    }
                    else path.Feed(new Vec3(s.X, s.Y, z), tool.FeedRate);
                    path.Feed(new Vec3(e.X, e.Y, z), tool.FeedRate);
                    last = e;
                }
                forward = !forward;
            }
            // finishing pass around the walls and islands
            foreach (var l in finishingPass ? loops : [])
            {
                var pts = l.Points;
                if (pts.Count < 3) continue;
                if (path.Moves.Count > 0) path.Rapid(new Vec3(path.Moves[^1].To.X, path.Moves[^1].To.Y, safeZ));
                path.Rapid(new Vec3(pts[0].X, pts[0].Y, safeZ));
                path.Feed(new Vec3(pts[0].X, pts[0].Y, z), tool.PlungeRate);
                foreach (var p in pts.Skip(1)) path.Feed(new Vec3(p.X, p.Y, z), tool.FeedRate);
                path.Feed(new Vec3(pts[0].X, pts[0].Y, z), tool.FeedRate);
            }
            if (path.Moves.Count > 0) path.Rapid(new Vec3(path.Moves[^1].To.X, path.Moves[^1].To.Y, safeZ));
        }
        return path;
    }

    /// <summary>Peck drilling cycle at the given points (expanded to explicit moves, no canned cycles).</summary>
    public static Toolpath Drill(IEnumerable<Vec2> points, Tool tool, double depth, double peck = 0, double safeZ = 5)
    {
        var path = new Toolpath("Drill", tool) { SafeZ = safeZ };
        double tipAllowance = tool.Type == ToolType.Drill ? tool.Diameter * 0.3 : 0; // 118° point
        double total = depth + tipAllowance;
        foreach (var p in OrderNearest(points))
        {
            path.Rapid(new Vec3(p.X, p.Y, safeZ));
            path.Rapid(new Vec3(p.X, p.Y, 1));
            if (peck <= 0) path.Feed(new Vec3(p.X, p.Y, -total), tool.PlungeRate);
            else
                for (double z = -peck; ; z -= peck)
                {
                    double zz = Math.Max(z, -total);
                    path.Feed(new Vec3(p.X, p.Y, zz), tool.PlungeRate);
                    path.Rapid(new Vec3(p.X, p.Y, 1));
                    if (zz <= -total) break;
                    path.Rapid(new Vec3(p.X, p.Y, zz + 0.5));
                }
            path.Rapid(new Vec3(p.X, p.Y, safeZ));
        }
        return path;
    }

    /// <summary>Greedy nearest-neighbour ordering to shorten rapids.</summary>
    public static List<Vec2> OrderNearest(IEnumerable<Vec2> points)
    {
        var rest = points.ToList();
        var r = new List<Vec2>(rest.Count);
        var cur = Vec2.Zero;
        while (rest.Count > 0)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < rest.Count; i++) { var d = (rest[i] - cur).LengthSquared; if (d < bd) { bd = d; best = i; } }
            cur = rest[best]; r.Add(cur); rest.RemoveAt(best);
        }
        return r;
    }
}

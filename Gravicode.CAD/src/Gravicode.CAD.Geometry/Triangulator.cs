namespace Gravicode.CAD.Geometry;

/// <summary>
/// Ear-clipping triangulation of simple polygons with holes. Holes are merged into the outer
/// boundary through bridge edges (Eberly's method), then ears are clipped in O(n²).
/// </summary>
public static class Triangulator
{
    /// <summary>Diagnostics: number of times the clipper had to drop a vertex (degenerate input).</summary>
    public static int Fallbacks;
    /// <summary>Triangulates a profile; returns the flattened vertex list and CCW triangle indices into it.</summary>
    public static (List<Vec2> Vertices, List<int> Indices) Triangulate(Profile2 profile)
    {
        var verts = new List<Vec2>(profile.Outer.Points);
        var ring = Enumerable.Range(0, verts.Count).ToList();

        // vertex order is outer, then holes in profile order (callers rely on this mapping)
        var holeBase = new int[profile.Holes.Count];
        for (int h = 0; h < profile.Holes.Count; h++) { holeBase[h] = verts.Count; verts.AddRange(profile.Holes[h].Points); }

        // merge holes, rightmost-first
        foreach (var hi in Enumerable.Range(0, profile.Holes.Count).OrderByDescending(h => profile.Holes[h].Points.Max(p => p.X)))
        {
            var hole = profile.Holes[hi];
            int baseIdx = holeBase[hi];
            int hm = 0;
            for (int i = 1; i < hole.Count; i++) if (hole.Points[i].X > hole.Points[hm].X) hm = i;
            int bridge = FindBridge(verts, ring, verts[baseIdx + hm]);
            if (bridge < 0) continue;
            int pos = ring.IndexOf(bridge);
            var insert = new List<int>(hole.Count + 2);
            for (int k = 0; k <= hole.Count; k++) insert.Add(baseIdx + (hm + k) % hole.Count);
            insert.Add(bridge);
            ring.InsertRange(pos + 1, insert);
        }

        var tris = new List<int>((ring.Count - 2) * 3);
        ClipEars(verts, ring, tris);
        return (verts, tris);
    }

    public static (List<Vec2> Vertices, List<int> Indices) Triangulate(Polygon2 polygon) => Triangulate(new Profile2(polygon));

    private static int FindBridge(List<Vec2> v, List<int> ring, Vec2 m)
    {
        // Cast a ray to +X from m and find the nearest intersected edge.
        double bestX = double.PositiveInfinity; int best = -1;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = v[ring[i]]; var b = v[ring[(i + 1) % ring.Count]];
            if (Math.Min(a.Y, b.Y) > m.Y || Math.Max(a.Y, b.Y) < m.Y) continue;
            if (Math.Abs(b.Y - a.Y) < 1e-15) continue;
            double t = (m.Y - a.Y) / (b.Y - a.Y);
            if (t < 0 || t > 1) continue;
            double x = a.X + t * (b.X - a.X);
            if (x >= m.X - 1e-12 && x < bestX)
            {
                bestX = x;
                best = a.X > b.X ? ring[i] : ring[(i + 1) % ring.Count];
            }
        }
        if (best < 0)
        {
            // fallback: nearest visible vertex
            double d = double.MaxValue;
            foreach (var idx in ring) { var dd = (v[idx] - m).LengthSquared; if (dd < d) { d = dd; best = idx; } }
            return best;
        }
        // Check reflex vertices inside triangle (m, intersection, best); pick the one with smallest angle.
        var p = new Vec2(bestX, m.Y);
        var pb = v[best];
        // the ray hit a vertex exactly: it is directly visible
        if (Math.Abs(pb.Y - m.Y) <= 1e-12 * Math.Max(1, Math.Abs(m.Y)) || (pb - p).LengthSquared < 1e-24) return best;
        double bestAngle = double.MaxValue; int cand = best;
        foreach (var idx in ring)
        {
            var q = v[idx];
            if (idx == best) continue;
            if (PointInTriangle(q, m, p, pb) && q.X >= m.X)
            {
                double ang = Math.Abs(Math.Atan2(q.Y - m.Y, q.X - m.X));
                if (ang < bestAngle || (Math.Abs(ang - bestAngle) < 1e-12 && (q - m).LengthSquared < (v[cand] - m).LengthSquared)) { bestAngle = ang; cand = idx; }
            }
        }
        return cand;
    }

    private static void ClipEars(List<Vec2> v, List<int> ring, List<int> tris)
    {
        var r = new List<int>(ring);
        int guard = 0, maxIterations = r.Count * 2 + 100; // every iteration removes one vertex
        while (r.Count > 3 && guard < maxIterations)
        {
            bool clipped = false;
            for (int i = 0; i < r.Count; i++)
            {
                int ip = r[(i - 1 + r.Count) % r.Count], ic = r[i], inx = r[(i + 1) % r.Count];
                var a = v[ip]; var b = v[ic]; var c = v[inx];
                double cross = Vec2.Cross(b - a, c - b);
                if (cross <= 1e-14) continue; // reflex or degenerate
                bool empty = true;
                foreach (var j in r)
                {
                    if (j == ip || j == ic || j == inx) continue;
                    var p = v[j];
                    if ((p.ApproxEquals(a, 1e-12)) || p.ApproxEquals(b, 1e-12) || p.ApproxEquals(c, 1e-12)) continue;
                    if (PointStrictlyInTriangle(p, a, b, c)) { empty = false; break; }
                }
                if (!empty) continue;
                tris.Add(ip); tris.Add(ic); tris.Add(inx);
                r.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                // Degenerate input: drop the flattest vertex and continue.
                Fallbacks++;
                int worst = 0; double minAbs = double.MaxValue;
                for (int i = 0; i < r.Count; i++)
                {
                    var a = v[r[(i - 1 + r.Count) % r.Count]]; var b = v[r[i]]; var c = v[r[(i + 1) % r.Count]];
                    var cr = Math.Abs(Vec2.Cross(b - a, c - b));
                    if (cr < minAbs) { minAbs = cr; worst = i; }
                }
                r.RemoveAt(worst);
            }
            guard++;
        }
        if (r.Count == 3 && Vec2.Cross(v[r[1]] - v[r[0]], v[r[2]] - v[r[1]]) > 0) { tris.Add(r[0]); tris.Add(r[1]); tris.Add(r[2]); }
    }

    /// <summary>Interior test that ignores points on the triangle's boundary (collinear CAD loops are common).</summary>
    private static bool PointStrictlyInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        double scale = Math.Max((b - a).LengthSquared, Math.Max((c - b).LengthSquared, (a - c).LengthSquared));
        double eps = 1e-12 * scale;
        double d1 = Vec2.Cross(b - a, p - a), d2 = Vec2.Cross(c - b, p - b), d3 = Vec2.Cross(a - c, p - c);
        return d1 > eps && d2 > eps && d3 > eps;
    }

    private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        double d1 = Vec2.Cross(b - a, p - a), d2 = Vec2.Cross(c - b, p - b), d3 = Vec2.Cross(a - c, p - c);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}

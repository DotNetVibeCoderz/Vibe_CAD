namespace Gravicode.CAD.Geometry;

/// <summary>Mesh clean-up utilities.</summary>
public static class MeshRepair
{
    /// <summary>
    /// Removes T-junctions (vertices lying in the interior of a neighbouring triangle's edge) by splitting
    /// the affected triangles. BSP booleans create these; fixing them makes the result watertight so that
    /// topology queries, feature edges and STEP export behave.
    /// </summary>
    public static Mesh FixTJunctions(Mesh mesh, double relTolerance = 1e-7, int maxPasses = 16)
    {
        var m = mesh.Welded(1e-9);
        double diag = Math.Max(m.Bounds.Diagonal, 1e-9);
        double eps = diag * relTolerance;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            var directed = new HashSet<(int, int)>();
            for (int i = 0; i < m.Indices.Count; i += 3)
                for (int k = 0; k < 3; k++) directed.Add((m.Indices[i + k], m.Indices[i + (k + 1) % 3]));
            var boundaryVerts = new HashSet<int>();
            foreach (var (a, b) in directed) if (!directed.Contains((b, a))) { boundaryVerts.Add(a); boundaryVerts.Add(b); }
            if (boundaryVerts.Count == 0) return m;

            // spatial hash of boundary vertices
            double cell = diag / 128;
            var grid = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) Key(Vec3 p) => ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));
            foreach (var v in boundaryVerts) { var k = Key(m.Positions[v]); if (!grid.TryGetValue(k, out var l)) grid[k] = l = []; l.Add(v); }

            var result = new Mesh(m.VertexCount, m.TriangleCount);
            result.Positions.AddRange(m.Positions);
            bool changed = false;
            for (int t = 0; t < m.TriangleCount; t++)
            {
                int i0 = m.Indices[t * 3], i1 = m.Indices[t * 3 + 1], i2 = m.Indices[t * 3 + 2];
                int[] tri = [i0, i1, i2];
                bool split = false;
                for (int e = 0; e < 3 && !split; e++)
                {
                    int a = tri[e], b = tri[(e + 1) % 3], c = tri[(e + 2) % 3];
                    if (directed.Contains((b, a))) continue; // interior edge
                    var on = PointsOnSegment(m, grid, Key, cell, a, b, eps);
                    if (on.Count == 0) continue;
                    split = true; changed = true;
                    int prev = a;
                    foreach (var v in on) { result.AddTriangle(prev, v, c); prev = v; }
                    result.AddTriangle(prev, b, c);
                }
                if (!split) result.AddTriangle(i0, i1, i2);
            }
            m = result;
            if (!changed) break;
        }
        return m;
    }

    private static List<int> PointsOnSegment(Mesh m, Dictionary<(long, long, long), List<int>> grid, Func<Vec3, (long, long, long)> key, double cell, int a, int b, double eps)
    {
        var pa = m.Positions[a]; var pb = m.Positions[b];
        var d = pb - pa;
        double len2 = d.LengthSquared;
        var found = new List<(double T, int V)>();
        if (len2 < eps * eps) return [];
        // march along the segment, visiting only the cells it passes near (O(length / cell))
        var visited = new HashSet<(long, long, long)>();
        var candidates = new List<int>();
        int steps = (int)Math.Ceiling(Math.Sqrt(len2) / (cell * 0.5)) + 1;
        for (int s = 0; s <= steps; s++)
        {
            var (cx, cy, cz) = key(pa + d * ((double)s / steps));
            for (long x = cx - 1; x <= cx + 1; x++) for (long y = cy - 1; y <= cy + 1; y++) for (long z = cz - 1; z <= cz + 1; z++)
                        if (visited.Add((x, y, z)) && grid.TryGetValue((x, y, z), out var l)) candidates.AddRange(l);
        }
        foreach (var v in candidates)
        {
            if (v == a || v == b) continue;
            var p = m.Positions[v];
            double t = Vec3.Dot(p - pa, d) / len2;
            if (t <= 1e-9 || t >= 1 - 1e-9) continue;
            var closest = pa + d * t;
            if ((p - closest).LengthSquared <= eps * eps * 4) found.Add((t, v));
        }
        found.Sort((p, q) => p.T.CompareTo(q.T));
        return [.. found.Select(f => f.V).Distinct()];
    }
}

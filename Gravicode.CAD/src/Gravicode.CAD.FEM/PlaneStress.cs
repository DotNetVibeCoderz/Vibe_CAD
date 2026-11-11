using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.FEM;

/// <summary>2D triangle mesh used by plane-stress and heat analyses.</summary>
public sealed class TriMesh2D
{
    public List<Vec2> Nodes { get; } = [];
    public List<(int A, int B, int C)> Elements { get; } = [];

    /// <summary>Structured mesh of a rectangle [x0,x0+w]×[y0,y0+h] with nx×ny cells split into triangles.</summary>
    public static TriMesh2D Rectangle(double width, double height, int nx, int ny, double x0 = 0, double y0 = 0)
    {
        var m = new TriMesh2D();
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++) m.Nodes.Add(new(x0 + width * i / nx, y0 + height * j / ny));
        int N(int i, int j) => j * (nx + 1) + i;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                // alternate the diagonal for a more isotropic mesh
                if ((i + j) % 2 == 0) { m.Elements.Add((N(i, j), N(i + 1, j), N(i + 1, j + 1))); m.Elements.Add((N(i, j), N(i + 1, j + 1), N(i, j + 1))); }
                else { m.Elements.Add((N(i, j), N(i + 1, j), N(i, j + 1))); m.Elements.Add((N(i + 1, j), N(i + 1, j + 1), N(i, j + 1))); }
            }
        return m;
    }

    /// <summary>
    /// Meshes an arbitrary profile with roughly uniform triangles of edge length <paramref name="size"/>:
    /// boundaries are resampled, interior points are laid on a staggered grid, the point set is Delaunay-
    /// triangulated (Bowyer–Watson) and triangles outside the region are discarded.
    /// </summary>
    public static TriMesh2D FromProfile(Profile2 profile, double size)
    {
        var pts = new List<Vec2>();
        foreach (var loop in new[] { profile.Outer }.Concat(profile.Holes))
            pts.AddRange(Shapes.Resample(loop, Math.Max(8, (int)Math.Ceiling(loop.Perimeter / size))));
        // staggered (triangular) interior grid kept away from the boundary
        var bbMin = new Vec2(profile.Outer.Points.Min(p => p.X), profile.Outer.Points.Min(p => p.Y));
        var bbMax = new Vec2(profile.Outer.Points.Max(p => p.X), profile.Outer.Points.Max(p => p.Y));
        double dy = size * Math.Sqrt(3) / 2;
        int row = 0;
        for (double y = bbMin.Y + dy; y < bbMax.Y; y += dy, row++)
            for (double x = bbMin.X + (row % 2 == 0 ? size : size / 2); x < bbMax.X; x += size)
            {
                var p = new Vec2(x, y);
                if (!Inside(profile, p) || DistanceToBoundary(profile, p) < size * 0.6) continue;
                pts.Add(p);
            }
        var tris = Delaunay(pts);
        var m = new TriMesh2D();
        m.Nodes.AddRange(pts);
        foreach (var (a, b, c) in tris)
        {
            var centroid = (pts[a] + pts[b] + pts[c]) / 3;
            if (!Inside(profile, centroid)) continue;
            double area = Vec2.Cross(pts[b] - pts[a], pts[c] - pts[a]);
            if (Math.Abs(area) < 1e-12 * size * size) continue;
            m.Elements.Add(area > 0 ? (a, b, c) : (a, c, b));
        }
        return m;
    }

    private static bool Inside(Profile2 p, Vec2 q) => p.Outer.Contains(q) && !p.Holes.Any(h => h.Contains(q));

    private static double DistanceToBoundary(Profile2 p, Vec2 q)
    {
        double best = double.MaxValue;
        foreach (var loop in new[] { p.Outer }.Concat(p.Holes))
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i]; var b = loop[i + 1];
                var d = b - a;
                double t = Math.Clamp(Vec2.Dot(q - a, d) / Math.Max(d.LengthSquared, 1e-30), 0, 1);
                best = Math.Min(best, Vec2.Distance(q, a + d * t));
            }
        return best;
    }

    /// <summary>Bowyer–Watson Delaunay triangulation of a point set (indices into <paramref name="pts"/>).</summary>
    public static List<(int A, int B, int C)> Delaunay(IReadOnlyList<Vec2> pts)
    {
        double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y), maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
        double span = Math.Max(maxX - minX, maxY - minY) * 20 + 1;
        var mid = new Vec2((minX + maxX) / 2, (minY + maxY) / 2);
        var all = new List<Vec2>(pts) { mid + new Vec2(-span, -span), mid + new Vec2(span, -span), mid + new Vec2(0, span) };
        int s0 = pts.Count, s1 = s0 + 1, s2 = s0 + 2;
        var tris = new List<(int A, int B, int C, Vec2 Cc, double R2)> { Make(s0, s1, s2) };

        // insertion in spatially coherent (sorted) order keeps the cavity search cheap enough for FEM meshes
        foreach (int i in Enumerable.Range(0, pts.Count).OrderBy(i => Math.Round(pts[i].Y / (span / 400))).ThenBy(i => pts[i].X))
        {
            var p = all[i];
            var bad = new List<int>();
            for (int t = 0; t < tris.Count; t++) if ((tris[t].Cc - p).LengthSquared <= tris[t].R2 * (1 + 1e-12)) bad.Add(t);
            var edges = new Dictionary<(int, int), int>();
            foreach (var t in bad)
            {
                var (a, b, c, _, _) = tris[t];
                foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
                {
                    var k = u < v ? (u, v) : (v, u);
                    edges[k] = edges.GetValueOrDefault(k) + 1;
                }
            }
            for (int k = bad.Count - 1; k >= 0; k--) { tris[bad[k]] = tris[^1]; tris.RemoveAt(tris.Count - 1); }
            foreach (var ((u, v), count) in edges) if (count == 1) tris.Add(Make(u, v, i));
        }
        return [.. tris.Where(t => t.A < s0 && t.B < s0 && t.C < s0).Select(t => (t.A, t.B, t.C))];

        (int, int, int, Vec2, double) Make(int a, int b, int c)
        {
            Vec2 pa = all[a], pb = all[b], pc = all[c];
            double d = 2 * (pa.X * (pb.Y - pc.Y) + pb.X * (pc.Y - pa.Y) + pc.X * (pa.Y - pb.Y));
            if (Math.Abs(d) < 1e-300) return (a, b, c, pa, double.MaxValue);
            double ux = (pa.LengthSquared * (pb.Y - pc.Y) + pb.LengthSquared * (pc.Y - pa.Y) + pc.LengthSquared * (pa.Y - pb.Y)) / d;
            double uy = (pa.LengthSquared * (pc.X - pb.X) + pb.LengthSquared * (pa.X - pc.X) + pc.LengthSquared * (pb.X - pa.X)) / d;
            var cc = new Vec2(ux, uy);
            return (a, b, c, cc, (pa - cc).LengthSquared);
        }
    }

    /// <summary>Uniform 1→4 subdivision.</summary>
    public TriMesh2D Subdivide()
    {
        var r = new TriMesh2D();
        r.Nodes.AddRange(Nodes);
        var mid = new Dictionary<(int, int), int>();
        int M(int a, int b)
        {
            var k = a < b ? (a, b) : (b, a);
            if (!mid.TryGetValue(k, out var i)) { i = r.Nodes.Count; r.Nodes.Add((Nodes[a] + Nodes[b]) * 0.5); mid[k] = i; }
            return i;
        }
        foreach (var (a, b, c) in Elements)
        {
            int ab = M(a, b), bc = M(b, c), ca = M(c, a);
            r.Elements.Add((a, ab, ca)); r.Elements.Add((ab, b, bc)); r.Elements.Add((ca, bc, c)); r.Elements.Add((ab, bc, ca));
        }
        return r;
    }

    public IEnumerable<int> NodesWhere(Func<Vec2, bool> predicate) => Enumerable.Range(0, Nodes.Count).Where(i => predicate(Nodes[i]));
}

/// <summary>
/// Linear plane-stress analysis with constant-strain triangles (CST). Typical use: brackets, plates
/// and beams loaded in their plane. Units: mm, N, MPa.
/// </summary>
public sealed class PlaneStressModel(TriMesh2D mesh, Material material, double thickness)
{
    public TriMesh2D Mesh { get; } = mesh;
    public Material Material { get; } = material;
    public double Thickness { get; } = thickness;
    private readonly Dictionary<int, double> _fixed = [];
    private readonly double[] _loads = new double[mesh.Nodes.Count * 2];

    public PlaneStressModel FixNodes(IEnumerable<int> nodes, bool x = true, bool y = true)
    {
        foreach (var n in nodes) { if (x) _fixed[n * 2] = 0; if (y) _fixed[n * 2 + 1] = 0; }
        return this;
    }
    public PlaneStressModel FixWhere(Func<Vec2, bool> where, bool x = true, bool y = true) => FixNodes(Mesh.NodesWhere(where), x, y);

    public PlaneStressModel LoadNode(int node, Vec2 force) { _loads[node * 2] += force.X; _loads[node * 2 + 1] += force.Y; return this; }

    /// <summary>Distributes a total force equally over the nodes matching <paramref name="where"/>.</summary>
    public PlaneStressModel LoadWhere(Func<Vec2, bool> where, Vec2 totalForce)
    {
        var nodes = Mesh.NodesWhere(where).ToList();
        if (nodes.Count == 0) throw new ArgumentException("No nodes match the load region.");
        foreach (var n in nodes) LoadNode(n, totalForce / nodes.Count);
        return this;
    }

    public sealed record Result(Vec2[] Displacements, double[] VonMises, (double Sx, double Sy, double Txy)[] Stresses, int Iterations, double Residual)
    {
        public double MaxDisplacement => Displacements.Max(d => d.Length);
        public double MaxVonMises => VonMises.Max();
    }

    public Result Solve()
    {
        int ndof = Mesh.Nodes.Count * 2;
        double E = Material.YoungsModulus, nu = Material.PoissonRatio, t = Thickness;
        double c = E / (1 - nu * nu);
        double[,] D = { { c, c * nu, 0 }, { c * nu, c, 0 }, { 0, 0, c * (1 - nu) / 2 } };

        var sol = LinearSystem.Solve(ndof, sink =>
        {
            Span<double> B = stackalloc double[18];
            foreach (var el in Mesh.Elements)
            {
                double area = Bmatrix(el, B);
                Span<int> dofs = [el.A * 2, el.A * 2 + 1, el.B * 2, el.B * 2 + 1, el.C * 2, el.C * 2 + 1];
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                    {
                        double s = 0;
                        for (int k = 0; k < 3; k++)
                            for (int l = 0; l < 3; l++) s += B[k * 6 + i] * D[k, l] * B[l * 6 + j];
                        sink.Add(dofs[i], dofs[j], s * area * t);
                    }
            }
        }, _loads, _fixed, 1e-11);

        var disp = Enumerable.Range(0, Mesh.Nodes.Count).Select(i => new Vec2(sol.U[i * 2], sol.U[i * 2 + 1])).ToArray();
        var vm = new double[Mesh.Elements.Count];
        var st = new (double, double, double)[Mesh.Elements.Count];
        Span<double> Bm = stackalloc double[18];
        Span<double> eps = stackalloc double[3];
        for (int e = 0; e < Mesh.Elements.Count; e++)
        {
            var el = Mesh.Elements[e];
            Bmatrix(el, Bm);
            Span<double> u = [sol.U[el.A * 2], sol.U[el.A * 2 + 1], sol.U[el.B * 2], sol.U[el.B * 2 + 1], sol.U[el.C * 2], sol.U[el.C * 2 + 1]];
            for (int k = 0; k < 3; k++) { double s = 0; for (int i = 0; i < 6; i++) s += Bm[k * 6 + i] * u[i]; eps[k] = s; }
            double sx = D[0, 0] * eps[0] + D[0, 1] * eps[1], sy = D[1, 0] * eps[0] + D[1, 1] * eps[1], txy = D[2, 2] * eps[2];
            st[e] = (sx, sy, txy);
            vm[e] = Math.Sqrt(sx * sx - sx * sy + sy * sy + 3 * txy * txy);
        }
        return new Result(disp, vm, st, sol.Iterations, sol.Residual);
    }

    private double Bmatrix((int A, int B, int C) el, Span<double> B)
    {
        var p1 = Mesh.Nodes[el.A]; var p2 = Mesh.Nodes[el.B]; var p3 = Mesh.Nodes[el.C];
        double area2 = (p2.X - p1.X) * (p3.Y - p1.Y) - (p3.X - p1.X) * (p2.Y - p1.Y);
        double b1 = p2.Y - p3.Y, b2 = p3.Y - p1.Y, b3 = p1.Y - p2.Y;
        double c1 = p3.X - p2.X, c2 = p1.X - p3.X, c3 = p2.X - p1.X;
        B.Clear();
        double inv = 1 / area2;
        B[0] = b1 * inv; B[2] = b2 * inv; B[4] = b3 * inv;
        B[7] = c1 * inv; B[9] = c2 * inv; B[11] = c3 * inv;
        B[12] = c1 * inv; B[13] = b1 * inv; B[14] = c2 * inv; B[15] = b2 * inv; B[16] = c3 * inv; B[17] = b3 * inv;
        return Math.Abs(area2) / 2;
    }

    /// <summary>Extruded result model with elements coloured by von Mises stress (one body per colour band).</summary>
    public CadModel ToModel(Result result, double displacementScale = 0, int bands = 12)
    {
        var model = new CadModel("PlaneStress");
        double max = Math.Max(result.MaxVonMises, 1e-12);
        var meshes = Enumerable.Range(0, bands).Select(_ => new Mesh()).ToArray();
        // outer boundary edges (used by exactly one element) get side walls; interior faces are skipped,
        // so the borders between colour bands read as stress contour lines.
        var edgeUse = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in Mesh.Elements)
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) }) { var k = u < v ? (u, v) : (v, u); edgeUse[k] = edgeUse.GetValueOrDefault(k) + 1; }
        Vec2 P(int i) => Mesh.Nodes[i] + result.Displacements[i] * displacementScale;
        double t = Thickness;
        for (int e = 0; e < Mesh.Elements.Count; e++)
        {
            var (a, b, c) = Mesh.Elements[e];
            int band = Math.Min(bands - 1, (int)(result.VonMises[e] / max * bands));
            var m = meshes[band];
            Vec2 pa = P(a), pb = P(b), pc = P(c);
            m.AddTriangle(pa.ToVec3(t), pb.ToVec3(t), pc.ToVec3(t));      // top
            m.AddTriangle(pa.ToVec3(0), pc.ToVec3(0), pb.ToVec3(0));      // bottom
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                if (edgeUse[u < v ? (u, v) : (v, u)] != 1) continue;
                Vec2 pu = P(u), pv = P(v);
                m.AddTriangle(pu.ToVec3(0), pv.ToVec3(0), pv.ToVec3(t));
                m.AddTriangle(pu.ToVec3(0), pv.ToVec3(t), pu.ToVec3(t));
            }
        }
        for (int i = 0; i < bands; i++)
            if (!meshes[i].IsEmpty)
                model.AddBody($"σ {max * i / bands:0.#}–{max * (i + 1) / bands:0.#} MPa", meshes[i].Welded(1e-9), ColorMap.Rainbow((i + 0.5) / bands)).Roughness = 0.6;
        model.Metadata["maxVonMises"] = max.ToString("G6");
        model.Metadata["maxDisplacement"] = result.MaxDisplacement.ToString("G6");
        return model;
    }
}

/// <summary>Steady-state 2D heat conduction (linear triangles). k in W/(mm·K), temperatures in °C.</summary>
public sealed class HeatConduction2D(TriMesh2D mesh, double conductivity, double thickness = 1)
{
    public TriMesh2D Mesh { get; } = mesh;
    private readonly Dictionary<int, double> _temps = [];
    private readonly double[] _q = new double[mesh.Nodes.Count];

    public HeatConduction2D Temperature(Func<Vec2, bool> where, double celsius)
    {
        foreach (var n in Mesh.NodesWhere(where)) _temps[n] = celsius;
        return this;
    }

    /// <summary>Total heat input (W) spread over matching nodes.</summary>
    public HeatConduction2D HeatInput(Func<Vec2, bool> where, double watts)
    {
        var nodes = Mesh.NodesWhere(where).ToList();
        foreach (var n in nodes) _q[n] += watts / nodes.Count;
        return this;
    }

    public double[] Solve()
    {
        var sol = LinearSystem.Solve(Mesh.Nodes.Count, sink =>
        {
            foreach (var (a, b, c) in Mesh.Elements)
            {
                var p1 = Mesh.Nodes[a]; var p2 = Mesh.Nodes[b]; var p3 = Mesh.Nodes[c];
                double area = Math.Abs((p2.X - p1.X) * (p3.Y - p1.Y) - (p3.X - p1.X) * (p2.Y - p1.Y)) / 2;
                Span<double> bb = [p2.Y - p3.Y, p3.Y - p1.Y, p1.Y - p2.Y];
                Span<double> cc = [p3.X - p2.X, p1.X - p3.X, p2.X - p1.X];
                Span<int> n = [a, b, c];
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        sink.Add(n[i], n[j], conductivity * thickness * (bb[i] * bb[j] + cc[i] * cc[j]) / (4 * area));
            }
        }, _q, _temps);
        return sol.U;
    }
}

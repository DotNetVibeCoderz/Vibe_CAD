namespace Gravicode.CAD.FEM;

/// <summary>Pin-jointed truss (2D when all Z = 0 and the Z DOFs are fixed automatically).</summary>
public sealed class Truss
{
    public sealed record Member(int A, int B, double Area, Material Material);
    public List<Vec3> Nodes { get; } = [];
    public List<Member> Members { get; } = [];
    private readonly Dictionary<int, double> _fixed = [];
    private readonly Dictionary<int, Vec3> _loads = [];

    public int AddNode(double x, double y, double z = 0) { Nodes.Add(new(x, y, z)); return Nodes.Count - 1; }
    public Truss AddMember(int a, int b, double area, Material? m = null) { Members.Add(new(a, b, area, m ?? Material.Steel)); return this; }
    /// <summary>Fixes translations of a node (pinned support). Use rollerX/rollerY to free an axis.</summary>
    public Truss Support(int node, bool x = true, bool y = true, bool z = true)
    {
        if (x) _fixed[node * 3] = 0; if (y) _fixed[node * 3 + 1] = 0; if (z) _fixed[node * 3 + 2] = 0;
        return this;
    }
    public Truss Load(int node, Vec3 force) { _loads[node] = _loads.GetValueOrDefault(node) + force; return this; }

    public sealed record Result(Vec3[] Displacements, double[] AxialForces, double[] Stresses, Vec3[] Reactions, int Iterations)
    {
        public double MaxDisplacement => Displacements.Max(d => d.Length);
        public double MaxStress => Stresses.Max(Math.Abs);
    }

    public Result Solve()
    {
        int n = Nodes.Count, ndof = n * 3;
        bool planar = Nodes.All(p => Math.Abs(p.Z) < 1e-12);
        var fixedDofs = new Dictionary<int, double>(_fixed);
        if (planar) for (int i = 0; i < n; i++) fixedDofs[i * 3 + 2] = 0;
        // nodes that no member touches are fixed to keep the system non-singular
        var used = new HashSet<int>(Members.SelectMany(m => new[] { m.A, m.B }));
        for (int i = 0; i < n; i++) if (!used.Contains(i)) { fixedDofs[i * 3] = 0; fixedDofs[i * 3 + 1] = 0; fixedDofs[i * 3 + 2] = 0; }

        var f = new double[ndof];
        foreach (var (node, F) in _loads) { f[node * 3] += F.X; f[node * 3 + 1] += F.Y; f[node * 3 + 2] += F.Z; }

        var sol = LinearSystem.Solve(ndof, sink =>
        {
            foreach (var m in Members)
            {
                var d = Nodes[m.B] - Nodes[m.A];
                double L = d.Length;
                var c = d / L;
                double k = m.Material.YoungsModulus * m.Area / L;
                Span<double> cc = [c.X, c.Y, c.Z];
                Span<int> dofs = [m.A * 3, m.A * 3 + 1, m.A * 3 + 2, m.B * 3, m.B * 3 + 1, m.B * 3 + 2];
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                    {
                        double s = k * cc[i % 3] * cc[j % 3] * ((i < 3) == (j < 3) ? 1 : -1);
                        sink.Add(dofs[i], dofs[j], s);
                    }
            }
        }, f, fixedDofs);

        var disp = Enumerable.Range(0, n).Select(i => new Vec3(sol.U[i * 3], sol.U[i * 3 + 1], sol.U[i * 3 + 2])).ToArray();
        var react = Enumerable.Range(0, n).Select(i => new Vec3(sol.Reactions[i * 3], sol.Reactions[i * 3 + 1], sol.Reactions[i * 3 + 2])).ToArray();
        var forces = new double[Members.Count]; var stresses = new double[Members.Count];
        for (int e = 0; e < Members.Count; e++)
        {
            var m = Members[e];
            var d = Nodes[m.B] - Nodes[m.A];
            double L = d.Length;
            double elong = Vec3.Dot(disp[m.B] - disp[m.A], d / L);
            stresses[e] = m.Material.YoungsModulus * elong / L;
            forces[e] = stresses[e] * m.Area;
        }
        return new Result(disp, forces, stresses, react, sol.Iterations);
    }

    /// <summary>3D model of the truss: members as cylinders coloured by axial stress (blue = compression, red = tension).</summary>
    public CadModel ToModel(Result? result = null, double memberRadius = 0, double displacementScale = 0)
    {
        var model = new CadModel("Truss");
        double span = BoundingBox.FromPoints(Nodes).Diagonal;
        if (memberRadius <= 0) memberRadius = span / 150;
        double maxS = result?.MaxStress is > 0 and var ms ? ms : 1;
        for (int e = 0; e < Members.Count; e++)
        {
            var m = Members[e];
            Vec3 a = Nodes[m.A], b = Nodes[m.B];
            if (result != null && displacementScale != 0) { a += result.Displacements[m.A] * displacementScale; b += result.Displacements[m.B] * displacementScale; }
            var mesh = Geometry.Shapes.Cylinder(memberRadius, (b - a).Length, 16);
            var z = (b - a).Normalized();
            var x = z.AnyPerpendicular(); var y = Vec3.Cross(z, x);
            mesh = mesh.Transformed(Mat4.FromBasis(a, x, y, z));
            var color = result == null ? CadColor.Steel : ColorMap.Diverging(result.Stresses[e] / maxS);
            model.AddBody($"member{e}", mesh, color).Roughness = 0.5;
        }
        foreach (var (i, p) in Nodes.Select((p, i) => (i, p)))
        {
            var pos = result != null && displacementScale != 0 ? p + result.Displacements[i] * displacementScale : p;
            model.AddBody($"node{i}", Geometry.Shapes.Sphere(memberRadius * 1.8, 16, 8).Translated(pos), CadColor.Graphite);
        }
        return model;
    }
}

/// <summary>Colour maps for result plots.</summary>
public static class ColorMap
{
    /// <summary>Turbo-like rainbow for t ∈ [0,1].</summary>
    public static CadColor Rainbow(double t)
    {
        t = Math.Clamp(t, 0, 1);
        CadColor[] stops = [CadColor.FromHex("#30123B"), CadColor.FromHex("#4686FB"), CadColor.FromHex("#1AE4B6"), CadColor.FromHex("#A2FC3C"), CadColor.FromHex("#FABA39"), CadColor.FromHex("#E4460A"), CadColor.FromHex("#7A0403")];
        double f = t * (stops.Length - 1);
        int i = Math.Min((int)f, stops.Length - 2);
        return CadColor.Lerp(stops[i], stops[i + 1], f - i);
    }

    /// <summary>Blue (−1) → white (0) → red (+1).</summary>
    public static CadColor Diverging(double t)
    {
        t = Math.Clamp(t, -1, 1);
        return t < 0 ? CadColor.Lerp(CadColor.FromHex("#F2F2F2"), CadColor.FromHex("#2F6FDE"), -t) : CadColor.Lerp(CadColor.FromHex("#F2F2F2"), CadColor.FromHex("#E4460A"), t);
    }
}

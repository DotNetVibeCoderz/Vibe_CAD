namespace Gravicode.CAD.Geometry;

/// <summary>
/// Constructive solid geometry on closed meshes using BSP trees (after Evan Wallace's csg.js),
/// in double precision. Inputs must be closed and outward oriented.
/// </summary>
public static class Csg
{
    public static Mesh Union(Mesh a, Mesh b)
    {
        if (!a.Bounds.Intersects(b.Bounds)) { var r = a.Clone(); r.Append(b); return r; }
        var na = new Node(ToPolys(a)); var nb = new Node(ToPolys(b));
        na.ClipTo(nb); nb.ClipTo(na); nb.Invert(); nb.ClipTo(na); nb.Invert();
        na.Build(nb.AllPolygons());
        return ToMesh(na.AllPolygons());
    }

    public static Mesh Subtract(Mesh a, Mesh b)
    {
        if (!a.Bounds.Intersects(b.Bounds)) return a.Clone();
        var na = new Node(ToPolys(a)); var nb = new Node(ToPolys(b));
        na.Invert(); na.ClipTo(nb); nb.ClipTo(na); nb.Invert(); nb.ClipTo(na); nb.Invert();
        na.Build(nb.AllPolygons()); na.Invert();
        return ToMesh(na.AllPolygons());
    }

    public static Mesh Intersect(Mesh a, Mesh b)
    {
        if (!a.Bounds.Intersects(b.Bounds)) return new Mesh();
        var na = new Node(ToPolys(a)); var nb = new Node(ToPolys(b));
        na.Invert(); nb.ClipTo(na); nb.Invert(); na.ClipTo(nb); nb.ClipTo(na);
        na.Build(nb.AllPolygons()); na.Invert();
        return ToMesh(na.AllPolygons());
    }

    public static Mesh UnionAll(IEnumerable<Mesh> meshes)
    {
        // balanced pairwise reduction keeps BSP trees small
        var list = meshes.ToList();
        if (list.Count == 0) return new Mesh();
        while (list.Count > 1)
        {
            var next = new List<Mesh>((list.Count + 1) / 2);
            for (int i = 0; i < list.Count; i += 2) next.Add(i + 1 < list.Count ? Union(list[i], list[i + 1]) : list[i]);
            list = next;
        }
        return list[0];
    }

    public static Mesh SubtractAll(Mesh a, IEnumerable<Mesh> tools)
    {
        var t = tools.ToList();
        if (t.Count == 0) return a;
        // subtract the union of disjoint tools in one pass when they don't overlap each other
        return Subtract(a, UnionAll(t));
    }

    // ---------------- internals ----------------

    private sealed class Poly(List<Vec3> v, CsgPlane plane)
    {
        public List<Vec3> V = v;
        public CsgPlane Plane = plane;
        public Poly Flipped() { var c = new List<Vec3>(V); c.Reverse(); return new Poly(c, Plane.Flipped()); }
    }

    private readonly record struct CsgPlane(Vec3 N, double W)
    {
        public static CsgPlane? From(Vec3 a, Vec3 b, Vec3 c)
        {
            var n = Vec3.Cross(b - a, c - a);
            var l = n.Length;
            if (l < 1e-14) return null;
            n /= l;
            return new CsgPlane(n, Vec3.Dot(n, a));
        }
        public CsgPlane Flipped() => new(-N, -W);
    }

    private const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

    private static void SplitPolygon(CsgPlane plane, Poly p, List<Poly> cf, List<Poly> cb, List<Poly> f, List<Poly> b)
    {
        const double eps = Tolerance.Boolean;
        int type = 0;
        Span<int> types = p.V.Count <= 64 ? stackalloc int[p.V.Count] : new int[p.V.Count];
        for (int i = 0; i < p.V.Count; i++)
        {
            double t = Vec3.Dot(plane.N, p.V[i]) - plane.W;
            int ty = t < -eps ? Back : t > eps ? Front : Coplanar;
            type |= ty; types[i] = ty;
        }
        switch (type)
        {
            case Coplanar: (Vec3.Dot(plane.N, p.Plane.N) > 0 ? cf : cb).Add(p); break;
            case Front: f.Add(p); break;
            case Back: b.Add(p); break;
            default:
                var fv = new List<Vec3>(p.V.Count + 1); var bv = new List<Vec3>(p.V.Count + 1);
                for (int i = 0; i < p.V.Count; i++)
                {
                    int j = (i + 1) % p.V.Count;
                    int ti = types[i], tj = types[j];
                    var vi = p.V[i]; var vj = p.V[j];
                    if (ti != Back) fv.Add(vi);
                    if (ti != Front) bv.Add(vi);
                    if ((ti | tj) == Spanning)
                    {
                        double t = (plane.W - Vec3.Dot(plane.N, vi)) / Vec3.Dot(plane.N, vj - vi);
                        var v = Vec3.Lerp(vi, vj, t);
                        fv.Add(v); bv.Add(v);
                    }
                }
                if (fv.Count >= 3) f.Add(new Poly(fv, p.Plane));
                if (bv.Count >= 3) b.Add(new Poly(bv, p.Plane));
                break;
        }
    }

    private sealed class Node
    {
        private CsgPlane? _plane;
        private Node? _front, _back;
        private List<Poly> _polys = [];

        public Node(List<Poly>? polys = null) { if (polys != null && polys.Count > 0) Build(polys); }

        public void Invert()
        {
            // iterative to avoid deep recursion on large trees
            var stack = new Stack<Node>(); stack.Push(this);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                for (int i = 0; i < n._polys.Count; i++) n._polys[i] = n._polys[i].Flipped();
                if (n._plane is { } pl) n._plane = pl.Flipped();
                (n._front, n._back) = (n._back, n._front);
                if (n._front != null) stack.Push(n._front);
                if (n._back != null) stack.Push(n._back);
            }
        }

        public List<Poly> ClipPolygons(List<Poly> polys)
        {
            if (_plane is not { } pl) return [.. polys];
            var f = new List<Poly>(); var b = new List<Poly>();
            foreach (var p in polys) SplitPolygon(pl, p, f, b, f, b);
            if (_front != null) f = _front.ClipPolygons(f);
            b = _back != null ? _back.ClipPolygons(b) : [];
            f.AddRange(b);
            return f;
        }

        public void ClipTo(Node bsp)
        {
            var stack = new Stack<Node>(); stack.Push(this);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                n._polys = bsp.ClipPolygons(n._polys);
                if (n._front != null) stack.Push(n._front);
                if (n._back != null) stack.Push(n._back);
            }
        }

        public List<Poly> AllPolygons()
        {
            var r = new List<Poly>();
            var stack = new Stack<Node>(); stack.Push(this);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                r.AddRange(n._polys);
                if (n._front != null) stack.Push(n._front);
                if (n._back != null) stack.Push(n._back);
            }
            return r;
        }

        public void Build(List<Poly> polys)
        {
            var work = new Stack<(Node, List<Poly>)>();
            work.Push((this, polys));
            while (work.Count > 0)
            {
                var (n, ps) = work.Pop();
                if (ps.Count == 0) continue;
                n._plane ??= ps[ps.Count / 2].Plane; // middle pick gives better balance than the first polygon
                var pl = n._plane.Value;
                var f = new List<Poly>(); var b = new List<Poly>();
                foreach (var p in ps) SplitPolygon(pl, p, n._polys, n._polys, f, b);
                if (f.Count > 0) { n._front ??= new Node(); work.Push((n._front, f)); }
                if (b.Count > 0) { n._back ??= new Node(); work.Push((n._back, b)); }
            }
        }
    }

    private static List<Poly> ToPolys(Mesh m)
    {
        var r = new List<Poly>(m.TriangleCount);
        for (int t = 0; t < m.TriangleCount; t++)
        {
            var (a, b, c) = m.GetTriangle(t);
            if (CsgPlane.From(a, b, c) is { } pl) r.Add(new Poly([a, b, c], pl));
        }
        return r;
    }

    private static Mesh ToMesh(List<Poly> polys)
    {
        var m = new Mesh();
        var map = new Dictionary<(long, long, long), int>();
        const double q = 1e7; // 0.1 micron welding grid
        int Idx(Vec3 p)
        {
            var k = ((long)Math.Round(p.X * q), (long)Math.Round(p.Y * q), (long)Math.Round(p.Z * q));
            if (!map.TryGetValue(k, out var i)) { i = m.AddVertex(p); map[k] = i; }
            return i;
        }
        foreach (var p in polys)
        {
            int i0 = Idx(p.V[0]);
            for (int i = 1; i + 1 < p.V.Count; i++)
            {
                int i1 = Idx(p.V[i]), i2 = Idx(p.V[i + 1]);
                if (i0 != i1 && i1 != i2 && i0 != i2) m.AddTriangle(i0, i1, i2);
            }
        }
        return MeshRepair.FixTJunctions(m);
    }
}

/// <summary>Fluent boolean helpers on <see cref="Mesh"/>.</summary>
public static class MeshBooleanExtensions
{
    public static Mesh Union(this Mesh a, Mesh b) => Csg.Union(a, b);
    public static Mesh Subtract(this Mesh a, Mesh b) => Csg.Subtract(a, b);
    public static Mesh Intersect(this Mesh a, Mesh b) => Csg.Intersect(a, b);
}

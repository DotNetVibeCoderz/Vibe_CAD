using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Topology;

/// <summary>Surface type recognised for a face region.</summary>
public enum SurfaceKind { Planar, Curved }

/// <summary>A face: a connected set of triangles on one surface, with its boundary loops.</summary>
public sealed class BrepFace
{
    public int Id { get; init; }
    public SurfaceKind Kind { get; init; }
    public List<int> Triangles { get; } = [];
    /// <summary>Outer loop first, inner loops (holes) after — as vertex indices of <see cref="BrepSolid.Mesh"/>.</summary>
    public List<List<int>> Loops { get; } = [];
    public Vec3 Normal { get; init; }
    public double Area { get; set; }
}

/// <summary>
/// Lightweight boundary representation recovered from a mesh: planar faces are merged by
/// coplanarity, remaining smooth regions by normal continuity. Used for STEP export with
/// real faces, face picking, and "select top face" style queries in the parametric layer.
/// </summary>
public sealed class BrepSolid
{
    public Mesh Mesh { get; }
    public List<BrepFace> Faces { get; } = [];

    private BrepSolid(Mesh mesh) { Mesh = mesh; }

    public static BrepSolid FromMesh(Mesh mesh, double planarToleranceDeg = 0.5, double smoothAngleDeg = 25)
    {
        var he = new HalfEdgeMesh(mesh);
        var m = he.Mesh;
        var s = new BrepSolid(m);
        var faceOf = new int[m.TriangleCount];
        Array.Fill(faceOf, -1);
        double cosPlanar = Math.Cos(Units.Deg(planarToleranceDeg));
        double cosSmooth = Math.Cos(Units.Deg(smoothAngleDeg));
        var normals = Enumerable.Range(0, m.TriangleCount).Select(m.FaceNormal).ToArray();

        for (int seed = 0; seed < m.TriangleCount; seed++)
        {
            if (faceOf[seed] >= 0) continue;
            var n0 = normals[seed];
            var (a0, _, _) = m.GetTriangle(seed);
            var plane = Plane.FromPointNormal(a0, n0);
            // try planar growth first
            var region = Grow(seed, t => Vec3.Dot(normals[t], n0) >= cosPlanar && Math.Abs(plane.SignedDistance(m.GetTriangle(t).A)) < 1e-4);
            var kind = SurfaceKind.Planar;
            if (region.Count == 1 && IsCurvedNeighbourhood(seed))
            {
                region = Grow(seed, t => true, smooth: true);
                kind = region.Count > 1 ? SurfaceKind.Curved : SurfaceKind.Planar;
            }
            var face = new BrepFace { Id = s.Faces.Count, Kind = kind, Normal = n0 };
            foreach (var t in region) { faceOf[t] = face.Id; face.Triangles.Add(t); }
            s.Faces.Add(face);
        }

        foreach (var f in s.Faces)
        {
            f.Area = f.Triangles.Sum(t => { var (a, b, c) = m.GetTriangle(t); return Vec3.Cross(b - a, c - a).Length / 2; });
            f.Loops.AddRange(ExtractLoops(he, f, faceOf));
        }
        return s;

        bool IsCurvedNeighbourhood(int t)
        {
            for (int k = 0; k < 3; k++)
            {
                int tw = he.Twin[t * 3 + k];
                if (tw < 0) continue;
                double d = Vec3.Dot(normals[t], normals[HalfEdgeMesh.FaceOf(tw)]);
                if (d < cosPlanar && d >= cosSmooth) return true;
            }
            return false;
        }

        List<int> Grow(int seed, Func<int, bool> accept, bool smooth = false)
        {
            var region = new List<int> { seed };
            var inRegion = new HashSet<int> { seed };
            var q = new Queue<int>(); q.Enqueue(seed);
            while (q.Count > 0)
            {
                int t = q.Dequeue();
                for (int k = 0; k < 3; k++)
                {
                    int tw = he.Twin[t * 3 + k];
                    if (tw < 0) continue;
                    int nt = HalfEdgeMesh.FaceOf(tw);
                    if (faceOf[nt] >= 0 || inRegion.Contains(nt)) continue;
                    if (smooth ? Vec3.Dot(normals[t], normals[nt]) < cosSmooth : !accept(nt)) continue;
                    inRegion.Add(nt); region.Add(nt); q.Enqueue(nt);
                }
            }
            return region;
        }
    }

    private static List<List<int>> ExtractLoops(HalfEdgeMesh he, BrepFace f, int[] faceOf)
    {
        var next = new Dictionary<int, int>();
        foreach (var t in f.Triangles)
            for (int k = 0; k < 3; k++)
            {
                int h = t * 3 + k, tw = he.Twin[h];
                if (tw < 0 || faceOf[HalfEdgeMesh.FaceOf(tw)] != f.Id) next.TryAdd(he.Origin[h], he.Target(h));
            }
        var loops = new List<List<int>>();
        var used = new HashSet<int>();
        foreach (var s in next.Keys)
        {
            if (used.Contains(s)) continue;
            var loop = new List<int>();
            int v = s;
            while (used.Add(v)) { loop.Add(v); if (!next.TryGetValue(v, out v)) break; }
            if (loop.Count >= 3) loops.Add(loop);
        }
        // outer loop = largest projected area
        if (loops.Count > 1)
        {
            var u = f.Normal.AnyPerpendicular(); var w = Vec3.Cross(f.Normal, u);
            loops.Sort((a, b) => Area(b).CompareTo(Area(a)));
            double Area(List<int> l) => new Polygon2(l.Select(i => { var p = he.Mesh.Positions[i]; return new Vec2(Vec3.Dot(p, u), Vec3.Dot(p, w)); })).Area;
        }
        return loops;
    }

    public int PlanarFaceCount => Faces.Count(f => f.Kind == SurfaceKind.Planar);
    public int CurvedFaceCount => Faces.Count(f => f.Kind == SurfaceKind.Curved);

    /// <summary>The face whose normal best matches <paramref name="direction"/> (e.g. +Z for "top").</summary>
    public BrepFace SelectFace(Vec3 direction) =>
        Faces.Where(f => f.Kind == SurfaceKind.Planar).OrderByDescending(f => Vec3.Dot(f.Normal, direction.Normalized()))
             .ThenByDescending(f => Centroid(f) is var c ? Vec3.Dot(c, direction) : 0).First();

    public Vec3 Centroid(BrepFace f)
    {
        Vec3 acc = Vec3.Zero; double area = 0;
        foreach (var t in f.Triangles)
        {
            var (a, b, c) = Mesh.GetTriangle(t);
            double ar = Vec3.Cross(b - a, c - a).Length / 2;
            acc += (a + b + c) / 3 * ar; area += ar;
        }
        return area > 0 ? acc / area : Vec3.Zero;
    }
}

using System.Runtime.InteropServices;

namespace Gravicode.CAD;

/// <summary>
/// Hot-path mesh algorithms. The managed implementation is always available;
/// <c>Gravicode.CAD.Native</c> swaps in the Rust engine via <see cref="MeshKernel.Current"/>.
/// </summary>
public interface IMeshKernel
{
    string Name { get; }
    MeshProperties ComputeProperties(Mesh mesh);
    void ComputeVertexNormals(Mesh mesh);
    Mesh Weld(Mesh mesh, double tolerance);
    List<(Vec3 A, Vec3 B)> SliceZ(Mesh mesh, double z);
}

public static class MeshKernel
{
    private static IMeshKernel _current = ManagedMeshKernel.Instance;

    /// <summary>The active kernel (managed by default; Rust when the native engine is enabled).</summary>
    public static IMeshKernel Current
    {
        get => _current;
        set => _current = value ?? ManagedMeshKernel.Instance;
    }
}

/// <summary>Pure C# reference implementation of <see cref="IMeshKernel"/>.</summary>
public sealed class ManagedMeshKernel : IMeshKernel
{
    public static readonly ManagedMeshKernel Instance = new();
    public string Name => "Managed (C#)";

    public MeshProperties ComputeProperties(Mesh mesh)
    {
        var pos = CollectionsMarshal.AsSpan(mesh.Positions);
        var idx = CollectionsMarshal.AsSpan(mesh.Indices);
        double vol = 0, area = 0, cx = 0, cy = 0, cz = 0;
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            var a = pos[idx[i]]; var b = pos[idx[i + 1]]; var c = pos[idx[i + 2]];
            double v = Vec3.Dot(a, Vec3.Cross(b, c)) / 6.0;
            vol += v;
            var s = a + b + c;
            cx += v * s.X; cy += v * s.Y; cz += v * s.Z;
            area += Vec3.Cross(b - a, c - a).Length * 0.5;
        }
        var centroid = Math.Abs(vol) > 1e-300 ? new Vec3(cx, cy, cz) / (4 * vol) : Vec3.Zero;
        return new MeshProperties(vol, area, centroid);
    }

    public void ComputeVertexNormals(Mesh mesh)
    {
        var n = new Vec3[mesh.VertexCount];
        var pos = CollectionsMarshal.AsSpan(mesh.Positions);
        var idx = CollectionsMarshal.AsSpan(mesh.Indices);
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            int ia = idx[i], ib = idx[i + 1], ic = idx[i + 2];
            var fn = Vec3.Cross(pos[ib] - pos[ia], pos[ic] - pos[ia]); // area-weighted
            n[ia] += fn; n[ib] += fn; n[ic] += fn;
        }
        for (int i = 0; i < n.Length; i++) n[i] = n[i].Normalized();
        mesh.Normals = [.. n];
    }

    public Mesh Weld(Mesh mesh, double tolerance)
    {
        double inv = 1.0 / Math.Max(tolerance, 1e-12);
        var map = new Dictionary<(long, long, long), int>(mesh.VertexCount);
        var remap = new int[mesh.VertexCount];
        var r = new Mesh(mesh.VertexCount, mesh.TriangleCount);
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Positions[i];
            var key = ((long)Math.Round(p.X * inv), (long)Math.Round(p.Y * inv), (long)Math.Round(p.Z * inv));
            if (!map.TryGetValue(key, out var ni)) { ni = r.AddVertex(p); map[key] = ni; }
            remap[i] = ni;
        }
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int a = remap[mesh.Indices[i]], b = remap[mesh.Indices[i + 1]], c = remap[mesh.Indices[i + 2]];
            if (a != b && b != c && a != c) r.AddTriangle(a, b, c);
        }
        return r;
    }

    public List<(Vec3 A, Vec3 B)> SliceZ(Mesh mesh, double z)
    {
        var segs = new List<(Vec3, Vec3)>();
        Span<Vec3> pts = stackalloc Vec3[3];
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = mesh.GetTriangle(t);
            int n = 0;
            Edge(a, b, z, pts, ref n);
            Edge(b, c, z, pts, ref n);
            Edge(c, a, z, pts, ref n);
            if (n >= 2 && !pts[0].ApproxEquals(pts[1], 1e-12)) segs.Add((pts[0], pts[1]));
        }
        return segs;

        static void Edge(Vec3 p, Vec3 q, double z, Span<Vec3> o, ref int n)
        {
            // half-open rule avoids double hits on vertices exactly on the plane
            bool pa = p.Z >= z, qa = q.Z >= z;
            if (pa == qa || n >= 3) return;
            // canonical order so neighbouring triangles compute bit-identical points on shared edges
            if (p.Z > q.Z || (p.Z == q.Z && (p.X > q.X || (p.X == q.X && p.Y > q.Y)))) (p, q) = (q, p);
            double t = (z - p.Z) / (q.Z - p.Z);
            o[n++] = Vec3.Lerp(p, q, t);
        }
    }
}

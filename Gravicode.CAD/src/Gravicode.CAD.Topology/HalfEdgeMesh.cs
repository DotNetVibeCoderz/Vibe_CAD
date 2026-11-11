namespace Gravicode.CAD.Topology;

/// <summary>
/// Half-edge connectivity built from an indexed mesh (vertices are welded first).
/// Half-edge h belongs to face h/3; Twin is -1 on boundaries.
/// </summary>
public sealed class HalfEdgeMesh
{
    public Mesh Mesh { get; }
    /// <summary>Origin vertex of each half-edge.</summary>
    public int[] Origin { get; }
    /// <summary>Opposite half-edge, or -1 when on a boundary.</summary>
    public int[] Twin { get; }
    /// <summary>Number of faces sharing each undirected edge (≠ 2 means non-manifold or boundary).</summary>
    public Dictionary<(int, int), int> EdgeUse { get; } = [];

    public int FaceCount => Mesh.TriangleCount;
    public int HalfEdgeCount => Origin.Length;

    public HalfEdgeMesh(Mesh mesh, double weldTolerance = 1e-6)
    {
        Mesh = mesh.Welded(weldTolerance);
        int h = Mesh.Indices.Count;
        Origin = [.. Mesh.Indices];
        Twin = new int[h];
        Array.Fill(Twin, -1);
        var directed = new Dictionary<(int, int), int>(h);
        for (int i = 0; i < h; i++)
        {
            int a = Origin[i], b = Origin[Next(i)];
            directed.TryAdd((a, b), i);
            var key = a < b ? (a, b) : (b, a);
            EdgeUse[key] = EdgeUse.GetValueOrDefault(key) + 1;
        }
        for (int i = 0; i < h; i++)
        {
            int a = Origin[i], b = Origin[Next(i)];
            if (directed.TryGetValue((b, a), out var t)) Twin[i] = t;
        }
    }

    public static int Next(int h) => h - h % 3 + (h + 1) % 3;
    public static int Prev(int h) => h - h % 3 + (h + 2) % 3;
    public static int FaceOf(int h) => h / 3;
    public int Target(int h) => Origin[Next(h)];

    public int EdgeCount => EdgeUse.Count;
    public int BoundaryEdgeCount => EdgeUse.Count(e => e.Value == 1);
    public int NonManifoldEdgeCount => EdgeUse.Count(e => e.Value > 2);

    /// <summary>Closed (watertight) and 2-manifold.</summary>
    public bool IsClosedManifold => EdgeUse.Values.All(v => v == 2);

    /// <summary>Euler characteristic V - E + F.</summary>
    public int EulerCharacteristic => Mesh.VertexCount - EdgeCount + FaceCount;

    /// <summary>Genus of a closed orientable surface per shell: g = (2·shells - χ) / 2.</summary>
    public int Genus => IsClosedManifold ? (2 * Shells().Count - EulerCharacteristic) / 2 : -1;

    /// <summary>True when every interior edge is traversed in opposite directions by its two faces.</summary>
    public bool IsConsistentlyOriented
    {
        get
        {
            for (int i = 0; i < Origin.Length; i++)
            {
                int a = Origin[i], b = Target(i);
                var key = a < b ? (a, b) : (b, a);
                if (EdgeUse[key] == 2 && Twin[i] < 0) return false;
            }
            return true;
        }
    }

    /// <summary>Boundary loops (holes in an open surface) as vertex index chains.</summary>
    public List<List<int>> BoundaryLoops()
    {
        var next = new Dictionary<int, int>();
        for (int i = 0; i < Origin.Length; i++)
            if (Twin[i] < 0) next.TryAdd(Origin[i], Target(i));
        var loops = new List<List<int>>();
        var used = new HashSet<int>();
        foreach (var start in next.Keys)
        {
            if (used.Contains(start)) continue;
            var loop = new List<int>();
            int v = start, guard = 0;
            while (used.Add(v) && guard++ < next.Count + 1)
            {
                loop.Add(v);
                if (!next.TryGetValue(v, out v)) break;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    /// <summary>Connected components (shells) as lists of face indices.</summary>
    public List<List<int>> Shells()
    {
        var seen = new bool[FaceCount];
        var shells = new List<List<int>>();
        var vertFaces = new List<int>[Mesh.VertexCount];
        for (int f = 0; f < FaceCount; f++)
            for (int k = 0; k < 3; k++) (vertFaces[Origin[f * 3 + k]] ??= []).Add(f);
        for (int f = 0; f < FaceCount; f++)
        {
            if (seen[f]) continue;
            var shell = new List<int>();
            var q = new Queue<int>(); q.Enqueue(f); seen[f] = true;
            while (q.Count > 0)
            {
                int c = q.Dequeue(); shell.Add(c);
                for (int k = 0; k < 3; k++)
                    foreach (var nf in vertFaces[Origin[c * 3 + k]])
                        if (!seen[nf]) { seen[nf] = true; q.Enqueue(nf); }
            }
            shells.Add(shell);
        }
        return shells;
    }

    /// <summary>Edges whose dihedral angle exceeds <paramref name="angleDegrees"/> plus all boundary edges.</summary>
    public List<(Vec3 A, Vec3 B)> FeatureEdges(double angleDegrees = 30)
    {
        double cosT = Math.Cos(Units.Deg(angleDegrees));
        var normals = FaceNormals();
        var r = new List<(Vec3, Vec3)>();
        for (int i = 0; i < Origin.Length; i++)
        {
            int t = Twin[i];
            if (t >= 0 && t < i) continue; // count each interior edge once
            Vec3 n0 = normals[FaceOf(i)], n1 = t >= 0 ? normals[FaceOf(t)] : Vec3.Zero;
            // edges touching zero-area slivers (left by boolean / T-junction repair) are never features
            bool feature = t < 0 || (n0 != Vec3.Zero && n1 != Vec3.Zero && Vec3.Dot(n0, n1) < cosT);
            if (feature) r.Add((Mesh.Positions[Origin[i]], Mesh.Positions[Target(i)]));
        }
        return r;
    }

    /// <summary>Unit face normals; <see cref="Vec3.Zero"/> for degenerate (near zero-area) triangles.</summary>
    public Vec3[] FaceNormals()
    {
        double diag = Math.Max(Mesh.Bounds.Diagonal, 1e-12);
        double minArea2 = 1e-18 * diag * diag * diag * diag; // |cross|² threshold
        var r = new Vec3[FaceCount];
        for (int f = 0; f < FaceCount; f++)
        {
            var (a, b, c) = Mesh.GetTriangle(f);
            var n = Vec3.Cross(b - a, c - a);
            r[f] = n.LengthSquared <= minArea2 ? Vec3.Zero : n.Normalized();
        }
        return r;
    }

    /// <summary>Summary of validation checks.</summary>
    public MeshReport Validate() => new(
        Mesh.VertexCount, EdgeCount, FaceCount, BoundaryEdgeCount, NonManifoldEdgeCount,
        IsClosedManifold, IsConsistentlyOriented, Shells().Count, EulerCharacteristic, Genus,
        Mesh.Properties.Volume, Mesh.Properties.Area);
}

public readonly record struct MeshReport(
    int Vertices, int Edges, int Faces, int BoundaryEdges, int NonManifoldEdges,
    bool Watertight, bool ConsistentOrientation, int Shells, int EulerCharacteristic, int Genus,
    double Volume, double Area)
{
    public override string ToString() =>
        $"V={Vertices} E={Edges} F={Faces} | watertight={Watertight} oriented={ConsistentOrientation} shells={Shells} " +
        $"χ={EulerCharacteristic} genus={Genus} | boundary={BoundaryEdges} non-manifold={NonManifoldEdges} | volume={Volume:G6} area={Area:G6}";
}

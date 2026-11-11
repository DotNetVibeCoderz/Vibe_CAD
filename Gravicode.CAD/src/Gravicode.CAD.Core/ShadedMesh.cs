namespace Gravicode.CAD;

/// <summary>
/// GPU-friendly single-precision mesh with crease-aware normals: vertices are shared across
/// smooth edges but split across sharp edges (dihedral angle above the crease angle), so CAD
/// parts get flat faces and smooth cylinders. Consumed by glTF export and all renderers.
/// </summary>
public sealed class ShadedMesh
{
    public float[] Positions { get; init; } = [];
    public float[] Normals { get; init; } = [];
    public uint[] Indices { get; init; } = [];
    public int VertexCount => Positions.Length / 3;

    public static ShadedMesh FromMesh(Mesh mesh, double creaseAngleDegrees = 35)
    {
        int tc = mesh.TriangleCount;
        var faceN = new Vec3[tc];
        for (int t = 0; t < tc; t++) faceN[t] = mesh.FaceNormal(t);
        double cosC = Math.Cos(creaseAngleDegrees * Math.PI / 180);

        // triangles incident to each vertex
        var vt = new List<int>[mesh.VertexCount];
        for (int t = 0; t < tc; t++)
            for (int k = 0; k < 3; k++) (vt[mesh.Indices[t * 3 + k]] ??= []).Add(t);

        var pos = new List<float>(mesh.VertexCount * 3);
        var nrm = new List<float>(mesh.VertexCount * 3);
        var idx = new uint[tc * 3];
        var corner = new int[tc * 3];
        Array.Fill(corner, -1);
        int next = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            var tris = vt[v];
            if (tris is null) continue;
            // group incident triangles into smooth clusters (greedy by normal similarity to cluster seed)
            var assigned = new int[tris.Count];
            Array.Fill(assigned, -1);
            for (int i = 0; i < tris.Count; i++)
            {
                if (assigned[i] >= 0) continue;
                int id = next++;
                Vec3 acc = Vec3.Zero;
                var seed = faceN[tris[i]];
                for (int j = i; j < tris.Count; j++)
                {
                    if (assigned[j] >= 0) continue;
                    if (Vec3.Dot(seed, faceN[tris[j]]) < cosC) continue;
                    assigned[j] = id;
                    var (a, b, c) = mesh.GetTriangle(tris[j]);
                    acc += Vec3.Cross(b - a, c - a); // area weighted
                }
                var p = mesh.Positions[v];
                var n = acc.Normalized();
                if (n == Vec3.Zero) n = seed;
                pos.Add((float)p.X); pos.Add((float)p.Y); pos.Add((float)p.Z);
                nrm.Add((float)n.X); nrm.Add((float)n.Y); nrm.Add((float)n.Z);
            }
            for (int i = 0; i < tris.Count; i++)
            {
                int t = tris[i];
                for (int k = 0; k < 3; k++)
                    if (mesh.Indices[t * 3 + k] == v && corner[t * 3 + k] < 0) { corner[t * 3 + k] = assigned[i]; break; }
            }
        }
        for (int i = 0; i < idx.Length; i++) idx[i] = (uint)Math.Max(corner[i], 0);
        return new ShadedMesh { Positions = [.. pos], Normals = [.. nrm], Indices = idx };
    }
}

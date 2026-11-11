using System.Runtime.InteropServices;

namespace Gravicode.CAD;

/// <summary>
/// Indexed triangle mesh in double precision. This is the common currency between the
/// geometry kernel, the importers/exporters, the renderers and the native (Rust) engine.
/// Triangles are counter-clockwise when seen from outside (outward normals).
/// </summary>
public sealed class Mesh
{
    public List<Vec3> Positions { get; }
    /// <summary>Triangle vertex indices, 3 per triangle.</summary>
    public List<int> Indices { get; }
    /// <summary>Optional per-vertex normals (same count as <see cref="Positions"/>).</summary>
    public List<Vec3>? Normals { get; set; }

    public Mesh() { Positions = []; Indices = []; }
    public Mesh(int vertexCapacity, int triangleCapacity) { Positions = new(vertexCapacity); Indices = new(triangleCapacity * 3); }
    public Mesh(IEnumerable<Vec3> positions, IEnumerable<int> indices) { Positions = [.. positions]; Indices = [.. indices]; }

    public int VertexCount => Positions.Count;
    public int TriangleCount => Indices.Count / 3;
    public bool IsEmpty => Indices.Count == 0;

    public int AddVertex(Vec3 p) { Positions.Add(p); return Positions.Count - 1; }
    public void AddTriangle(int a, int b, int c) { Indices.Add(a); Indices.Add(b); Indices.Add(c); }
    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c)
    {
        int i = Positions.Count;
        Positions.Add(a); Positions.Add(b); Positions.Add(c);
        Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
    }

    /// <summary>Adds a convex/planar polygon as a triangle fan.</summary>
    public void AddFan(ReadOnlySpan<int> loop)
    {
        for (int i = 1; i + 1 < loop.Length; i++) AddTriangle(loop[0], loop[i], loop[i + 1]);
    }

    public (Vec3 A, Vec3 B, Vec3 C) GetTriangle(int t) =>
        (Positions[Indices[t * 3]], Positions[Indices[t * 3 + 1]], Positions[Indices[t * 3 + 2]]);

    public IEnumerable<(Vec3 A, Vec3 B, Vec3 C)> Triangles()
    {
        for (int t = 0; t < TriangleCount; t++) yield return GetTriangle(t);
    }

    public Vec3 FaceNormal(int t)
    {
        var (a, b, c) = GetTriangle(t);
        return Vec3.Cross(b - a, c - a).Normalized();
    }

    public BoundingBox Bounds
    {
        get
        {
            var b = BoundingBox.Empty;
            foreach (var p in CollectionsMarshal.AsSpan(Positions)) b = b.Include(p);
            return b;
        }
    }

    /// <summary>Signed volume (positive for closed outward-oriented meshes).</summary>
    public double Volume => MeshKernel.Current.ComputeProperties(this).Volume;
    public double SurfaceArea => MeshKernel.Current.ComputeProperties(this).Area;
    public MeshProperties Properties => MeshKernel.Current.ComputeProperties(this);

    public Mesh Clone()
    {
        var m = new Mesh(Positions, Indices);
        if (Normals != null) m.Normals = [.. Normals];
        return m;
    }

    /// <summary>Returns a transformed copy (winding is fixed when the transform mirrors).</summary>
    public Mesh Transformed(Mat4 m)
    {
        var r = new Mesh(Positions.Count, TriangleCount);
        foreach (var p in Positions) r.Positions.Add(m.TransformPoint(p));
        bool flip = m.Determinant3 < 0;
        for (int i = 0; i < Indices.Count; i += 3)
        {
            if (flip) r.AddTriangle(Indices[i], Indices[i + 2], Indices[i + 1]);
            else r.AddTriangle(Indices[i], Indices[i + 1], Indices[i + 2]);
        }
        if (Normals != null) r.Normals = [.. Normals.Select(n => flip ? -m.TransformNormal(n) : m.TransformNormal(n))];
        return r;
    }

    public Mesh Translated(double x, double y, double z) => Transformed(Mat4.Translate(x, y, z));
    public Mesh Translated(Vec3 t) => Transformed(Mat4.Translation3(t));
    public Mesh Rotated(Vec3 axis, double degrees) => Transformed(Mat4.RotateAxis(axis, Units.Deg(degrees)));
    public Mesh Scaled(double s) => Transformed(Mat4.Scale(s));

    /// <summary>Flips all triangles (inverts the solid).</summary>
    public Mesh Flipped()
    {
        var r = new Mesh(Positions.Count, TriangleCount);
        r.Positions.AddRange(Positions);
        for (int i = 0; i < Indices.Count; i += 3) r.AddTriangle(Indices[i], Indices[i + 2], Indices[i + 1]);
        return r;
    }

    /// <summary>Appends another mesh (no welding).</summary>
    public void Append(Mesh other)
    {
        int offset = Positions.Count;
        Positions.AddRange(other.Positions);
        foreach (var i in other.Indices) Indices.Add(i + offset);
        if (Normals != null && other.Normals != null) Normals.AddRange(other.Normals); else Normals = null;
    }

    public static Mesh Merge(IEnumerable<Mesh> meshes)
    {
        var r = new Mesh();
        foreach (var m in meshes) r.Append(m);
        return r;
    }

    /// <summary>Computes smooth per-vertex normals (area-weighted).</summary>
    public Mesh WithVertexNormals() { MeshKernel.Current.ComputeVertexNormals(this); return this; }

    /// <summary>Merges vertices closer than <paramref name="tolerance"/>, removing degenerate triangles.</summary>
    public Mesh Welded(double tolerance = 1e-6) => MeshKernel.Current.Weld(this, tolerance);

    /// <summary>Unshares vertices so each triangle has its own (flat shading).</summary>
    public Mesh Unwelded()
    {
        var r = new Mesh(Indices.Count, TriangleCount);
        for (int t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = GetTriangle(t);
            r.AddTriangle(a, b, c);
        }
        return r;
    }

    /// <summary>Intersects the mesh with the plane z = <paramref name="z"/> returning line segments.</summary>
    public List<(Vec3 A, Vec3 B)> SliceZ(double z) => MeshKernel.Current.SliceZ(this, z);

    public override string ToString() => $"Mesh({VertexCount} vertices, {TriangleCount} triangles)";
}

/// <summary>Mass properties of a closed mesh.</summary>
public readonly record struct MeshProperties(double Volume, double Area, Vec3 Centroid);

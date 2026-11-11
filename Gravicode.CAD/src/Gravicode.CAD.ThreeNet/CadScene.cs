using System.Numerics;
using Gravicode.CAD.Rendering;
using Gravicode.CAD.Topology;
using ThreeNet;
using ThreeNet.Interop;

namespace Gravicode.CAD.ThreeNet;

/// <summary>
/// Builds and maintains ThreeNet scene content for a <see cref="CadModel"/>.
/// CAD is Z-up; ThreeNet (like three.js) is Y-up, so models are rotated -90° about X under a root node.
/// </summary>
public sealed class CadScene : IDisposable
{
    public Scene Scene { get; }
    public Node Root { get; }
    public Node Camera { get; private set; }
    public BoundingBox Bounds { get; private set; } = BoundingBox.Empty;
    private readonly List<Node> _modelNodes = [];
    private readonly List<global::ThreeNet.Geometry> _geometries = [];
    private readonly List<global::ThreeNet.Material> _materials = [];
    private Node? _grid;

    public bool ShowEdges { get; set; } = true;
    public double EdgeAngle { get; set; } = 30;

    private readonly bool _light;

    /// <param name="scene">Existing scene (e.g. from ThreeNetView) or null to create one.</param>
    /// <param name="light">Light paper-like background (true) or dark studio background (false).</param>
    public CadScene(Scene? scene = null, bool light = false)
    {
        _light = light;
        Scene = scene ?? new Scene();
        Root = Scene.CreateNode(null!, "GravicodeCAD");
        Root.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2); // Z-up -> Y-up
        var env = Scene.Environment;
        env.Background = light ? new Vector4(0.86f, 0.89f, 0.92f, 1) : new Vector4(0.07f, 0.09f, 0.13f, 1);
        Scene.Environment = env;
        Scene.AddLight(Light.Hemisphere(new Vector3(0.75f, 0.82f, 1f), new Vector3(0.22f, 0.2f, 0.18f), 0.9f));
        var key = Scene.AddLight(Light.Directional(new Vector3(1f, 0.97f, 0.92f), 3.2f));
        key.Position = new Vector3(4, 8, 6); key.LookAt(Vector3.Zero);
        var fill = Scene.AddLight(Light.Directional(new Vector3(0.7f, 0.8f, 1f), 1.1f));
        fill.Position = new Vector3(-6, 3, -4); fill.LookAt(Vector3.Zero);
        Camera = Scene.AddCamera(global::ThreeNet.Camera.Perspective(35f * MathF.PI / 180f), new Vector3(0, 2, 6));
        Camera.LookAt(Vector3.Zero);
    }

    /// <summary>Replaces the displayed model.</summary>
    public void SetModel(CadModel model)
    {
        Clear();
        foreach (var body in model.Bodies.Where(b => b.Visible && !b.Mesh.IsEmpty))
        {
            var sm = ShadedMesh.FromMesh(body.Mesh, 35);
            var verts = new Vertex[sm.VertexCount];
            for (int i = 0; i < verts.Length; i++)
                verts[i] = new Vertex
                {
                    Position = new Vector3(sm.Positions[i * 3], sm.Positions[i * 3 + 1], sm.Positions[i * 3 + 2]),
                    Normal = new Vector3(sm.Normals[i * 3], sm.Normals[i * 3 + 1], sm.Normals[i * 3 + 2]),
                };
            var geo = Scene.CreateGeometry(verts, sm.Indices, PrimitiveTopology.TriangleList);
            var c = body.Color;
            var mat = Scene.CreateMaterial(MaterialOptions.Pbr(new Vector4((float)c.R, (float)c.G, (float)c.B, (float)c.A), (float)body.Metallic, (float)body.Roughness));
            _geometries.Add(geo); _materials.Add(mat);
            var node = Scene.AddMesh(geo, mat, Root, body.Name);
            node.CastShadow = true; node.ReceiveShadow = true;
            _modelNodes.Add(node);

            if (ShowEdges)
            {
                var edges = new HalfEdgeMesh(body.Mesh).FeatureEdges(EdgeAngle);
                if (edges.Count > 0)
                {
                    var ev = new Vertex[edges.Count * 2];
                    var ei = new uint[edges.Count * 2];
                    for (int i = 0; i < edges.Count; i++)
                    {
                        ev[i * 2] = new Vertex { Position = edges[i].A.ToVector3(), Normal = Vector3.UnitZ };
                        ev[i * 2 + 1] = new Vertex { Position = edges[i].B.ToVector3(), Normal = Vector3.UnitZ };
                        ei[i * 2] = (uint)(i * 2); ei[i * 2 + 1] = (uint)(i * 2 + 1);
                    }
                    var eg = Scene.CreateGeometry(ev, ei, PrimitiveTopology.LineList);
                    var em = Scene.CreateMaterial(MaterialOptions.Basic(new Vector4(0.05f, 0.06f, 0.08f, 1)));
                    _geometries.Add(eg); _materials.Add(em);
                    _modelNodes.Add(Scene.AddMesh(eg, em, Root, body.Name + "_edges"));
                }
            }
        }
        Bounds = model.Bounds;
        AddGrid();
    }

    private void AddGrid()
    {
        if (Bounds.IsEmpty) return;
        double size = Math.Max(Bounds.Size.X, Bounds.Size.Y) * 1.8;
        var g = Scene.CreateGridGeometry((float)size, 18);
        var m = Scene.CreateMaterial(MaterialOptions.Basic(_light ? new Vector4(0.62f, 0.68f, 0.77f, 1) : new Vector4(0.22f, 0.27f, 0.36f, 1)));
        _geometries.Add(g); _materials.Add(m);
        // grid geometry lies in ThreeNet's XZ plane (Y-up) — place it at the model's floor
        _grid = Scene.AddMesh(g, m, null!, "grid");
        var c = Bounds.Center;
        _grid.Position = new Vector3((float)c.X, (float)Bounds.Min.Z, (float)-c.Y);
    }

    /// <summary>Converts a CAD (Z-up) point to ThreeNet world (Y-up).</summary>
    public static Vector3 ToWorld(Vec3 p) => new((float)p.X, (float)p.Z, (float)-p.Y);

    /// <summary>World-space centre and radius of the model (for orbit controllers).</summary>
    public (Vector3 Center, float Radius) Frame()
    {
        if (Bounds.IsEmpty) return (Vector3.Zero, 1);
        return (ToWorld(Bounds.Center), (float)Math.Max(Bounds.Diagonal / 2, 1e-3));
    }

    /// <summary>Places the camera like the software renderer's orbit camera.</summary>
    public void ApplyCamera(OrbitCamera cam)
    {
        Camera.Position = ToWorld(cam.Position);
        Camera.LookAt(ToWorld(cam.Target));
    }

    public void Clear()
    {
        foreach (var n in _modelNodes) n.Remove();
        _modelNodes.Clear();
        _grid?.Remove(); _grid = null;
        foreach (var g in _geometries) g.Destroy();
        foreach (var m in _materials) m.Destroy();
        _geometries.Clear(); _materials.Clear();
    }

    public void Dispose() => Clear();
}

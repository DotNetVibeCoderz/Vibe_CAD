using Gravicode.CAD.Topology;

namespace Gravicode.CAD.Studio;

/// <summary>Drawing layer (AutoCAD-style): colour, visibility and lock state.</summary>
public sealed record Layer(string Name, string Color = "#E6EDF3", bool Visible = true, bool Locked = false);

/// <summary>A 2D drafting object. Geometry objects are treated as immutable: edits replace the entity.</summary>
public sealed record StudioEntity(Guid Id, string Layer, DrawingEntity Geometry, string? Color = null)
{
    public static StudioEntity Create(string layer, DrawingEntity g, string? color = null) => new(Guid.NewGuid(), layer, g, color);
}

/// <summary>A 3D solid body. Meshes are immutable values: edits create new meshes.</summary>
public sealed record StudioSolid(Guid Id, string Name, string Layer, Mesh Mesh, string Color = "#9AA7B4", double Metallic = 0.4, double Roughness = 0.4)
{
    public static StudioSolid Create(string name, string layer, Mesh mesh, string color = "#9AA7B4", double metallic = 0.4, double roughness = 0.4) =>
        new(Guid.NewGuid(), name, layer, mesh, color, metallic, roughness);

    private List<(Vec2 A, Vec2 B)>? _plan;
    /// <summary>Top-view outline (feature edges projected to XY), cached for drafting display and picking.</summary>
    public IReadOnlyList<(Vec2 A, Vec2 B)> PlanOutline
    {
        get
        {
            if (_plan != null) return _plan;
            var he = new HalfEdgeMesh(Mesh);
            var normals = he.FaceNormals();
            var list = new List<(Vec2, Vec2)>();
            for (int h = 0; h < he.HalfEdgeCount; h++)
            {
                int tw = he.Twin[h];
                if (tw >= 0 && tw < h) continue;
                var n0 = normals[HalfEdgeMesh.FaceOf(h)];
                bool edge = tw < 0;
                if (!edge)
                {
                    var n1 = normals[HalfEdgeMesh.FaceOf(tw)];
                    if (n0 == Vec3.Zero || n1 == Vec3.Zero) continue;
                    edge = (n0.Z > 1e-6) != (n1.Z > 1e-6) || Vec3.Dot(n0, n1) < Math.Cos(Units.Deg(35));
                }
                if (!edge) continue;
                var a = he.Mesh.Positions[he.Origin[h]]; var b = he.Mesh.Positions[he.Target(h)];
                // vertical edges collapse to points in plan: skip
                if ((a.XY - b.XY).LengthSquared < 1e-12) continue;
                list.Add((a.XY, b.XY));
            }
            return _plan = list;
        }
    }

    public CadBody ToBody() => new(Name, Mesh) { Color = CadColor.FromHex(Color), Metallic = Metallic, Roughness = Roughness };
}

/// <summary>Immutable snapshot used by undo/redo (lists are copied, objects are shared).</summary>
public sealed record DocState(IReadOnlyList<Layer> Layers, string CurrentLayer, IReadOnlyList<StudioEntity> Entities, IReadOnlyList<StudioSolid> Solids,
    IReadOnlyDictionary<string, BlockDefinition> Blocks);

/// <summary>
/// A Gravicode CAD Studio drawing: layers, 2D entities (model space) and 3D solids, with undo/redo.
/// All edits go through <see cref="Edit"/> so they are undoable and raise <see cref="Changed"/>.
/// </summary>
public sealed class StudioDocument
{
    public string Name { get; set; } = "Drawing1";
    public LengthUnit Units { get; set; } = LengthUnit.Millimeter;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public List<Layer> Layers { get; private set; } = [new("0"), new("Dimensions", "#7FD1FF"), new("Solids", "#9AA7B4")];
    public string CurrentLayer { get; set; } = "0";
    public List<StudioEntity> Entities { get; private set; } = [];
    public List<StudioSolid> Solids { get; private set; } = [];
    public Dictionary<string, string> Properties { get; } = [];
    /// <summary>Block definitions by name (case-insensitive). Definitions are immutable; redefining replaces them.</summary>
    public Dictionary<string, BlockDefinition> Blocks { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly Stack<(string Label, DocState State)> _undo = new();
    private readonly Stack<(string Label, DocState State)> _redo = new();

    /// <summary>Incremented on every change; views use it to know when to redraw.</summary>
    public int Version { get; private set; }
    public bool IsDirty { get; set; }
    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.Count > 0 ? _undo.Peek().Label : null;

    public DocState Snapshot() => new([.. Layers], CurrentLayer, [.. Entities], [.. Solids], new Dictionary<string, BlockDefinition>(Blocks, StringComparer.OrdinalIgnoreCase));

    private void Restore(DocState s)
    {
        Layers = [.. s.Layers]; CurrentLayer = s.CurrentLayer; Entities = [.. s.Entities]; Solids = [.. s.Solids];
        Blocks = new Dictionary<string, BlockDefinition>(s.Blocks, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Applies an undoable edit.</summary>
    public void Edit(string label, Action<StudioDocument> change)
    {
        _undo.Push((label, Snapshot()));
        if (_undo.Count > 200) TrimUndo();
        _redo.Clear();
        change(this);
        Touch();
    }

    private void TrimUndo()
    {
        var keep = _undo.Take(150).Reverse().ToList();
        _undo.Clear();
        foreach (var k in keep) _undo.Push(k);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var (label, state) = _undo.Pop();
        _redo.Push((label, Snapshot()));
        Restore(state);
        Touch();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var (label, state) = _redo.Pop();
        _undo.Push((label, Snapshot()));
        Restore(state);
        Touch();
        return true;
    }

    public void Touch() { Version++; IsDirty = true; Changed?.Invoke(); }
    public void ClearHistory() { _undo.Clear(); _redo.Clear(); }

    public Layer? FindLayer(string name) => Layers.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public Layer LayerOf(string name) => FindLayer(name) ?? Layers[0];

    public void EnsureLayer(string name, string color = "#E6EDF3")
    {
        if (FindLayer(name) is null) Layers.Add(new Layer(name, color));
    }

    // ---------- convenience builders (used by templates, AI and commands; not undoable on their own) ----------
    public StudioEntity Add(DrawingEntity g, string? layer = null, string? color = null)
    {
        var e = StudioEntity.Create(layer ?? CurrentLayer, g, color);
        EnsureLayer(e.Layer);
        Entities.Add(e);
        return e;
    }

    public StudioSolid AddSolid(string name, Mesh mesh, string color = "#9AA7B4", string? layer = null, double metallic = 0.4, double roughness = 0.4)
    {
        var s = StudioSolid.Create(name, layer ?? "Solids", mesh, color, metallic, roughness);
        EnsureLayer(s.Layer);
        Solids.Add(s);
        return s;
    }

    /// <summary>Changes whenever a visible solid's mesh, colour or material changes — 3D views rebuild only then
    /// (drawing edits and selection changes leave it alone).</summary>
    public int SolidsKey()
    {
        var h = new HashCode();
        foreach (var s in VisibleSolids) { h.Add(s.Id); h.Add(s.Mesh); h.Add(s.Color); h.Add(s.Metallic); h.Add(s.Roughness); }
        return h.ToHashCode();
    }

    /// <summary>Solids in the order 3D viewers receive them (visible, non-empty) — index i is body i in the viewer.</summary>
    public IReadOnlyList<StudioSolid> ViewerSolids() => [.. VisibleSolids.Where(s => !s.Mesh.IsEmpty)];

    public IEnumerable<StudioEntity> VisibleEntities => Entities.Where(e => LayerOf(e.Layer).Visible);
    public IEnumerable<StudioSolid> VisibleSolids => Solids.Where(s => LayerOf(s.Layer).Visible);

    public BoundingBox Bounds
    {
        get
        {
            var bb = BoundingBox.Empty;
            foreach (var e in VisibleEntities) { var (a, b) = e.Geometry.Extents(); bb = bb.Include(a.ToVec3()).Include(b.ToVec3()); }
            foreach (var s in VisibleSolids) bb = bb.Union(s.Mesh.Bounds);
            return bb;
        }
    }

    /// <summary>Library model (bodies + drawing) for export, rendering and viewers.</summary>
    public CadModel ToCadModel(bool includeDrawing = true)
    {
        var m = new CadModel(Name) { Units = Units };
        foreach (var s in ViewerSolids()) m.Bodies.Add(s.ToBody());
        if (includeDrawing)
            foreach (var e in VisibleEntities)
                foreach (var (prim, layer, color) in Geometry2D.Primitives(e.Geometry))
                {
                    // studio-only objects (dimensions, hatches, blocks) are exploded so every exporter understands them
                    var g = prim.Transformed(p => p);
                    g.Layer = layer ?? e.Layer;
                    g.Color = CadColor.FromHex(color ?? e.Color ?? LayerOf(g.Layer).Color);
                    m.Drawing.Add(g);
                }
        if (Description != null) m.Metadata["description"] = Description;
        return m;
    }

    /// <summary>Imports bodies and drawing entities from a library model into this document.</summary>
    public void Import(CadModel model, string? layerPrefix = null)
    {
        foreach (var b in model.Bodies)
            AddSolid(b.Name, b.Mesh, b.Color.ToHex(), layerPrefix != null ? $"{layerPrefix}-Solids" : "Solids", b.Metallic, b.Roughness);
        foreach (var d in model.Drawing)
        {
            var layer = layerPrefix != null ? $"{layerPrefix}-{d.Layer}" : d.Layer;
            EnsureLayer(layer, d.Color?.ToHex() ?? "#E6EDF3");
            Entities.Add(StudioEntity.Create(layer, d, d.Color?.ToHex()));
        }
        foreach (var w in model.Wires)
        {
            EnsureLayer("Wires", "#FF9F43");
            Entities.Add(StudioEntity.Create("Wires", new PolylineEntity(w.Points.Select(p => p.XY), w.Closed), w.Color?.ToHex()));
        }
    }
}

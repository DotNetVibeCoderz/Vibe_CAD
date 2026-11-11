namespace Gravicode.CAD;

/// <summary>A named solid/surface body with appearance.</summary>
public sealed class CadBody(string name, Mesh mesh)
{
    public string Name { get; set; } = name;
    public Mesh Mesh { get; set; } = mesh;
    public CadColor Color { get; set; } = CadColor.Steel;
    public double Metallic { get; set; } = 0.6;
    public double Roughness { get; set; } = 0.35;
    public bool Visible { get; set; } = true;
    public Dictionary<string, string> Properties { get; } = [];

    public CadBody WithColor(CadColor c) { Color = c; return this; }
}

/// <summary>
/// The in-memory CAD document: 3D bodies, 2D drawing entities and 3D wires.
/// Load/Save dispatch to the formats registered in <see cref="CadFormats"/>
/// (Gravicode.CAD.IO registers STEP, IGES, STL, OBJ, glTF, DXF, SVG, 3MF and PDF).
/// </summary>
public sealed class CadModel
{
    public string Name { get; set; } = "Untitled";
    public LengthUnit Units { get; set; } = LengthUnit.Millimeter;
    public List<CadBody> Bodies { get; } = [];
    public List<DrawingEntity> Drawing { get; } = [];
    public List<Polyline3> Wires { get; } = [];
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Non-fatal issues collected while importing.</summary>
    public List<string> Warnings { get; } = [];

    public CadModel() { }
    public CadModel(string name) { Name = name; }
    public CadModel(string name, Mesh mesh, CadColor? color = null) : this(name)
    {
        Bodies.Add(new CadBody(name, mesh) { Color = color ?? CadColor.Steel });
    }

    public CadBody AddBody(string name, Mesh mesh, CadColor? color = null)
    {
        var b = new CadBody(name, mesh) { Color = color ?? CadColor.Steel };
        Bodies.Add(b);
        return b;
    }

    public BoundingBox Bounds
    {
        get
        {
            var b = BoundingBox.Empty;
            foreach (var body in Bodies) b = b.Union(body.Mesh.Bounds);
            foreach (var w in Wires) foreach (var p in w.Points) b = b.Include(p);
            foreach (var e in Drawing) { var (mn, mx) = e.Extents(); b = b.Include(mn.ToVec3()).Include(mx.ToVec3()); }
            return b;
        }
    }

    public int TriangleCount => Bodies.Sum(b => b.Mesh.TriangleCount);

    /// <summary>All bodies merged into one mesh (world space).</summary>
    public Mesh ToMesh() => Mesh.Merge(Bodies.Where(b => b.Visible).Select(b => b.Mesh));

    public CadModel Transform(Mat4 m)
    {
        foreach (var b in Bodies) b.Mesh = b.Mesh.Transformed(m);
        foreach (var w in Wires) for (int i = 0; i < w.Points.Count; i++) w.Points[i] = m.TransformPoint(w.Points[i]);
        return this;
    }

    /// <summary>Scales the model to another unit system.</summary>
    public CadModel ConvertUnits(LengthUnit target)
    {
        if (target == Units) return this;
        var f = CAD.Units.Factor(Units, target);
        Transform(Mat4.Scale(f));
        for (int i = 0; i < Drawing.Count; i++) Drawing[i] = Drawing[i].Transformed(p => p * f);
        Units = target;
        return this;
    }

    /// <summary>Loads any supported file, choosing the importer by extension.</summary>
    public static CadModel Load(string path, ImportOptions? options = null)
    {
        var importer = CadFormats.FindImporter(path) ?? throw new NotSupportedException(CadFormats.UnsupportedMessage(path, import: true));
        using var fs = File.OpenRead(path);
        var opts = (options ?? new ImportOptions()) with { SourcePath = path };
        var model = importer.Import(fs, opts);
        if (model.Name == "Untitled") model.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        return model;
    }

    public static CadModel Load(Stream stream, string formatOrExtension, ImportOptions? options = null)
    {
        var importer = CadFormats.FindImporter(formatOrExtension) ?? throw new NotSupportedException(CadFormats.UnsupportedMessage(formatOrExtension, import: true));
        return importer.Import(stream, options ?? new ImportOptions());
    }

    /// <summary>Saves to any supported format, choosing the exporter by extension.</summary>
    public void Save(string path, ExportOptions? options = null)
    {
        var exporter = CadFormats.FindExporter(path) ?? throw new NotSupportedException(CadFormats.UnsupportedMessage(path, import: false));
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        exporter.Export(this, fs, (options ?? new ExportOptions()) with { TargetExtension = Ext(path) });
    }

    public void Save(Stream stream, string formatOrExtension, ExportOptions? options = null)
    {
        var exporter = CadFormats.FindExporter(formatOrExtension) ?? throw new NotSupportedException(CadFormats.UnsupportedMessage(formatOrExtension, import: false));
        exporter.Export(this, stream, (options ?? new ExportOptions()) with { TargetExtension = Ext(formatOrExtension) });
    }

    private static string Ext(string pathOrExt)
    {
        var e = System.IO.Path.GetExtension(pathOrExt);
        return (string.IsNullOrEmpty(e) ? pathOrExt : e).TrimStart('.').ToLowerInvariant();
    }

    public byte[] SaveToBytes(string formatOrExtension, ExportOptions? options = null)
    {
        using var ms = new MemoryStream();
        Save(ms, formatOrExtension, options);
        return ms.ToArray();
    }

    public override string ToString() => $"CadModel '{Name}': {Bodies.Count} bodies, {TriangleCount} triangles, {Drawing.Count} drawing entities, {Wires.Count} wires";
}

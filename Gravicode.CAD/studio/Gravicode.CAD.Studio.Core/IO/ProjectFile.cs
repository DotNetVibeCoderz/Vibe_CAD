using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gravicode.CAD.IO;

namespace Gravicode.CAD.Studio.IO;

/// <summary>
/// Gravicode CAD Studio project file (<c>.gcproj</c>): gzip-compressed JSON holding layers, 2D entities and
/// solids (meshes as base64 double/int arrays, lossless).
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".gcproj";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private sealed record Dto(int Version, string App, string Name, string Units, string? Description, string? Category,
        List<Layer> Layers, string CurrentLayer, List<EntityDto> Entities, List<SolidDto> Solids, Dictionary<string, string> Properties,
        List<BlockDto>? Blocks = null, List<PlaneDto>? Planes = null, List<LayoutDto>? Layouts = null, string? ActiveSpace = null);

    private sealed record PlaneDto(string Key, string Name, double[] Frame, Guid? Solid);
    /// <summary>Viewports as [minX, minY, maxX, maxY, centreX, centreY, scale].</summary>
    private sealed record LayoutDto(string Key, string Name, string Paper, bool Landscape, bool TitleBlock, List<double[]> Viewports);

    /// <summary>Block definition: its objects stored like model-space entities (relative to the base point).</summary>
    private sealed record BlockDto(string Name, List<EntityDto> Items, string? Description = null);

    private sealed record EntityDto(Guid Id, string Layer, string? Color, string Kind, double[] V, string? Text = null, bool Closed = false, double[]? B = null, string? Space = null);
    private sealed record SolidDto(Guid Id, string Name, string Layer, string Color, double Metallic, double Roughness, string Positions, string Indices);

    public static byte[] Save(StudioDocument doc)
    {
        var dto = new Dto(FormatVersion, $"{GravicodeCad.ProductName} Studio {GravicodeCad.Version}", doc.Name, doc.Units.ToString(), doc.Description, doc.Category,
            doc.Layers, doc.CurrentLayer,
            [.. doc.Entities.Select(e => ToDto(e) is { } dto ? dto with { Space = e.Space } : null).OfType<EntityDto>()],
            [.. doc.Solids.Select(s => new SolidDto(s.Id, s.Name, s.Layer, s.Color, s.Metallic, s.Roughness,
                Convert.ToBase64String(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(s.Mesh.Positions))),
                Convert.ToBase64String(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(s.Mesh.Indices)))))],
            doc.Properties,
            doc.Blocks.Count == 0 ? null : [.. doc.Blocks.Values.Select(b => new BlockDto(b.Name,
                [.. b.Items.Select(i => ToDto(new StudioEntity(Guid.Empty, i.Layer, i.Geometry, i.Color))).OfType<EntityDto>()], b.Description))],
            doc.Planes.Count == 0 ? null : [.. doc.Planes.Values.Select(p => new PlaneDto(p.Key, p.Name,
                [p.Origin.X, p.Origin.Y, p.Origin.Z, p.XAxis.X, p.XAxis.Y, p.XAxis.Z, p.YAxis.X, p.YAxis.Y, p.YAxis.Z], p.SolidId))],
            doc.Layouts.Count == 0 ? null : [.. doc.Layouts.Values.Select(l => new LayoutDto(l.Key, l.Name, l.Paper, l.Landscape, l.TitleBlock,
                [.. l.Viewports.Select(v => new[] { v.Min.X, v.Min.Y, v.Max.X, v.Max.Y, v.ModelCenter.X, v.ModelCenter.Y, v.Scale })]))],
            doc.ActiveSpace);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            JsonSerializer.Serialize(gz, dto, Json);
        return ms.ToArray();
    }

    public static void Save(StudioDocument doc, string path)
    {
        File.WriteAllBytes(path, Save(doc));
        doc.IsDirty = false;
    }

    public static StudioDocument Load(byte[] data)
    {
        using var ms = new MemoryStream(data);
        Stream s = data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B ? new GZipStream(ms, CompressionMode.Decompress) : ms;
        var dto = JsonSerializer.Deserialize<Dto>(s, Json) ?? throw new InvalidDataException("Empty project file.");
        var doc = new StudioDocument
        {
            Name = dto.Name,
            Units = Enum.TryParse<LengthUnit>(dto.Units, out var u) ? u : LengthUnit.Millimeter,
            Description = dto.Description,
            Category = dto.Category,
            CurrentLayer = dto.CurrentLayer
        };
        doc.Layers.Clear();
        doc.Layers.AddRange(dto.Layers);
        // blocks first: references look their definition up by name (a block may contain earlier blocks)
        foreach (var b in dto.Blocks ?? [])
            doc.Blocks[b.Name] = new BlockDefinition(b.Name,
                b.Items.Select(i => FromDto(i, doc.Blocks) is { } g ? new BlockItem(g, i.Layer, i.Color) : null).OfType<BlockItem>(), b.Description);
        foreach (var e in dto.Entities) if (FromDto(e, doc.Blocks) is { } g) doc.Entities.Add(new StudioEntity(e.Id, e.Layer, g, e.Color, e.Space));
        foreach (var p in dto.Planes ?? [])
        {
            var f = p.Frame;
            doc.Planes[p.Key] = new WorkPlane(p.Key, p.Name, new Vec3(f[0], f[1], f[2]), new Vec3(f[3], f[4], f[5]), new Vec3(f[6], f[7], f[8]), p.Solid);
        }
        foreach (var l in dto.Layouts ?? [])
            doc.Layouts[l.Key] = new Layout(l.Key, l.Name, l.Paper, l.Landscape, l.TitleBlock,
                [.. l.Viewports.Select(v => new LayoutViewport(new Vec2(v[0], v[1]), new Vec2(v[2], v[3]), new Vec2(v[4], v[5]), v[6]))]);
        if (dto.ActiveSpace is { } space && (doc.Planes.ContainsKey(space) || doc.Layouts.ContainsKey(space))) doc.ActiveSpace = space;
        foreach (var sd in dto.Solids)
        {
            var pos = MemoryMarshal.Cast<byte, Vec3>(Convert.FromBase64String(sd.Positions)).ToArray();
            var idx = MemoryMarshal.Cast<byte, int>(Convert.FromBase64String(sd.Indices)).ToArray();
            doc.Solids.Add(new StudioSolid(sd.Id, sd.Name, sd.Layer, new Mesh(pos, idx), sd.Color, sd.Metallic, sd.Roughness));
        }
        foreach (var (k, v) in dto.Properties) doc.Properties[k] = v;
        doc.IsDirty = false;
        return doc;
    }

    public static StudioDocument Load(string path)
    {
        var d = Load(File.ReadAllBytes(path));
        if (string.IsNullOrWhiteSpace(d.Name)) d.Name = Path.GetFileNameWithoutExtension(path);
        return d;
    }

    private static EntityDto? ToDto(StudioEntity e) => e.Geometry switch
    {
        LineEntity l => new(e.Id, e.Layer, e.Color, "line", [l.A.X, l.A.Y, l.B.X, l.B.Y]),
        CircleEntity c => new(e.Id, e.Layer, e.Color, "circle", [c.Center.X, c.Center.Y, c.Radius]),
        ArcEntity a => new(e.Id, e.Layer, e.Color, "arc", [a.Center.X, a.Center.Y, a.Radius, a.StartAngle, a.EndAngle]),
        PolylineEntity p => new(e.Id, e.Layer, e.Color, "polyline", [.. p.Points.SelectMany(q => new[] { q.X, q.Y })], Closed: p.Closed, B: p.HasArcs ? [.. p.Bulges] : null),
        TextEntity t => new(e.Id, e.Layer, e.Color, "text", [t.Position.X, t.Position.Y, t.Height], t.Text),
        PointEntity pt => new(e.Id, e.Layer, e.Color, "point", [pt.Position.X, pt.Position.Y]),
        DimensionEntity d => new(e.Id, e.Layer, e.Color, "dim", [d.A.X, d.A.Y, d.B.X, d.B.Y, d.Offset, d.TextHeight]),
        // hatch: [scale, angle, loopCount, n0, x, y, …, n1, x, y, …]
        HatchEntity h => new(e.Id, e.Layer, e.Color, "hatch",
            [h.Scale, h.Angle, h.Loops.Count, .. h.Loops.SelectMany(l => l.SelectMany(q => new[] { q.X, q.Y }).Prepend(l.Count))], h.Pattern),
        BlockRefEntity b => new(e.Id, e.Layer, e.Color, "insert", [b.Position.X, b.Position.Y, b.ScaleX, b.ScaleY, b.Rotation], b.Block.Name),
        _ => null
    };

    private static DrawingEntity? FromDto(EntityDto e, IReadOnlyDictionary<string, BlockDefinition> blocks)
    {
        var v = e.V;
        return e.Kind switch
        {
            "line" => new LineEntity(new(v[0], v[1]), new(v[2], v[3])),
            "circle" => new CircleEntity(new(v[0], v[1]), v[2]),
            "arc" => new ArcEntity(new(v[0], v[1]), v[2], v[3], v[4]),
            "polyline" => new PolylineEntity(Enumerable.Range(0, v.Length / 2).Select(i => new Vec2(v[2 * i], v[2 * i + 1])), e.Closed, e.B),
            "text" => new TextEntity(new(v[0], v[1]), e.Text ?? "", v[2]),
            "point" => new PointEntity(new(v[0], v[1])),
            "dim" => new DimensionEntity(new(v[0], v[1]), new(v[2], v[3]), v[4], v[5]),
            "hatch" => ReadHatch(v, e.Text),
            "insert" => e.Text is { } bn && blocks.TryGetValue(bn, out var def) ? new BlockRefEntity(def, new(v[0], v[1]), v[2], v[3], v[4]) : null,
            _ => null
        };
    }

    private static HatchEntity ReadHatch(double[] v, string? pattern)
    {
        var loops = new List<IReadOnlyList<Vec2>>();
        int k = 3;
        for (int l = 0; l < (int)v[2] && k < v.Length; l++)
        {
            int n = (int)v[k++];
            var pts = new Vec2[n];
            for (int i = 0; i < n; i++, k += 2) pts[i] = new Vec2(v[k], v[k + 1]);
            loops.Add(pts);
        }
        return new HatchEntity(loops, pattern ?? "ANSI31", v[0], v[1]);
    }

    // ---------------- interchange ----------------

    /// <summary>Formats the studio can open or insert (in addition to .gcproj).</summary>
    public static IReadOnlyList<string> ImportExtensions { get { CadIO.RegisterAll(); return [.. CadFormats.Importers.SelectMany(i => i.Extensions)]; } }
    public static IReadOnlyList<string> ExportExtensions { get { CadIO.RegisterAll(); return [.. CadFormats.Exporters.SelectMany(e => e.Extensions), "png"]; } }

    /// <summary>Opens any file: .gcproj natively, everything else through the Gravicode.CAD importers.</summary>
    public static StudioDocument Open(string fileName, byte[] data)
    {
        if (fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) return Load(data);
        CadIO.RegisterAll();
        var model = CadModel.Load(new MemoryStream(data), Path.GetExtension(fileName));
        var doc = new StudioDocument { Name = Path.GetFileNameWithoutExtension(fileName), Units = model.Units };
        doc.Import(model);
        foreach (var w in model.Warnings) doc.Properties[$"warning.{doc.Properties.Count}"] = w;
        doc.ClearHistory();
        doc.IsDirty = false;
        return doc;
    }

    /// <summary>Exports to any library format (STEP, STL, GLB, 3MF, OBJ, DXF, SVG, PDF, IGES) or a PNG render.</summary>
    public static byte[] Export(StudioDocument doc, string extension)
    {
        CadIO.RegisterAll();
        extension = extension.TrimStart('.').ToLowerInvariant();
        if (extension == "gcproj") return Save(doc);
        if (extension == "png" && doc.Solids.Count == 0) return UI.BlueprintThumbnail.Render(doc, 1600, 1000, 0.05).EncodePng();
        if (extension == "png")
            return Rendering.SoftwareRenderer.Render(doc.ToCadModel(includeDrawing: doc.Solids.Count == 0), new Rendering.RenderOptions { Width = 1600, Height = 1000 }).EncodePng();
        // 2D formats get the drawing; solid formats get solids (STEP/STL/... ignore drawing entities anyway)
        var model = doc.ToCadModel();
        if (extension is "dxf" or "svg" or "pdf" && doc.Entities.Count == 0) model.Drawing.Clear();
        return model.SaveToBytes(extension, new ExportOptions { Title = doc.Name });
    }
}

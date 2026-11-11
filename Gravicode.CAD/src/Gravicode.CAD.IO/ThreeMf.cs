using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using static Gravicode.CAD.IO.CadIO;

namespace Gravicode.CAD.IO;

/// <summary>3MF (3D Manufacturing Format) core spec: meshes, base materials colors, components and build transforms.</summary>
public sealed class ThreeMfFormat : ICadImporter, ICadExporter
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    public string FormatName => "3MF";
    public IReadOnlyList<string> Extensions => ["3mf"];

    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Write(zip, "[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/></Types>""");
        Write(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>""");

        string unit = model.Units switch { LengthUnit.Centimeter => "centimeter", LengthUnit.Meter => "meter", LengthUnit.Inch => "inch", LengthUnit.Foot => "foot", LengthUnit.Micrometer => "micron", _ => "millimeter" };
        var sb = new StringBuilder(1 << 20);
        sb.Append($"""<?xml version="1.0" encoding="UTF-8"?><model unit="{unit}" xml:lang="en-US" xmlns="{Ns}">""");
        sb.Append($"<metadata name=\"Title\">{System.Security.SecurityElement.Escape(model.Name)}</metadata>");
        sb.Append($"<metadata name=\"Application\">{GravicodeCad.ProductName} {GravicodeCad.Version}</metadata>");
        sb.Append($"<metadata name=\"Designer\">{System.Security.SecurityElement.Escape(options.Organization)}</metadata>");
        sb.Append("<resources><basematerials id=\"1\">");
        foreach (var b in model.Bodies) sb.Append($"<base name=\"{System.Security.SecurityElement.Escape(b.Name)}\" displaycolor=\"{b.Color.ToHex()}FF\"/>");
        sb.Append("</basematerials>");
        int id = 2;
        for (int bi = 0; bi < model.Bodies.Count; bi++)
        {
            var m = model.Bodies[bi].Mesh;
            sb.Append($"<object id=\"{id + bi}\" type=\"model\" name=\"{System.Security.SecurityElement.Escape(model.Bodies[bi].Name)}\" pid=\"1\" pindex=\"{bi}\"><mesh><vertices>");
            foreach (var p in m.Positions) sb.Append($"<vertex x=\"{F(p.X)}\" y=\"{F(p.Y)}\" z=\"{F(p.Z)}\"/>");
            sb.Append("</vertices><triangles>");
            for (int i = 0; i < m.Indices.Count; i += 3) sb.Append($"<triangle v1=\"{m.Indices[i]}\" v2=\"{m.Indices[i + 1]}\" v3=\"{m.Indices[i + 2]}\"/>");
            sb.Append("</triangles></mesh></object>");
        }
        sb.Append("</resources><build>");
        for (int bi = 0; bi < model.Bodies.Count; bi++) sb.Append($"<item objectid=\"{id + bi}\"/>");
        sb.Append("</build></model>");
        Write(zip, "3D/3dmodel.model", sb.ToString());
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes);
    }

    public CadModel Import(Stream stream, ImportOptions options)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase) && e.FullName.Contains("3D/", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("3MF package has no 3D model part.");
        XDocument doc;
        using (var s = entry.Open()) doc = XDocument.Load(s);
        var root = doc.Root!;
        var ns = root.Name.Namespace;
        var model = new CadModel
        {
            Units = (string?)root.Attribute("unit") switch { "centimeter" => LengthUnit.Centimeter, "meter" => LengthUnit.Meter, "inch" => LengthUnit.Inch, "foot" => LengthUnit.Foot, "micron" => LengthUnit.Micrometer, _ => LengthUnit.Millimeter }
        };
        var title = root.Elements(ns + "metadata").FirstOrDefault(m => (string?)m.Attribute("name") == "Title")?.Value;
        if (!string.IsNullOrWhiteSpace(title)) model.Name = title;

        var resources = root.Element(ns + "resources")!;
        var colors = new Dictionary<string, List<CadColor>>();
        foreach (var bm in resources.Elements(ns + "basematerials"))
            colors[(string)bm.Attribute("id")!] = [.. bm.Elements(ns + "base").Select(b => ParseColor((string?)b.Attribute("displaycolor")))];
        var objects = resources.Elements(ns + "object").ToDictionary(o => (string)o.Attribute("id")!);

        foreach (var item in root.Element(ns + "build")?.Elements(ns + "item") ?? [])
            AddObject((string)item.Attribute("objectid")!, ParseTransform((string?)item.Attribute("transform")), 0);
        return model;

        void AddObject(string id, Mat4 tf, int depth)
        {
            if (depth > 16 || !objects.TryGetValue(id, out var obj)) return;
            if (obj.Element(ns + "mesh") is { } meshEl)
            {
                var m = new Mesh();
                foreach (var v in meshEl.Element(ns + "vertices")!.Elements(ns + "vertex"))
                    m.AddVertex(tf.TransformPoint(new Vec3(D((string)v.Attribute("x")!), D((string)v.Attribute("y")!), D((string)v.Attribute("z")!))));
                foreach (var t in meshEl.Element(ns + "triangles")!.Elements(ns + "triangle"))
                    m.AddTriangle((int)t.Attribute("v1")!, (int)t.Attribute("v2")!, (int)t.Attribute("v3")!);
                if (tf.Determinant3 < 0) m = m.Flipped();
                var body = model.AddBody((string?)obj.Attribute("name") ?? $"object{id}", m);
                var pid = (string?)obj.Attribute("pid");
                if (pid != null && colors.TryGetValue(pid, out var list))
                {
                    int pi = (int?)obj.Attribute("pindex") ?? 0;
                    if (pi < list.Count) body.Color = list[pi];
                }
            }
            foreach (var comp in obj.Element(ns + "components")?.Elements(ns + "component") ?? [])
                AddObject((string)comp.Attribute("objectid")!, tf * ParseTransform((string?)comp.Attribute("transform")), depth + 1);
        }
    }

    private static CadColor ParseColor(string? hex) => string.IsNullOrEmpty(hex) ? CadColor.Steel : CadColor.FromHex(hex);

    /// <summary>3MF uses row vectors: p' = [x y z 1] · M with M given as 12 values (m00 m01 m02 m10 ... m32).</summary>
    private static Mat4 ParseTransform(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Mat4.Identity;
        var v = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(D).ToArray();
        if (v.Length != 12) return Mat4.Identity;
        return new Mat4(v[0], v[3], v[6], v[9], v[1], v[4], v[7], v[10], v[2], v[5], v[8], v[11], 0, 0, 0, 1);
    }
}

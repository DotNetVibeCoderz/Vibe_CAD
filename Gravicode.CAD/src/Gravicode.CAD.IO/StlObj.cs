using System.Text;
using static Gravicode.CAD.IO.CadIO;

namespace Gravicode.CAD.IO;

/// <summary>STL, ASCII and binary (auto-detected on import).</summary>
public sealed class StlFormat : ICadImporter, ICadExporter
{
    public string FormatName => "STL";
    public IReadOnlyList<string> Extensions => ["stl"];

    public CadModel Import(Stream stream, ImportOptions options)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        var mesh = IsBinary(data) ? ReadBinary(data) : ReadAscii(data);
        var model = new CadModel { Units = options.DefaultUnit };
        model.AddBody(Path.GetFileNameWithoutExtension(options.SourcePath ?? "stl"), mesh.Welded(1e-7));
        return model;
    }

    private static bool IsBinary(byte[] d)
    {
        if (d.Length < 84) return false;
        uint n = BitConverter.ToUInt32(d, 80);
        if (84 + (long)n * 50 == d.Length) return true;
        var head = Encoding.ASCII.GetString(d, 0, Math.Min(d.Length, 512)).TrimStart();
        return !(head.StartsWith("solid", StringComparison.OrdinalIgnoreCase) && head.Contains("facet", StringComparison.OrdinalIgnoreCase));
    }

    private static Mesh ReadBinary(byte[] d)
    {
        int n = (int)BitConverter.ToUInt32(d, 80);
        var m = new Mesh(n * 3, n);
        for (int i = 0; i < n; i++)
        {
            int o = 84 + i * 50 + 12;
            m.AddTriangle(V(d, o), V(d, o + 12), V(d, o + 24));
        }
        return m;
        static Vec3 V(byte[] d, int o) => new(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8));
    }

    private static Mesh ReadAscii(byte[] d)
    {
        var m = new Mesh();
        var verts = new List<Vec3>(3);
        using var r = new StreamReader(new MemoryStream(d));
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var s = line.AsSpan().Trim();
            if (!s.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = s[6..].ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            verts.Add(new(D(parts[0]), D(parts[1]), D(parts[2])));
            if (verts.Count == 3) { m.AddTriangle(verts[0], verts[1], verts[2]); verts.Clear(); }
        }
        return m;
    }

    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        var mesh = model.ToMesh();
        if (options.Binary)
        {
            using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            var header = new byte[80];
            Encoding.ASCII.GetBytes($"Gravicode.CAD binary STL: {model.Name}".PadRight(80)[..80], header);
            w.Write(header);
            w.Write((uint)mesh.TriangleCount);
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var (a, b, c) = mesh.GetTriangle(t);
                var n = Vec3.Cross(b - a, c - a).Normalized();
                foreach (var v in (ReadOnlySpan<Vec3>)[n, a, b, c]) { w.Write((float)v.X); w.Write((float)v.Y); w.Write((float)v.Z); }
                w.Write((ushort)0);
            }
        }
        else
        {
            using var w = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true);
            var name = model.Name.Replace(' ', '_');
            w.WriteLine($"solid {name}");
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var (a, b, c) = mesh.GetTriangle(t);
                var n = Vec3.Cross(b - a, c - a).Normalized();
                w.WriteLine($"  facet normal {G(n.X)} {G(n.Y)} {G(n.Z)}");
                w.WriteLine("    outer loop");
                foreach (var v in (ReadOnlySpan<Vec3>)[a, b, c]) w.WriteLine($"      vertex {G(v.X)} {G(v.Y)} {G(v.Z)}");
                w.WriteLine("    endloop");
                w.WriteLine("  endfacet");
            }
            w.WriteLine($"endsolid {name}");
        }
    }
}

/// <summary>Wavefront OBJ (groups/objects become bodies; polygons are fan-triangulated; usemtl colors via .mtl when present).</summary>
public sealed class ObjFormat : ICadImporter, ICadExporter
{
    public string FormatName => "Wavefront OBJ";
    public IReadOnlyList<string> Extensions => ["obj"];

    public CadModel Import(Stream stream, ImportOptions options)
    {
        var model = new CadModel { Units = options.DefaultUnit };
        var positions = new List<Vec3>();
        var colors = LoadMtl(options.SourcePath);
        Mesh? current = null; string currentName = "Object"; string? currentMtl = null;
        var remap = new Dictionary<int, int>();
        using var r = new StreamReader(stream, leaveOpen: true);
        string? line;
        void Flush()
        {
            if (current is { IsEmpty: false })
            {
                var body = model.AddBody(currentName, current);
                if (currentMtl != null && colors.TryGetValue(currentMtl, out var c)) body.Color = c;
            }
            current = null; remap.Clear();
        }
        while ((line = r.ReadLine()) != null)
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#') continue;
            var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v": positions.Add(new(D(parts[1]), D(parts[2]), D(parts[3]))); break;
                case "o":
                case "g":
                    Flush(); currentName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Object"; break;
                case "usemtl":
                    if (current is { IsEmpty: false } && currentMtl != null && currentMtl != parts[1]) { var n = currentName; Flush(); currentName = n; }
                    currentMtl = parts.Length > 1 ? parts[1] : null; break;
                case "f":
                    current ??= new Mesh();
                    var idx = new int[parts.Length - 1];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var vi = int.Parse(parts[i].Split('/')[0], Inv);
                        int gi = vi < 0 ? positions.Count + vi : vi - 1;
                        if (!remap.TryGetValue(gi, out var li)) { li = current.AddVertex(positions[gi]); remap[gi] = li; }
                        idx[i - 1] = li;
                    }
                    current.AddFan(idx);
                    break;
                case "l":
                    model.Wires.Add(new Polyline3(parts.Skip(1).Select(p => positions[int.Parse(p.Split('/')[0], Inv) - 1])));
                    break;
            }
        }
        Flush();
        return model;
    }

    private static Dictionary<string, CadColor> LoadMtl(string? objPath)
    {
        var map = new Dictionary<string, CadColor>();
        if (objPath is null) return map;
        var mtl = Path.ChangeExtension(objPath, ".mtl");
        if (!File.Exists(mtl)) return map;
        string? name = null;
        foreach (var raw in File.ReadLines(mtl))
        {
            var p = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p[0] == "newmtl" && p.Length > 1) name = p[1];
            else if (p[0] == "Kd" && name != null && p.Length >= 4) map[name] = new CadColor(D(p[1]), D(p[2]), D(p[3]));
        }
        return map;
    }

    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        using var w = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true);
        w.WriteLine($"# {GravicodeCad.ProductName} {GravicodeCad.Version} - {GravicodeCad.Credits}");
        w.WriteLine($"# {model.Name}");
        int offset = 1;
        foreach (var b in model.Bodies)
        {
            w.WriteLine($"o {b.Name.Replace(' ', '_')}");
            w.WriteLine($"# color {b.Color.ToHex()}");
            foreach (var p in b.Mesh.Positions) w.WriteLine($"v {G(p.X)} {G(p.Y)} {G(p.Z)}");
            for (int i = 0; i < b.Mesh.Indices.Count; i += 3)
                w.WriteLine($"f {b.Mesh.Indices[i] + offset} {b.Mesh.Indices[i + 1] + offset} {b.Mesh.Indices[i + 2] + offset}");
            offset += b.Mesh.VertexCount;
        }
        foreach (var wire in model.Wires)
        {
            foreach (var p in wire.Points) w.WriteLine($"v {G(p.X)} {G(p.Y)} {G(p.Z)}");
            var ids = Enumerable.Range(offset, wire.Points.Count).Select(i => i.ToString(Inv)).ToList();
            if (wire.Closed && ids.Count > 0) ids.Add(ids[0]);
            w.WriteLine("l " + string.Join(' ', ids));
            offset += wire.Points.Count;
        }
    }
}

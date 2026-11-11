using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravicode.CAD.IO;

/// <summary>
/// glTF 2.0: .glb (binary, default) and .gltf (JSON with embedded base64 buffer).
/// Export writes one mesh + PBR material per body; import reads triangle primitives, applies node
/// transforms (matrix or TRS) and base colors. Units: glTF is meters, models are scaled from/to mm.
/// </summary>
public sealed class GltfFormat : ICadImporter, ICadExporter
{
    public string FormatName => "glTF 2.0";
    public IReadOnlyList<string> Extensions => ["glb", "gltf"];

    /// <summary>When true (default) millimeter models are written in meters, as glTF requires.</summary>
    public static bool ConvertToMeters { get; set; } = true;

    // ---------------- export ----------------
    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        bool glb = options.TargetExtension == "gltf" ? false : options.TargetExtension == "glb" || options.Binary;
        double scale = ConvertToMeters ? Units.Factor(model.Units, LengthUnit.Meter) : 1;
        var bin = new MemoryStream();
        var accessors = new JsonArray(); var views = new JsonArray(); var meshes = new JsonArray();
        var materials = new JsonArray(); var nodes = new JsonArray(); var sceneNodes = new JsonArray();

        foreach (var body in model.Bodies.Where(b => b.Visible && !b.Mesh.IsEmpty))
        {
            var sm = ShadedMesh.FromMesh(body.Mesh);
            var pos = sm.Positions.Select(p => (float)(p * scale)).ToArray();
            float[] min = [float.MaxValue, float.MaxValue, float.MaxValue], max = [float.MinValue, float.MinValue, float.MinValue];
            for (int i = 0; i < pos.Length; i += 3)
                for (int k = 0; k < 3; k++) { min[k] = Math.Min(min[k], pos[i + k]); max[k] = Math.Max(max[k], pos[i + k]); }

            int posAcc = AddAccessor(Write(pos), 5126, sm.VertexCount, "VEC3", 34962, min, max);
            int nrmAcc = AddAccessor(Write(sm.Normals), 5126, sm.VertexCount, "VEC3", 34962);
            int idxAcc = AddAccessor(Write(sm.Indices), 5125, sm.Indices.Length, "SCALAR", 34963);

            materials.Add(new JsonObject
            {
                ["name"] = body.Name,
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorFactor"] = new JsonArray(body.Color.R, body.Color.G, body.Color.B, body.Color.A),
                    ["metallicFactor"] = body.Metallic,
                    ["roughnessFactor"] = body.Roughness
                },
                ["doubleSided"] = false
            });
            meshes.Add(new JsonObject
            {
                ["name"] = body.Name,
                ["primitives"] = new JsonArray(new JsonObject
                {
                    ["attributes"] = new JsonObject { ["POSITION"] = posAcc, ["NORMAL"] = nrmAcc },
                    ["indices"] = idxAcc,
                    ["material"] = materials.Count - 1,
                    ["mode"] = 4
                })
            });
            nodes.Add(new JsonObject { ["name"] = body.Name, ["mesh"] = meshes.Count - 1 });
            sceneNodes.Add(nodes.Count - 1);
        }

        // CAD is Z-up, glTF is Y-up: rotate -90° about X at the root.
        nodes.Add(new JsonObject { ["name"] = model.Name, ["children"] = sceneNodes, ["rotation"] = new JsonArray(-Math.Sqrt(0.5), 0, 0, Math.Sqrt(0.5)) });
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = $"{GravicodeCad.ProductName} {GravicodeCad.Version} ({GravicodeCad.Credits})" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["name"] = model.Name, ["nodes"] = new JsonArray(nodes.Count - 1) }),
            ["nodes"] = nodes, ["meshes"] = meshes, ["materials"] = materials,
            ["accessors"] = accessors, ["bufferViews"] = views
        };
        var binBytes = bin.ToArray();
        var buffer = new JsonObject { ["byteLength"] = binBytes.Length };
        if (!glb) buffer["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(binBytes);
        root["buffers"] = new JsonArray(buffer);

        var json = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = !glb }));
        if (!glb) { stream.Write(json); return; }

        int jsonPad = (4 - json.Length % 4) % 4, binPad = (4 - binBytes.Length % 4) % 4;
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        w.Write(0x46546C67u); w.Write(2u);
        w.Write((uint)(12 + 8 + json.Length + jsonPad + 8 + binBytes.Length + binPad));
        w.Write((uint)(json.Length + jsonPad)); w.Write(0x4E4F534Au); w.Write(json); for (int i = 0; i < jsonPad; i++) w.Write((byte)' ');
        w.Write((uint)(binBytes.Length + binPad)); w.Write(0x004E4942u); w.Write(binBytes); for (int i = 0; i < binPad; i++) w.Write((byte)0);

        (int Offset, int Length) Write<T>(T[] data) where T : unmanaged
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            int off = (int)bin.Length;
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan());
            bin.Write(bytes);
            return (off, bytes.Length);
        }

        int AddAccessor((int Offset, int Length) view, int componentType, int count, string type, int target, float[]? mn = null, float[]? mx = null)
        {
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = view.Offset, ["byteLength"] = view.Length, ["target"] = target });
            var acc = new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (mn != null && mx != null) { acc["min"] = new JsonArray(mn[0], mn[1], mn[2]); acc["max"] = new JsonArray(mx[0], mx[1], mx[2]); }
            accessors.Add(acc);
            return accessors.Count - 1;
        }
    }

    // ---------------- import ----------------
    public CadModel Import(Stream stream, ImportOptions options)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        JsonNode root; byte[]? glbBin = null;
        if (data.Length >= 12 && BitConverter.ToUInt32(data, 0) == 0x46546C67u)
        {
            int off = 12; string? json = null;
            while (off + 8 <= data.Length)
            {
                int len = (int)BitConverter.ToUInt32(data, off); uint type = BitConverter.ToUInt32(data, off + 4);
                if (type == 0x4E4F534Au) json = Encoding.UTF8.GetString(data, off + 8, len);
                else if (type == 0x004E4942u) glbBin = data.AsSpan(off + 8, len).ToArray();
                off += 8 + len;
            }
            root = JsonNode.Parse(json ?? throw new InvalidDataException("GLB has no JSON chunk."))!;
        }
        else root = JsonNode.Parse(data)!;

        var buffers = (root["buffers"]?.AsArray() ?? []).Select(b => LoadBuffer(b!, glbBin, options.SourcePath)).ToList();
        var model = new CadModel { Units = LengthUnit.Millimeter };
        double toMm = ConvertToMeters ? 1000 : 1;

        var nodes = root["nodes"]?.AsArray() ?? [];
        var meshes = root["meshes"]?.AsArray() ?? [];
        var materials = root["materials"]?.AsArray();
        int sceneIdx = root["scene"]?.GetValue<int>() ?? 0;
        var scene = root["scenes"]?.AsArray().ElementAtOrDefault(sceneIdx);
        var roots = scene?["nodes"]?.AsArray().Select(n => n!.GetValue<int>()).ToList() ?? Enumerable.Range(0, nodes.Count).ToList();
        // glTF Y-up -> CAD Z-up
        var yUpToZUp = Mat4.RotateX(Math.PI / 2) * Mat4.Scale(toMm);
        foreach (var r in roots) Visit(r, yUpToZUp);
        if (nodes.Count == 0) for (int mi = 0; mi < meshes.Count; mi++) AddMesh(mi, yUpToZUp, $"mesh{mi}");
        return model;

        void Visit(int ni, Mat4 parent)
        {
            var n = nodes[ni]!;
            var local = Mat4.Identity;
            if (n["matrix"] is JsonArray ma) local = Mat4.FromColumnMajor(ma.Select(v => v!.GetValue<double>()).ToArray());
            else
            {
                var t = n["translation"]?.AsArray(); var q = n["rotation"]?.AsArray(); var s = n["scale"]?.AsArray();
                var T = t is null ? Mat4.Identity : Mat4.Translate(Num(t[0]), Num(t[1]), Num(t[2]));
                var R = q is null ? Mat4.Identity : FromQuat(Num(q[0]), Num(q[1]), Num(q[2]), Num(q[3]));
                var S = s is null ? Mat4.Identity : Mat4.Scale(Num(s[0]), Num(s[1]), Num(s[2]));
                local = T * R * S;
            }
            var world = parent * local;
            if (n["mesh"] is JsonNode mn) AddMesh(mn.GetValue<int>(), world, n["name"]?.GetValue<string>() ?? $"node{ni}");
            if (n["children"] is JsonArray ch) foreach (var c in ch) Visit(c!.GetValue<int>(), world);
        }

        void AddMesh(int mi, Mat4 world, string name)
        {
            var mesh = meshes[mi]!;
            foreach (var prim in mesh["primitives"]!.AsArray())
            {
                int mode = prim!["mode"]?.GetValue<int>() ?? 4;
                if (mode != 4) { model.Warnings.Add($"Skipped non-triangle primitive (mode {mode}) in '{name}'."); continue; }
                var pos = ReadFloat3(prim["attributes"]!["POSITION"]!.GetValue<int>());
                var m = new Mesh(pos.Count, pos.Count / 3);
                foreach (var p in pos) m.AddVertex(world.TransformPoint(p));
                if (prim["indices"] is JsonNode ix) { var ind = ReadIndices(ix.GetValue<int>()); for (int i = 0; i + 2 < ind.Count; i += 3) m.AddTriangle(ind[i], ind[i + 1], ind[i + 2]); }
                else for (int i = 0; i + 2 < pos.Count; i += 3) m.AddTriangle(i, i + 1, i + 2);
                if (world.Determinant3 < 0) m = m.Flipped();
                var body = model.AddBody(mesh["name"]?.GetValue<string>() ?? name, m.Welded(1e-7));
                if (prim["material"] is JsonNode matIdx && materials?[matIdx.GetValue<int>()]?["pbrMetallicRoughness"] is JsonNode pbr)
                {
                    if (pbr["baseColorFactor"] is JsonArray bc) body.Color = new CadColor(Num(bc[0]), Num(bc[1]), Num(bc[2]), Num(bc[3]));
                    if (pbr["metallicFactor"] is JsonNode mf) body.Metallic = mf.GetValue<double>();
                    if (pbr["roughnessFactor"] is JsonNode rf) body.Roughness = rf.GetValue<double>();
                }
            }
        }

        ReadOnlySpan<byte> View(int accessor, out JsonNode acc, out int stride)
        {
            acc = root["accessors"]![accessor]!;
            var bv = root["bufferViews"]![acc["bufferView"]!.GetValue<int>()]!;
            var buf = buffers[bv["buffer"]!.GetValue<int>()];
            int off = (bv["byteOffset"]?.GetValue<int>() ?? 0) + (acc["byteOffset"]?.GetValue<int>() ?? 0);
            stride = bv["byteStride"]?.GetValue<int>() ?? 0;
            return buf.AsSpan(off);
        }

        List<Vec3> ReadFloat3(int accessor)
        {
            var span = View(accessor, out var acc, out var stride);
            int count = acc["count"]!.GetValue<int>();
            if (stride == 0) stride = 12;
            var r = new List<Vec3>(count);
            for (int i = 0; i < count; i++)
            {
                var s = span[(i * stride)..];
                r.Add(new(BitConverter.ToSingle(s), BitConverter.ToSingle(s[4..]), BitConverter.ToSingle(s[8..])));
            }
            return r;
        }

        List<int> ReadIndices(int accessor)
        {
            var span = View(accessor, out var acc, out _);
            int count = acc["count"]!.GetValue<int>();
            int ct = acc["componentType"]!.GetValue<int>();
            var r = new List<int>(count);
            for (int i = 0; i < count; i++)
                r.Add(ct switch
                {
                    5121 => span[i],
                    5123 => BitConverter.ToUInt16(span[(i * 2)..]),
                    _ => (int)BitConverter.ToUInt32(span[(i * 4)..])
                });
            return r;
        }
    }

    private static double Num(JsonNode? n) => n?.GetValue<double>() ?? 0;

    private static Mat4 FromQuat(double x, double y, double z, double w) => new(
        1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 0,
        2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), 0,
        2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), 0,
        0, 0, 0, 1);

    private static byte[] LoadBuffer(JsonNode b, byte[]? glbBin, string? sourcePath)
    {
        var uri = b["uri"]?.GetValue<string>();
        if (uri is null) return glbBin ?? throw new InvalidDataException("Buffer without data.");
        if (uri.StartsWith("data:", StringComparison.Ordinal)) return Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
        var dir = sourcePath is null ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        return File.ReadAllBytes(Path.Combine(dir, Uri.UnescapeDataString(uri)));
    }
}

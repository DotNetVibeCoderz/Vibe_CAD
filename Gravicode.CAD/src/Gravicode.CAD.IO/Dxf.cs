using System.Text;
using Gravicode.CAD.Geometry;
using static Gravicode.CAD.IO.CadIO;

namespace Gravicode.CAD.IO;

/// <summary>
/// AutoCAD DXF (ASCII). Import: LINE, CIRCLE, ARC, LWPOLYLINE (with bulges), POLYLINE/VERTEX (2D, 3D, polyface mesh),
/// 3DFACE, SPLINE, ELLIPSE, POINT, TEXT/MTEXT and INSERT of BLOCKS. Export: drawing entities, 3D wires and meshes as 3DFACE.
/// </summary>
public sealed class DxfFormat : ICadImporter, ICadExporter
{
    public string FormatName => "DXF";
    public IReadOnlyList<string> Extensions => ["dxf"];

    // ======================= import =======================
    private readonly record struct Pair(int Code, string Value);

    private sealed class RawEntity(string type)
    {
        public string Type = type;
        public List<Pair> Pairs = [];
        public string Layer => Get(8) ?? "0";
        public string? Get(int code) => Pairs.FirstOrDefault(p => p.Code == code).Value;
        public double Num(int code, double def = 0) => Get(code) is { } s && TryD(s, out var v) ? v : def;
        public int Int(int code, int def = 0) => Get(code) is { } s && int.TryParse(s.Trim(), out var v) ? v : def;
        public IEnumerable<double> All(int code) => Pairs.Where(p => p.Code == code).Select(p => D(p.Value));
    }

    public CadModel Import(Stream stream, ImportOptions options)
    {
        var pairs = ReadPairs(stream);
        var model = new CadModel();
        var blocks = new Dictionary<string, List<RawEntity>>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<RawEntity>();
        string section = "";
        string? blockName = null;
        List<RawEntity>? target = null;
        RawEntity? cur = null;
        int insUnits = 4;

        for (int i = 0; i < pairs.Count; i++)
        {
            var p = pairs[i];
            if (p.Code == 0)
            {
                cur = null;
                switch (p.Value)
                {
                    case "SECTION": section = pairs.ElementAtOrDefault(i + 1).Value ?? ""; i++; continue;
                    case "ENDSEC": section = ""; target = null; continue;
                    case "EOF": break;
                    case "BLOCK" when section == "BLOCKS":
                        blockName = null; target = []; cur = new RawEntity("BLOCK"); continue;
                    case "ENDBLK":
                        target = null; continue;
                }
                if (section == "ENTITIES") { cur = new RawEntity(p.Value); entities.Add(cur); }
                else if (section == "BLOCKS" && target != null) { cur = new RawEntity(p.Value); target.Add(cur); }
                continue;
            }
            if (section == "HEADER" && p.Code == 9 && p.Value == "$INSUNITS" && i + 1 < pairs.Count) int.TryParse(pairs[i + 1].Value.Trim(), out insUnits);
            if (cur == null) continue;
            if (cur.Type == "BLOCK" && p.Code == 2 && blockName == null && target != null) { blockName = p.Value; blocks[blockName] = target; continue; }
            cur.Pairs.Add(p);
        }

        model.Units = insUnits switch { 1 => LengthUnit.Inch, 2 => LengthUnit.Foot, 5 => LengthUnit.Centimeter, 6 => LengthUnit.Meter, 13 => LengthUnit.Micrometer, _ => LengthUnit.Millimeter };
        var faces = new Mesh();
        Emit(entities, Mat4.Identity, 0);
        if (!faces.IsEmpty) model.AddBody("3DFACE", faces.Welded(1e-7));
        return model;

        void Emit(List<RawEntity> list, Mat4 tf, int depth)
        {
            Vec2 T(Vec2 v) { var r = tf.TransformPoint(v.ToVec3()); return new(r.X, r.Y); }
            for (int k = 0; k < list.Count; k++)
            {
                var e = list[k];
                var color = AciColor(e.Int(62, 256));
                DrawingEntity? d = null;
                switch (e.Type)
                {
                    case "LINE":
                        {
                            var a = new Vec3(e.Num(10), e.Num(20), e.Num(30)); var b = new Vec3(e.Num(11), e.Num(21), e.Num(31));
                            if (Math.Abs(a.Z) > 1e-9 || Math.Abs(b.Z) > 1e-9) model.Wires.Add(new Polyline3([tf.TransformPoint(a), tf.TransformPoint(b)]) { Layer = e.Layer, Color = color });
                            else d = new LineEntity(T(a.XY), T(b.XY));
                            break;
                        }
                    case "CIRCLE": d = new CircleEntity(new(e.Num(10), e.Num(20)), e.Num(40)).Transformed(T); break;
                    case "ARC": d = new ArcEntity(new(e.Num(10), e.Num(20)), e.Num(40), Units.Deg(e.Num(50)), Units.Deg(e.Num(51))).Transformed(T); break;
                    case "POINT": d = new PointEntity(T(new(e.Num(10), e.Num(20)))); break;
                    case "TEXT":
                    case "MTEXT": d = new TextEntity(T(new(e.Num(10), e.Num(20))), (e.Get(1) ?? "").Replace("\\P", "\n"), e.Num(40, 2.5)); break;
                    case "LWPOLYLINE": d = LwPolyline(e, options.ChordTolerance).Transformed(T); break;
                    case "ELLIPSE":
                        {
                            var c = new Vec2(e.Num(10), e.Num(20)); var maj = new Vec2(e.Num(11), e.Num(21));
                            double ratio = e.Num(40, 1), t0 = e.Num(41, 0), t1 = e.Num(42, 2 * Math.PI);
                            if (t1 <= t0) t1 += 2 * Math.PI;
                            var minor = maj.Perp() * ratio;
                            int n = 96;
                            var pts = Enumerable.Range(0, n + 1).Select(i2 => { double t = t0 + (t1 - t0) * i2 / n; return T(c + maj * Math.Cos(t) + minor * Math.Sin(t)); });
                            d = new PolylineEntity(pts, Math.Abs(t1 - t0 - 2 * Math.PI) < 1e-6);
                            break;
                        }
                    case "SPLINE":
                        {
                            int deg = e.Int(71, 3);
                            var xs = e.All(10).ToList(); var ys = e.All(20).ToList(); var zs = e.All(30).ToList();
                            var knots = e.All(40).ToList(); var weights = e.All(41).ToList();
                            var cps = xs.Select((x, i2) => new Vec3(x, ys.ElementAtOrDefault(i2), zs.ElementAtOrDefault(i2))).ToList();
                            if (cps.Count > deg)
                            {
                                try
                                {
                                    var curve = new NurbsCurve(deg, cps, weights.Count == cps.Count ? weights : null, knots.Count == cps.Count + deg + 1 ? knots : null);
                                    var pts = curve.Tessellate(options.ChordTolerance);
                                    d = new PolylineEntity(pts.Select(q => T(q.XY)), (e.Int(70) & 1) != 0);
                                }
                                catch (ArgumentException ex) { model.Warnings.Add($"SPLINE skipped: {ex.Message}"); }
                            }
                            break;
                        }
                    case "3DFACE":
                        {
                            var v = new Vec3[4];
                            for (int c = 0; c < 4; c++) v[c] = tf.TransformPoint(new Vec3(e.Num(10 + c), e.Num(20 + c), e.Num(30 + c)));
                            faces.AddTriangle(v[0], v[1], v[2]);
                            if (!v[3].ApproxEquals(v[2], 1e-12)) faces.AddTriangle(v[0], v[2], v[3]);
                            break;
                        }
                    case "POLYLINE":
                        {
                            int flags = e.Int(70);
                            var verts = new List<RawEntity>();
                            while (k + 1 < list.Count && list[k + 1].Type == "VERTEX") verts.Add(list[++k]);
                            if (k + 1 < list.Count && list[k + 1].Type == "SEQEND") k++;
                            if ((flags & 64) != 0) // polyface mesh
                            {
                                var pv = verts.Where(v => (v.Int(70) & 64) != 0).Select(v => tf.TransformPoint(new Vec3(v.Num(10), v.Num(20), v.Num(30)))).ToList();
                                var pm = new Mesh(); foreach (var q in pv) pm.AddVertex(q);
                                foreach (var f in verts.Where(v => (v.Int(70) & 128) != 0 && (v.Int(70) & 64) == 0))
                                {
                                    var ids = new[] { 71, 72, 73, 74 }.Select(c => Math.Abs(f.Int(c)) - 1).Where(x => x >= 0).ToArray();
                                    if (ids.Length >= 3) pm.AddFan(ids);
                                }
                                if (!pm.IsEmpty) model.AddBody($"POLYFACE_{model.Bodies.Count}", pm).Color = color ?? CadColor.Steel;
                            }
                            else if ((flags & 8) != 0) // 3D polyline
                                model.Wires.Add(new Polyline3(verts.Select(v => tf.TransformPoint(new Vec3(v.Num(10), v.Num(20), v.Num(30)))), (flags & 1) != 0) { Layer = e.Layer, Color = color });
                            else
                            {
                                var pts = new List<Vec2>();
                                for (int vi = 0; vi < verts.Count; vi++)
                                {
                                    var a = new Vec2(verts[vi].Num(10), verts[vi].Num(20));
                                    double bulge = verts[vi].Num(42);
                                    pts.Add(a);
                                    if (Math.Abs(bulge) > 1e-12 && (vi + 1 < verts.Count || (flags & 1) != 0))
                                    {
                                        var b = new Vec2(verts[(vi + 1) % verts.Count].Num(10), verts[(vi + 1) % verts.Count].Num(20));
                                        pts.AddRange(BulgePoints(a, b, bulge, options.ChordTolerance));
                                    }
                                }
                                d = new PolylineEntity(pts.Select(T), (flags & 1) != 0);
                            }
                            break;
                        }
                    case "INSERT":
                        {
                            var name = e.Get(2);
                            if (name != null && blocks.TryGetValue(name, out var blk))
                            {
                                var ins = Mat4.Translate(e.Num(10), e.Num(20), e.Num(30)) * Mat4.RotateZ(Units.Deg(e.Num(50))) * Mat4.Scale(e.Num(41, 1), e.Num(42, 1), e.Num(43, 1));
                                if (depth < 16) Emit(blk, tf * ins, depth + 1);
                            }
                            break;
                        }
                }
                if (d != null) { d.Layer = e.Layer; d.Color = color; model.Drawing.Add(d); }
            }
        }
    }

    private static PolylineEntity LwPolyline(RawEntity e, double tol)
    {
        var verts = new List<(Vec2 P, double Bulge)>();
        double x = 0; bool haveX = false;
        foreach (var p in e.Pairs)
        {
            if (p.Code == 10) { x = D(p.Value); haveX = true; }
            else if (p.Code == 20 && haveX) { verts.Add((new Vec2(x, D(p.Value)), 0)); haveX = false; }
            else if (p.Code == 42 && verts.Count > 0) verts[^1] = (verts[^1].P, D(p.Value));
        }
        bool closed = (e.Int(70) & 1) != 0;
        var pts = new List<Vec2>();
        for (int i = 0; i < verts.Count; i++)
        {
            pts.Add(verts[i].P);
            if (Math.Abs(verts[i].Bulge) > 1e-12 && (i + 1 < verts.Count || closed))
                pts.AddRange(BulgePoints(verts[i].P, verts[(i + 1) % verts.Count].P, verts[i].Bulge, tol));
        }
        return new PolylineEntity(pts, closed);
    }

    /// <summary>Interior points of a bulged polyline segment (bulge = tan(θ/4)).</summary>
    private static IEnumerable<Vec2> BulgePoints(Vec2 a, Vec2 b, double bulge, double tol)
    {
        double theta = 4 * Math.Atan(bulge);
        double chord = Vec2.Distance(a, b);
        if (chord < 1e-12) yield break;
        double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
        var mid = (a + b) * 0.5;
        var dir = (b - a) / chord;
        double h = r * Math.Cos(theta / 2) * Math.Sign(bulge);
        var center = mid + dir.Perp() * h;
        double a0 = (a - center).Angle;
        int n = CurveMath.ArcSegments(Math.Abs(r), Math.Abs(theta), tol);
        for (int i = 1; i < n; i++) yield return center + Vec2.FromAngle(a0 + theta * i / n, Math.Abs(r));
    }

    private static List<Pair> ReadPairs(Stream s)
    {
        var list = new List<Pair>(1 << 14);
        using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        while (true)
        {
            var c = r.ReadLine(); if (c == null) break;
            var v = r.ReadLine(); if (v == null) break;
            if (int.TryParse(c.Trim(), out var code)) list.Add(new Pair(code, code == 1 ? v : v.Trim()));
        }
        return list;
    }

    private static readonly CadColor[] Aci = [
        CadColor.Black, CadColor.FromHex("#FF0000"), CadColor.FromHex("#FFFF00"), CadColor.FromHex("#00FF00"),
        CadColor.FromHex("#00FFFF"), CadColor.FromHex("#0000FF"), CadColor.FromHex("#FF00FF"), CadColor.White,
        CadColor.FromHex("#808080"), CadColor.FromHex("#C0C0C0")];
    private static CadColor? AciColor(int aci) => aci is > 0 and < 10 ? Aci[aci] : null;
    private static int ToAci(CadColor? c)
    {
        if (c is not { } col) return 256;
        int best = 7; double bd = double.MaxValue;
        for (int i = 1; i < Aci.Length; i++)
        {
            double d = Math.Pow(Aci[i].R - col.R, 2) + Math.Pow(Aci[i].G - col.G, 2) + Math.Pow(Aci[i].B - col.B, 2);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    // ======================= export =======================
    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        using var w = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\r\n" };
        void P(int code, string value) { w.WriteLine(code.ToString().PadLeft(3)); w.WriteLine(value); }
        void N(int code, double v) => P(code, F(v));

        int insunits = model.Units switch { LengthUnit.Inch => 1, LengthUnit.Foot => 2, LengthUnit.Centimeter => 5, LengthUnit.Meter => 6, LengthUnit.Micrometer => 13, _ => 4 };
        P(999, $"{GravicodeCad.ProductName} {GravicodeCad.Version} - {GravicodeCad.Credits}");
        P(0, "SECTION"); P(2, "HEADER");
        P(9, "$ACADVER"); P(1, "AC1009");
        P(9, "$INSUNITS"); P(70, insunits.ToString());
        var bb = model.Bounds;
        if (!bb.IsEmpty) { P(9, "$EXTMIN"); N(10, bb.Min.X); N(20, bb.Min.Y); N(30, bb.Min.Z); P(9, "$EXTMAX"); N(10, bb.Max.X); N(20, bb.Max.Y); N(30, bb.Max.Z); }
        P(0, "ENDSEC");

        var layers = model.Drawing.Select(d => d.Layer).Concat(model.Wires.Select(x => x.Layer)).Concat(model.Bodies.Select(b => Sanitize(b.Name))).Append("0").Distinct().ToList();
        P(0, "SECTION"); P(2, "TABLES");
        P(0, "TABLE"); P(2, "LAYER"); P(70, layers.Count.ToString());
        foreach (var l in layers) { P(0, "LAYER"); P(2, l); P(70, "0"); P(62, "7"); P(6, "CONTINUOUS"); }
        P(0, "ENDTAB"); P(0, "ENDSEC");

        P(0, "SECTION"); P(2, "ENTITIES");
        foreach (var d in model.Drawing)
        {
            void Head(string type) { P(0, type); P(8, d.Layer); if (d.Color is { } c) P(62, ToAci(c).ToString()); }
            switch (d)
            {
                case LineEntity l: Head("LINE"); N(10, l.A.X); N(20, l.A.Y); N(30, 0); N(11, l.B.X); N(21, l.B.Y); N(31, 0); break;
                case CircleEntity c: Head("CIRCLE"); N(10, c.Center.X); N(20, c.Center.Y); N(30, 0); N(40, c.Radius); break;
                case ArcEntity a: Head("ARC"); N(10, a.Center.X); N(20, a.Center.Y); N(30, 0); N(40, a.Radius); N(50, Units.ToDegrees(a.StartAngle)); N(51, Units.ToDegrees(a.EndAngle)); break;
                case PointEntity p: Head("POINT"); N(10, p.Position.X); N(20, p.Position.Y); N(30, 0); break;
                case TextEntity t: Head("TEXT"); N(10, t.Position.X); N(20, t.Position.Y); N(30, 0); N(40, t.Height); P(1, t.Text.Replace("\n", " ")); break;
                case PolylineEntity pl:
                    Head("POLYLINE"); P(66, "1"); N(10, 0); N(20, 0); N(30, 0); P(70, pl.Closed ? "1" : "0");
                    foreach (var v in pl.Points) { P(0, "VERTEX"); P(8, d.Layer); N(10, v.X); N(20, v.Y); N(30, 0); }
                    P(0, "SEQEND"); P(8, d.Layer);
                    break;
            }
        }
        foreach (var wire in model.Wires)
        {
            P(0, "POLYLINE"); P(8, wire.Layer); P(66, "1"); N(10, 0); N(20, 0); N(30, 0); P(70, (8 | (wire.Closed ? 1 : 0)).ToString());
            foreach (var v in wire.Points) { P(0, "VERTEX"); P(8, wire.Layer); N(10, v.X); N(20, v.Y); N(30, v.Z); P(70, "32"); }
            P(0, "SEQEND"); P(8, wire.Layer);
        }
        foreach (var b in model.Bodies)
        {
            var layer = Sanitize(b.Name); var aci = ToAci(b.Color).ToString();
            foreach (var (a, bb2, c) in b.Mesh.Triangles())
            {
                P(0, "3DFACE"); P(8, layer); P(62, aci);
                N(10, a.X); N(20, a.Y); N(30, a.Z); N(11, bb2.X); N(21, bb2.Y); N(31, bb2.Z);
                N(12, c.X); N(22, c.Y); N(32, c.Z); N(13, c.X); N(23, c.Y); N(33, c.Z);
            }
        }
        P(0, "ENDSEC"); P(0, "EOF");
    }

    private static string Sanitize(string s) => new([.. s.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? char.ToUpperInvariant(ch) : '_')]);
}

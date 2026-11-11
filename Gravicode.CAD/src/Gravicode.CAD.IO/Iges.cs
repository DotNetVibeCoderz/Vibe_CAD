using System.Text;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Topology;
using static Gravicode.CAD.IO.CadIO;

namespace Gravicode.CAD.IO;

/// <summary>
/// IGES 5.3 (fixed 80-column ASCII). Wireframe interchange:
/// import of 100 (arc), 102 (composite), 106 (copious data / polylines), 110 (line), 116 (point), 126 (NURBS curve) with 124 transforms;
/// export of drawing entities, 3D wires and body feature edges.
/// </summary>
public sealed class IgesFormat : ICadImporter, ICadExporter
{
    public string FormatName => "IGES";
    public IReadOnlyList<string> Extensions => ["igs", "iges"];

    // ============================== export ==============================
    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        var entities = new List<(int Type, int Form, string Params)>();
        string N(double v) => G(Math.Round(v, 10));
        void Line(Vec3 a, Vec3 b) => entities.Add((110, 0, $"110,{N(a.X)},{N(a.Y)},{N(a.Z)},{N(b.X)},{N(b.Y)},{N(b.Z)};"));
        void Poly(IReadOnlyList<Vec3> pts, bool closed)
        {
            var list = closed ? pts.Append(pts[0]).ToList() : pts.ToList();
            var sb = new StringBuilder($"106,2,{list.Count}");
            foreach (var p in list) sb.Append($",{N(p.X)},{N(p.Y)},{N(p.Z)}");
            entities.Add((106, 12, sb.Append(';').ToString()));
        }

        foreach (var d in model.Drawing)
        {
            switch (d)
            {
                case LineEntity l: Line(l.A.ToVec3(), l.B.ToVec3()); break;
                case ArcEntity a:
                    entities.Add((100, 0, $"100,0,{N(a.Center.X)},{N(a.Center.Y)},{N(a.StartPoint.X)},{N(a.StartPoint.Y)},{N(a.EndPoint.X)},{N(a.EndPoint.Y)};")); break;
                case CircleEntity c:
                    {
                        var s = c.Center + new Vec2(c.Radius, 0);
                        entities.Add((100, 0, $"100,0,{N(c.Center.X)},{N(c.Center.Y)},{N(s.X)},{N(s.Y)},{N(s.X)},{N(s.Y)};")); break;
                    }
                case PointEntity p: entities.Add((116, 0, $"116,{N(p.Position.X)},{N(p.Position.Y)},0.,0;")); break;
                case PolylineEntity pl when pl.Points.Count >= 2: Poly([.. pl.Points.Select(p => p.ToVec3())], pl.Closed); break;
            }
        }
        foreach (var wire in model.Wires) if (wire.Points.Count >= 2) Poly(wire.Points, wire.Closed);
        foreach (var b in model.Bodies)
            foreach (var (a, c) in new HalfEdgeMesh(b.Mesh).FeatureEdges(30)) Line(a, c);

        // ---- assemble sections ----
        var S = new List<string> { $"{GravicodeCad.ProductName} IGES export - {model.Name} - {GravicodeCad.Credits}" };
        int unitFlag = model.Units switch { LengthUnit.Inch => 1, LengthUnit.Foot => 4, LengthUnit.Meter => 6, LengthUnit.Centimeter => 10, LengthUnit.Micrometer => 11, _ => 2 };
        string unitName = model.Units switch { LengthUnit.Inch => "INCH", LengthUnit.Foot => "FT", LengthUnit.Meter => "M", LengthUnit.Centimeter => "CM", LengthUnit.Micrometer => "UM", _ => "MM" };
        string H(string s) => $"{s.Length}H{s}";
        var bb = model.Bounds;
        double maxCoord = bb.IsEmpty ? 1000 : Math.Max(bb.Max.Length, bb.Min.Length);
        var now = DateTime.UtcNow.ToString("yyyyMMdd.HHmmss");
        var global = $"1H,,1H;,{H(model.Name)},{H(model.Name + ".igs")},{H(GravicodeCad.ProductName)},{H(GravicodeCad.Version)},32,38,6,308,15,{H(model.Name)},1.,{unitFlag},{H(unitName)},1,0.01,{H(now)},1E-07,{N(maxCoord)},{H(options.Author)},{H(options.Organization)},11,0,{H(now)};";
        var GL = Wrap(global, 72);

        var P = new List<(string Text, int De)>();
        var D = new List<string>();
        int pLine = 1;
        for (int i = 0; i < entities.Count; i++)
        {
            int de = 2 * i + 1;
            var lines = Wrap(entities[i].Params, 64);
            foreach (var l in lines) P.Add((l, de));
            var (type, form, _) = entities[i];
            D.Add($"{type,8}{pLine,8}{0,8}{0,8}{0,8}{0,8}{0,8}{0,8}{"00000000",8}");
            D.Add($"{type,8}{0,8}{(type == 110 ? 1 : 7),8}{lines.Count,8}{form,8}{"",8}{"",8}{"",8}{0,8}");
            pLine += lines.Count;
        }

        using var w = new StreamWriter(stream, Encoding.ASCII, 1 << 16, leaveOpen: true) { NewLine = "\r\n" };
        for (int i = 0; i < S.Count; i++) w.WriteLine($"{S[i],-72}S{i + 1,7}");
        for (int i = 0; i < GL.Count; i++) w.WriteLine($"{GL[i],-72}G{i + 1,7}");
        for (int i = 0; i < D.Count; i++) w.WriteLine($"{D[i],-72}D{i + 1,7}");
        for (int i = 0; i < P.Count; i++) w.WriteLine($"{P[i].Text,-64}{P[i].De,8}P{i + 1,7}");
        w.WriteLine($"{$"S{S.Count,7}G{GL.Count,7}D{D.Count,7}P{P.Count,7}",-72}T{1,7}");
    }

    private static List<string> Wrap(string s, int width)
    {
        var r = new List<string>();
        var sb = new StringBuilder();
        foreach (var tok in SplitKeep(s))
        {
            if (sb.Length + tok.Length > width && sb.Length > 0) { r.Add(sb.ToString()); sb.Clear(); }
            sb.Append(tok);
        }
        if (sb.Length > 0) r.Add(sb.ToString());
        return r;

        static IEnumerable<string> SplitKeep(string s)
        {
            int st = 0;
            for (int i = 0; i < s.Length; i++)
                if (s[i] is ',' or ';') { yield return s[st..(i + 1)]; st = i + 1; }
            if (st < s.Length) yield return s[st..];
        }
    }

    // ============================== import ==============================
    public CadModel Import(Stream stream, ImportOptions options)
    {
        var lines = new List<string>();
        using (var r = new StreamReader(stream, Encoding.ASCII, false, 1 << 16, leaveOpen: true))
        { string? l; while ((l = r.ReadLine()) != null) if (l.Length >= 73) lines.Add(l.PadRight(80)); }
        var gText = string.Concat(lines.Where(l => l[72] == 'G').Select(l => l[..72]));
        var dLines = lines.Where(l => l[72] == 'D').ToList();
        var pLines = lines.Where(l => l[72] == 'P').ToList();
        var model = new CadModel();

        char pd = ',', rd = ';';
        if (gText.StartsWith("1H")) { pd = gText[2]; if (gText.Length > 6 && gText[4] == '1' && gText[5] == 'H') rd = gText[6]; }
        var gp = SplitParams(gText, pd, rd);
        if (gp.Count > 14 && int.TryParse(gp[14].Trim(), out var uf))
            model.Units = uf switch { 1 => LengthUnit.Inch, 4 => LengthUnit.Foot, 6 => LengthUnit.Meter, 10 => LengthUnit.Centimeter, 11 => LengthUnit.Micrometer, _ => LengthUnit.Millimeter };

        var de = new Dictionary<int, (int Type, int PStart, int PCount, int Form, int Xform, int Status)>();
        for (int i = 0; i + 1 < dLines.Count; i += 2)
        {
            string a = dLines[i], b = dLines[i + 1];
            int F(string l, int k) => int.TryParse(l.Substring(k * 8, 8).Trim(), out var v) ? v : 0;
            de[i + 1] = (F(a, 0), F(a, 1), F(b, 3), F(b, 4), F(a, 6), F(a, 8));
        }
        var pdata = new Dictionary<int, List<string>>();
        foreach (var (key, d) in de)
        {
            var sb = new StringBuilder();
            for (int k = d.PStart - 1; k < d.PStart - 1 + d.PCount && k < pLines.Count; k++) sb.Append(pLines[k][..64]);
            pdata[key] = SplitParams(sb.ToString(), pd, rd);
        }
        Mat4 Xf(int deIdx, int depth = 0)
        {
            if (deIdx == 0 || depth > 8 || !de.TryGetValue(deIdx, out var d) || d.Type != 124) return Mat4.Identity;
            var p = pdata[deIdx].Skip(1).Select(s => TryD(s, out var v) ? v : 0).ToArray();
            if (p.Length < 12) return Mat4.Identity;
            var m = new Mat4(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9], p[10], p[11], 0, 0, 0, 1);
            return Xf(d.Xform, depth + 1) * m;
        }
        double V(List<string> p, int i) => i < p.Count && TryD(p[i], out var v) ? v : 0;

        var subordinate = new HashSet<int>();
        foreach (var (k, d) in de) if (d.Type == 102) foreach (var s in pdata[k].Skip(2).Select(x => int.TryParse(x.Trim(), out var v) ? v : 0)) subordinate.Add(s);

        foreach (var (k, d) in de)
        {
            if (subordinate.Contains(k) && d.Type != 102) continue;
            var pts = Curve(k, d.Type, d.Form);
            if (pts is null) continue;
            var tf = Xf(d.Xform);
            var tp = pts.Select(tf.TransformPoint).ToList();
            if (d.Type == 116) { model.Drawing.Add(new PointEntity(tp[0].XY)); continue; }
            if (tp.All(p => Math.Abs(p.Z) < 1e-9))
            {
                if (tp.Count == 2) model.Drawing.Add(new LineEntity(tp[0].XY, tp[1].XY));
                else model.Drawing.Add(new PolylineEntity(tp.Select(p => p.XY), tp[0].ApproxEquals(tp[^1], 1e-9)));
            }
            else model.Wires.Add(new Polyline3(tp, tp.Count > 2 && tp[0].ApproxEquals(tp[^1], 1e-9)));
        }
        if (de.Values.Any(d => d.Type is 128 or 143 or 144 or 186))
            model.Warnings.Add("IGES surfaces/solids (128/143/144/186) are not tessellated yet; only curves were imported. Use STEP for solids.");
        return model;

        List<Vec3>? Curve(int k, int type, int form)
        {
            var p = pdata[k];
            switch (type)
            {
                case 110: return [new(V(p, 1), V(p, 2), V(p, 3)), new(V(p, 4), V(p, 5), V(p, 6))];
                case 116: return [new(V(p, 1), V(p, 2), V(p, 3))];
                case 100:
                    {
                        double z = V(p, 1); var c = new Vec2(V(p, 2), V(p, 3)); var s = new Vec2(V(p, 4), V(p, 5)); var e = new Vec2(V(p, 6), V(p, 7));
                        double r = Vec2.Distance(c, s), a0 = (s - c).Angle, a1 = (e - c).Angle;
                        if (a1 <= a0 + 1e-12) a1 += 2 * Math.PI;
                        int n = CurveMath.ArcSegments(r, a1 - a0, options.ChordTolerance);
                        return [.. Enumerable.Range(0, n + 1).Select(i => (c + Vec2.FromAngle(a0 + (a1 - a0) * i / n, r)).ToVec3(z))];
                    }
                case 106:
                    {
                        int ip = (int)V(p, 1), n = (int)V(p, 2);
                        var r = new List<Vec3>(n);
                        if (ip == 1) { double z = V(p, 3); for (int i = 0; i < n; i++) r.Add(new(V(p, 4 + 2 * i), V(p, 5 + 2 * i), z)); }
                        else { int stride = ip == 3 ? 6 : 3; for (int i = 0; i < n; i++) r.Add(new(V(p, 3 + stride * i), V(p, 4 + stride * i), V(p, 5 + stride * i))); }
                        return r.Count >= 1 ? r : null;
                    }
                case 102:
                    {
                        int n = (int)V(p, 1);
                        var r = new List<Vec3>();
                        for (int i = 0; i < n; i++)
                        {
                            int sub = (int)V(p, 2 + i);
                            if (!de.TryGetValue(sub, out var sd)) continue;
                            var seg = Curve(sub, sd.Type, sd.Form);
                            if (seg is null) continue;
                            var tf = Xf(sd.Xform);
                            foreach (var q in seg.Select(tf.TransformPoint)) if (r.Count == 0 || !r[^1].ApproxEquals(q, 1e-9)) r.Add(q);
                        }
                        return r.Count >= 2 ? r : null;
                    }
                case 126:
                    {
                        int K = (int)V(p, 1), M = (int)V(p, 2);
                        int nk = K + M + 2;
                        var knots = Enumerable.Range(0, nk).Select(i => V(p, 7 + i)).ToList();
                        var wts = Enumerable.Range(0, K + 1).Select(i => V(p, 7 + nk + i)).ToList();
                        int cpStart = 7 + nk + K + 1;
                        var cps = Enumerable.Range(0, K + 1).Select(i => new Vec3(V(p, cpStart + 3 * i), V(p, cpStart + 3 * i + 1), V(p, cpStart + 3 * i + 2))).ToList();
                        double k0 = knots[0], k1 = knots[^1];
                        try
                        {
                            var nc = new NurbsCurve(M, cps, wts, knots.Select(x => (x - k0) / Math.Max(k1 - k0, 1e-12)).ToList());
                            return [.. nc.Tessellate(options.ChordTolerance)];
                        }
                        catch { return cps; }
                    }
                default: return null;
            }
        }
    }

    private static List<string> SplitParams(string s, char pd, char rd)
    {
        var r = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            // Hollerith strings nH...
            if (char.IsDigit(c) && sb.Length >= 0)
            {
                int j = i; while (j < s.Length && char.IsDigit(s[j])) j++;
                if (j < s.Length && s[j] == 'H' && sb.ToString().Trim().Length == 0)
                {
                    int n = int.Parse(s[i..j]);
                    r.Add(s.Substring(j + 1, Math.Min(n, s.Length - j - 1)));
                    i = j + n;
                    while (i + 1 < s.Length && s[i + 1] != pd && s[i + 1] != rd) i++;
                    if (i + 1 < s.Length && s[i + 1] == rd) { i++; break; }
                    i++; sb.Clear();
                    continue;
                }
            }
            if (c == pd) { r.Add(sb.ToString()); sb.Clear(); }
            else if (c == rd) { r.Add(sb.ToString()); sb.Clear(); break; }
            else sb.Append(c);
        }
        if (sb.Length > 0) r.Add(sb.ToString());
        return r;
    }
}

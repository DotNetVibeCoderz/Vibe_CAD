using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Gravicode.CAD.Geometry;
using static Gravicode.CAD.IO.CadIO;

namespace Gravicode.CAD.IO;

/// <summary>
/// SVG. Export writes the model's 2D drawing, or — for 3D-only models — a standard engineering sheet.
/// Import reads line, rect, circle, ellipse, polyline, polygon and path (M L H V C S Q T A Z) with nested transforms.
/// SVG's y-down coordinates are flipped to CAD y-up.
/// </summary>
public sealed partial class SvgFormat : ICadImporter, ICadExporter
{
    public string FormatName => "SVG";
    public IReadOnlyList<string> Extensions => ["svg"];

    // ======================= export =======================
    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        List<DrawingEntity> ents; double w, h, x0, y0;
        if (model.Drawing.Count > 0)
        {
            ents = model.Drawing;
            var bb = Extents(ents);
            double pad = Math.Max(bb.Size.X, bb.Size.Y) * 0.05 + 1;
            x0 = bb.Min.X - pad; y0 = bb.Min.Y - pad; w = bb.Size.X + 2 * pad; h = bb.Size.Y + 2 * pad;
        }
        else
        {
            var sheet = StandardDrawing.Create(model, options.Title);
            ents = sheet.Entities; x0 = 0; y0 = 0; w = sheet.Width; h = sheet.Height;
        }
        Write(stream, ents, x0, y0, w, h, model.Name);
    }

    public static void Write(Stream stream, IEnumerable<DrawingEntity> ents, double x0, double y0, double w, double h, string title)
    {
        var sb = new StringBuilder();
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string X(double x) => N(x - x0);
        string Y(double y) => N(h - (y - y0));
        double stroke = Math.Max(w, h) / 1200;
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{N(w)}mm" height="{N(h)}mm" viewBox="0 0 {N(w)} {N(h)}">""");
        sb.AppendLine($"  <title>{System.Security.SecurityElement.Escape(title)}</title>");
        sb.AppendLine($"  <desc>{GravicodeCad.ProductName} {GravicodeCad.Version} — {GravicodeCad.Credits}</desc>");
        sb.AppendLine($"""  <rect width="100%" height="100%" fill="white"/>""");
        sb.AppendLine($"""  <g fill="none" stroke="#1b1f24" stroke-width="{N(stroke * 1.6)}" stroke-linecap="round" stroke-linejoin="round">""");
        foreach (var e in ents)
        {
            var color = e.Color?.ToHex();
            string style = e.Layer switch
            {
                "HIDDEN" => $" stroke=\"#8a94a3\" stroke-width=\"{N(stroke)}\" stroke-dasharray=\"{N(stroke * 6)} {N(stroke * 4)}\"",
                "DIM" => $" stroke=\"#2f6fde\" stroke-width=\"{N(stroke)}\"",
                "BORDER" or "TITLE" => $" stroke-width=\"{N(stroke * 2.2)}\"",
                "CONSTRUCTION" => $" stroke=\"#9aa7b4\" stroke-dasharray=\"{N(stroke * 8)} {N(stroke * 3)}\"",
                _ => ""
            };
            if (color != null) style += $" stroke=\"{color}\"";
            switch (e)
            {
                case LineEntity l: sb.AppendLine($"    <line x1=\"{X(l.A.X)}\" y1=\"{Y(l.A.Y)}\" x2=\"{X(l.B.X)}\" y2=\"{Y(l.B.Y)}\"{style}/>"); break;
                case CircleEntity c: sb.AppendLine($"    <circle cx=\"{X(c.Center.X)}\" cy=\"{Y(c.Center.Y)}\" r=\"{N(c.Radius)}\"{style}/>"); break;
                case ArcEntity a:
                    {
                        var s = a.StartPoint; var en = a.EndPoint;
                        int large = a.Sweep > Math.PI ? 1 : 0;
                        // CCW in y-up == sweep-flag 0 in y-down
                        sb.AppendLine($"    <path d=\"M {X(s.X)} {Y(s.Y)} A {N(a.Radius)} {N(a.Radius)} 0 {large} 0 {X(en.X)} {Y(en.Y)}\"{style}/>");
                        break;
                    }
                case PolylineEntity p when p.Points.Count > 0:
                    sb.AppendLine($"    <{(p.Closed ? "polygon" : "polyline")} points=\"{string.Join(' ', p.Points.Select(q => $"{X(q.X)},{Y(q.Y)}"))}\"{style}/>"); break;
                case TextEntity t:
                    {
                        var fill = e.Layer == "DIM" ? "#2f6fde" : "#1b1f24";
                        var lines = t.Text.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                            sb.AppendLine($"    <text x=\"{X(t.Position.X)}\" y=\"{Y(t.Position.Y - i * t.Height * 1.3)}\" font-family=\"Inter, Segoe UI, Arial, sans-serif\" font-size=\"{N(t.Height)}\" fill=\"{fill}\" stroke=\"none\">{System.Security.SecurityElement.Escape(lines[i])}</text>");
                        break;
                    }
                case PointEntity pt: sb.AppendLine($"    <circle cx=\"{X(pt.Position.X)}\" cy=\"{Y(pt.Position.Y)}\" r=\"{N(stroke * 2)}\" fill=\"#1b1f24\"/>"); break;
            }
        }
        sb.AppendLine("  </g>");
        sb.AppendLine("</svg>");
        var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
        stream.Write(bytes);
    }

    internal static BoundingBox Extents(IEnumerable<DrawingEntity> ents)
    {
        var bb = BoundingBox.Empty;
        foreach (var e in ents) { var (a, b) = e.Extents(); bb = bb.Include(a.ToVec3()).Include(b.ToVec3()); }
        return bb.IsEmpty ? new BoundingBox(Vec3.Zero, new Vec3(100, 100, 0)) : bb;
    }

    // ======================= import =======================
    public CadModel Import(Stream stream, ImportOptions options)
    {
        var doc = XDocument.Load(stream);
        var model = new CadModel();
        var root = doc.Root!;
        double tol = options.ChordTolerance;
        Walk(root, Mat4.Scale(1, -1, 1), "0"); // flip Y
        if (root.Element(root.Name.Namespace + "title") is { } t && !string.IsNullOrWhiteSpace(t.Value)) model.Name = t.Value;
        return model;

        void Walk(XElement el, Mat4 parent, string layer)
        {
            var tf = parent * ParseTransform((string?)el.Attribute("transform"));
            var id = (string?)el.Attribute("id");
            if (el.Name.LocalName == "g" && id != null) layer = id;
            CadColor? color = ParseColor((string?)el.Attribute("stroke")) ?? ParseColor(StyleProp((string?)el.Attribute("style"), "stroke"));
            void AddPoly(IEnumerable<Vec2> pts, bool closed)
            {
                var list = pts.Select(p => { var q = tf.TransformPoint(p.ToVec3()); return new Vec2(q.X, q.Y); }).ToList();
                if (list.Count >= 2) model.Drawing.Add(new PolylineEntity(list, closed) { Layer = layer, Color = color });
            }
            double A(string n) => el.Attribute(n) is { } a && TryD(Regex.Replace(a.Value, "[a-z%]+$", ""), out var v) ? v : 0;
            switch (el.Name.LocalName)
            {
                case "line":
                    {
                        var a = tf.TransformPoint(new Vec3(A("x1"), A("y1"), 0)); var b = tf.TransformPoint(new Vec3(A("x2"), A("y2"), 0));
                        model.Drawing.Add(new LineEntity(a.XY, b.XY) { Layer = layer, Color = color });
                        break;
                    }
                case "rect":
                    {
                        double x = A("x"), y = A("y"), w = A("width"), h = A("height");
                        AddPoly([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)], true);
                        break;
                    }
                case "circle":
                    {
                        var c = tf.TransformPoint(new Vec3(A("cx"), A("cy"), 0));
                        var r = Vec3.Distance(tf.TransformPoint(new Vec3(A("cx") + A("r"), A("cy"), 0)), c);
                        model.Drawing.Add(new CircleEntity(c.XY, r) { Layer = layer, Color = color });
                        break;
                    }
                case "ellipse":
                    {
                        double cx = A("cx"), cy = A("cy"), rx = A("rx"), ry = A("ry");
                        AddPoly(Enumerable.Range(0, 96).Select(i => new Vec2(cx + rx * Math.Cos(2 * Math.PI * i / 96), cy + ry * Math.Sin(2 * Math.PI * i / 96))), true);
                        break;
                    }
                case "polyline":
                case "polygon":
                    {
                        var nums = Numbers((string?)el.Attribute("points") ?? "");
                        AddPoly(Enumerable.Range(0, nums.Count / 2).Select(i => new Vec2(nums[2 * i], nums[2 * i + 1])), el.Name.LocalName == "polygon");
                        break;
                    }
                case "path":
                    foreach (var (pts, closed) in ParsePath((string?)el.Attribute("d") ?? "", tol)) AddPoly(pts, closed);
                    break;
            }
            foreach (var c in el.Elements()) Walk(c, tf, layer);
        }
    }

    private static string? StyleProp(string? style, string name)
    {
        if (style is null) return null;
        foreach (var part in style.Split(';'))
        {
            var kv = part.Split(':', 2);
            if (kv.Length == 2 && kv[0].Trim() == name) return kv[1].Trim();
        }
        return null;
    }

    private static CadColor? ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s == "none" || !s.StartsWith('#')) return null;
        try { return CadColor.FromHex(s); } catch { return null; }
    }

    [GeneratedRegex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberRegex();
    [GeneratedRegex(@"([MmLlHhVvCcSsQqTtAaZz])|([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)")]
    private static partial Regex PathTokenRegex();
    [GeneratedRegex(@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)")]
    private static partial Regex TransformRegex();

    private static List<double> Numbers(string s) => [.. NumberRegex().Matches(s).Select(m => D(m.Value))];

    private static Mat4 ParseTransform(string? s)
    {
        var m = Mat4.Identity;
        if (string.IsNullOrWhiteSpace(s)) return m;
        foreach (Match t in TransformRegex().Matches(s))
        {
            var a = Numbers(t.Groups[2].Value);
            Mat4 op = t.Groups[1].Value switch
            {
                "matrix" when a.Count >= 6 => new Mat4(a[0], a[2], 0, a[4], a[1], a[3], 0, a[5], 0, 0, 1, 0, 0, 0, 0, 1),
                "translate" => Mat4.Translate(a.ElementAtOrDefault(0), a.ElementAtOrDefault(1), 0),
                "scale" => Mat4.Scale(a[0], a.Count > 1 ? a[1] : a[0], 1),
                "rotate" => a.Count >= 3
                    ? Mat4.Translate(a[1], a[2], 0) * Mat4.RotateZ(Units.Deg(a[0])) * Mat4.Translate(-a[1], -a[2], 0)
                    : Mat4.RotateZ(Units.Deg(a[0])),
                "skewX" => new Mat4(1, Math.Tan(Units.Deg(a[0])), 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
                "skewY" => new Mat4(1, 0, 0, 0, Math.Tan(Units.Deg(a[0])), 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
                _ => Mat4.Identity
            };
            m = m * op;
        }
        return m;
    }

    /// <summary>Flattens an SVG path into polylines.</summary>
    public static List<(List<Vec2> Points, bool Closed)> ParsePath(string d, double tol = 0.05)
    {
        var result = new List<(List<Vec2>, bool)>();
        var tokens = PathTokenRegex().Matches(d).Select(m => m.Value).ToList();
        int i = 0; char cmd = 'M';
        var cur = Vec2.Zero; var start = Vec2.Zero; Vec2? lastCtrl = null; char lastCmd = ' ';
        List<Vec2>? poly = null;
        bool IsCmd(int k) => k < tokens.Count && char.IsLetter(tokens[k][0]);
        double Num() => D(tokens[i++]);

        while (i < tokens.Count)
        {
            if (IsCmd(i)) cmd = tokens[i++][0];
            bool rel = char.IsLower(cmd);
            Vec2 Pt() { var x = Num(); var y = Num(); return rel ? cur + new Vec2(x, y) : new Vec2(x, y); }
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    if (poly is { Count: > 1 }) result.Add((poly, false));
                    cur = Pt(); start = cur; poly = [cur];
                    cmd = rel ? 'l' : 'L'; // subsequent pairs are lineto
                    break;
                case 'L': cur = Pt(); (poly ??= [cur]).Add(cur); break;
                case 'H': { var x = Num(); cur = new Vec2(rel ? cur.X + x : x, cur.Y); (poly ??= [cur]).Add(cur); break; }
                case 'V': { var y = Num(); cur = new Vec2(cur.X, rel ? cur.Y + y : y); (poly ??= [cur]).Add(cur); break; }
                case 'C':
                    {
                        var c1 = Pt(); var c2 = Pt(); var e = Pt();
                        AddCurve(new Bezier2([cur, c1, c2, e])); lastCtrl = c2; cur = e; lastCmd = 'C'; continue;
                    }
                case 'S':
                    {
                        var c1 = lastCmd is 'C' or 'S' && lastCtrl is { } lc ? cur * 2 - lc : cur;
                        var c2 = Pt(); var e = Pt();
                        AddCurve(new Bezier2([cur, c1, c2, e])); lastCtrl = c2; cur = e; lastCmd = 'S'; continue;
                    }
                case 'Q':
                    {
                        var c = Pt(); var e = Pt();
                        AddCurve(new Bezier2([cur, c, e])); lastCtrl = c; cur = e; lastCmd = 'Q'; continue;
                    }
                case 'T':
                    {
                        var c = lastCmd is 'Q' or 'T' && lastCtrl is { } lc ? cur * 2 - lc : cur;
                        var e = Pt();
                        AddCurve(new Bezier2([cur, c, e])); lastCtrl = c; cur = e; lastCmd = 'T'; continue;
                    }
                case 'A':
                    {
                        double rx = Num(), ry = Num(), rot = Num(); bool large = Num() != 0, sweep = Num() != 0;
                        var e = Pt();
                        foreach (var p in ArcPoints(cur, e, rx, ry, rot, large, sweep, tol)) (poly ??= [cur]).Add(p);
                        cur = e; break;
                    }
                case 'Z':
                    if (poly is { Count: > 1 }) result.Add((poly, true));
                    poly = null; cur = start;
                    break;
                default: i++; break;
            }
            lastCmd = char.ToUpperInvariant(cmd);
        }
        if (poly is { Count: > 1 }) result.Add((poly, false));
        return result;

        void AddCurve(Bezier2 b)
        {
            poly ??= [cur];
            foreach (var p in b.Tessellate(tol).Skip(1)) poly.Add(p);
        }
    }

    /// <summary>SVG elliptical arc (endpoint parameterisation, F.6.5 of the SVG spec).</summary>
    private static IEnumerable<Vec2> ArcPoints(Vec2 p0, Vec2 p1, double rx, double ry, double phiDeg, bool large, bool sweep, double tol)
    {
        if (rx == 0 || ry == 0) { yield return p1; yield break; }
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        double phi = Units.Deg(phiDeg), cs = Math.Cos(phi), sn = Math.Sin(phi);
        var d = (p0 - p1) / 2;
        double x1 = cs * d.X + sn * d.Y, y1 = -sn * d.X + cs * d.Y;
        double lam = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lam > 1) { rx *= Math.Sqrt(lam); ry *= Math.Sqrt(lam); }
        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
        double cxp = coef * rx * y1 / ry, cyp = -coef * ry * x1 / rx;
        var mid = (p0 + p1) / 2;
        var c = new Vec2(cs * cxp - sn * cyp + mid.X, sn * cxp + cs * cyp + mid.Y);
        double Ang(double ux, double uy, double vx, double vy) => Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        double t1 = Ang(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry);
        double dt = Ang((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);
        if (!sweep && dt > 0) dt -= 2 * Math.PI; else if (sweep && dt < 0) dt += 2 * Math.PI;
        int n = CurveMath.ArcSegments(Math.Max(rx, ry), Math.Abs(dt), tol);
        for (int i = 1; i <= n; i++)
        {
            double t = t1 + dt * i / n;
            double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            yield return new Vec2(cs * ex - sn * ey + c.X, sn * ex + cs * ey + c.Y);
        }
    }
}

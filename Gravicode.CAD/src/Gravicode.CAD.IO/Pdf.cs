using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Gravicode.CAD.IO;

/// <summary>
/// Vector PDF drawing export (PDF 1.4, Helvetica, Flate-compressed content). Writes the model's 2D drawing,
/// or — for 3D models — a standard A3 engineering sheet with orthographic and isometric views.
/// </summary>
public sealed class PdfExporter : ICadExporter
{
    public string FormatName => "PDF drawing";
    public IReadOnlyList<string> Extensions => ["pdf"];

    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        List<DrawingEntity> ents; double w, h, x0 = 0, y0 = 0;
        if (model.Drawing.Count > 0)
        {
            ents = model.Drawing;
            var bb = SvgFormat.Extents(ents);
            double pad = Math.Max(bb.Size.X, bb.Size.Y) * 0.05 + 1;
            x0 = bb.Min.X - pad; y0 = bb.Min.Y - pad; w = bb.Size.X + 2 * pad; h = bb.Size.Y + 2 * pad;
        }
        else
        {
            var sheet = StandardDrawing.Create(model, options.Title);
            ents = sheet.Entities; w = sheet.Width; h = sheet.Height;
        }
        Write(stream, ents, x0, y0, w, h, options.Title ?? model.Name, options.Author);
    }

    public static void Write(Stream stream, IEnumerable<DrawingEntity> ents, double x0, double y0, double w, double h, string title, string author)
    {
        const double mmToPt = 72 / 25.4;
        // 1 drawing unit = 1 mm on paper; huge drawings are scaled to the 14400 pt PDF page limit
        double k = mmToPt * Math.Min(1, 14400 / (Math.Max(w, h) * mmToPt));
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string P(Vec2 p) => $"{N((p.X - x0) * k)} {N((p.Y - y0) * k)}";

        var cs = new StringBuilder();
        cs.AppendLine("1 j 1 J");
        string? lastLayer = null;
        foreach (var e in ents)
        {
            if (e.Layer != lastLayer)
            {
                lastLayer = e.Layer;
                (string rgb, double lw, string dash) = e.Layer switch
                {
                    "HIDDEN" => ("0.54 0.58 0.64", 0.25, "[2.5 1.8] 0 d"),
                    "DIM" => ("0.18 0.43 0.87", 0.25, "[] 0 d"),
                    "BORDER" or "TITLE" => ("0.1 0.12 0.14", 0.9, "[] 0 d"),
                    _ => ("0.1 0.12 0.14", 0.55, "[] 0 d")
                };
                cs.AppendLine($"{rgb} RG {rgb} rg {N(lw)} w {dash}");
            }
            switch (e)
            {
                case LineEntity l: cs.AppendLine($"{P(l.A)} m {P(l.B)} l S"); break;
                case TextEntity t:
                    {
                        var lines = t.Text.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                            cs.AppendLine($"BT /F1 {N(t.Height * k * 1.05)} Tf {P(t.Position - new Vec2(0, i * t.Height * 1.3))} Td ({Escape(lines[i])}) Tj ET");
                        break;
                    }
                case PointEntity pt: cs.AppendLine($"{P(pt.Position)} m {P(pt.Position + new Vec2(0.01, 0))} l S"); break;
                default:
                    {
                        var pts = e.Tessellate(0.05);
                        if (pts.Count < 2) break;
                        cs.Append($"{P(pts[0])} m");
                        for (int i = 1; i < pts.Count; i++) cs.Append($" {P(pts[i])} l");
                        cs.AppendLine(e is PolylineEntity { Closed: true } ? " s" : " S");
                        break;
                    }
            }
        }

        byte[] content = Deflate(Encoding.Latin1.GetBytes(cs.ToString()));
        var objs = new List<byte[]>();
        void Obj(string s) => objs.Add(Encoding.Latin1.GetBytes(s));
        double pw = w * k, ph = h * k;
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pw)} {N(ph)}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        objs.Add([.. Encoding.Latin1.GetBytes($"<< /Length {content.Length} /Filter /FlateDecode >>\nstream\n"), .. content, .. Encoding.Latin1.GetBytes("\nendstream")]);
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj($"<< /Title ({Escape(title)}) /Author ({Escape(author)}) /Creator ({Escape(GravicodeCad.ProductName + " " + GravicodeCad.Version + " - " + GravicodeCad.Credits)}) /Producer (Gravicode.CAD PDF) /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>");

        using var ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long>();
        for (int i = 0; i < objs.Count; i++)
        {
            offsets.Add(ms.Position);
            W($"{i + 1} 0 obj\n"); ms.Write(objs[i]); W("\nendobj\n");
        }
        long xref = ms.Position;
        W($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R /Info {objs.Count} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        ms.Position = 0;
        ms.CopyTo(stream);
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append(' ');
            else if (c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }
}

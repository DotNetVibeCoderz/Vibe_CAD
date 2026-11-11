using System.Globalization;
using Gravicode.CAD.IO;

namespace Gravicode.CAD.Studio.IO;

/// <summary>What to plot and how.</summary>
/// <param name="Paper">ISO sheet: A4, A3, A2, A1 or A0.</param>
/// <param name="Scale">Model units → paper, as the denominator of "1:N" in millimetres (50 = 1:50). Null fits the area to the sheet.</param>
/// <param name="Window">Model-space area to plot; null = drawing extents.</param>
/// <param name="Format">"pdf" or "svg".</param>
public sealed record PlotSettings(string Paper = "A3", bool Landscape = true, double? Scale = null, (Vec2 Min, Vec2 Max)? Window = null,
    bool TitleBlock = true, string Format = "pdf", bool IncludeSolids = true);

/// <summary>Result of a plot: the file and the scale that was used.</summary>
public sealed record PlotResult(byte[] Data, string FileName, string Mime, string Paper, double ScaleDenominator);

/// <summary>
/// Plots model space onto an ISO sheet (PDF/SVG): fits or scales the drawing, adds a border and a title block.
/// Paper coordinates are millimetres.
/// </summary>
public static class Plotter
{
    /// <summary>Portrait sheet sizes in mm.</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> Papers = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
    {
        ["A4"] = (210, 297), ["A3"] = (297, 420), ["A2"] = (420, 594), ["A1"] = (594, 841), ["A0"] = (841, 1189)
    };

    private const double Margin = 10, TitleHeight = 28;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static PlotResult Plot(StudioDocument doc, PlotSettings s)
    {
        var (pw, ph) = Papers.TryGetValue(s.Paper, out var size) ? size : Papers["A3"];
        double W = s.Landscape ? Math.Max(pw, ph) : Math.Min(pw, ph), H = s.Landscape ? Math.Min(pw, ph) : Math.Max(pw, ph);

        // ---- model objects as primitives (dimension line work goes on the "DIM" pen)
        var model = new List<DrawingEntity>();
        foreach (var e in doc.VisibleEntities)
            foreach (var (g, _, _) in Geometry2D.Primitives(e.Geometry))
            {
                var c = g.Transformed(p => p);
                c.Layer = e.Geometry is DimensionEntity ? "DIM" : "0";
                model.Add(c);
            }
        if (s.IncludeSolids)
            foreach (var so in doc.VisibleSolids)
                foreach (var (a, b) in so.PlanOutline) model.Add(new LineEntity(a, b));

        if (s.Window is { } win)
        {
            var lo = new Vec2(Math.Min(win.Min.X, win.Max.X), Math.Min(win.Min.Y, win.Max.Y));
            var hi = new Vec2(Math.Max(win.Min.X, win.Max.X), Math.Max(win.Min.Y, win.Max.Y));
            model = [.. model.Where(e => { var (a, b) = e.Extents(); return !(b.X < lo.X || a.X > hi.X || b.Y < lo.Y || a.Y > hi.Y); })];
        }
        if (model.Count == 0) throw new InvalidOperationException("Nothing to plot in that area.");

        var (amin, amax) = s.Window is { } w2
            ? (new Vec2(Math.Min(w2.Min.X, w2.Max.X), Math.Min(w2.Min.Y, w2.Max.Y)), new Vec2(Math.Max(w2.Min.X, w2.Max.X), Math.Max(w2.Min.Y, w2.Max.Y)))
            : Extents(model);

        // ---- drawable area inside the border, above the title block
        double dx0 = Margin + 5, dy0 = Margin + 5 + (s.TitleBlock ? TitleHeight : 0), dx1 = W - Margin - 5, dy1 = H - Margin - 5;
        double aw = Math.Max(amax.X - amin.X, 1e-9), ah = Math.Max(amax.Y - amin.Y, 1e-9);
        double unitMm = doc.Units.ToMillimeters();
        double factor = s.Scale is { } denom && denom > 0 ? unitMm / denom : Math.Min((dx1 - dx0) / aw, (dy1 - dy0) / ah);
        double scaleDenom = unitMm / factor;
        var ac = (amin + amax) / 2; var dc = new Vec2((dx0 + dx1) / 2, (dy0 + dy1) / 2);
        Vec2 Map(Vec2 p) => dc + (p - ac) * factor;

        var sheet = new List<DrawingEntity>();
        foreach (var e in model)
        {
            DrawingEntity g = e is TextEntity t ? new TextEntity(Map(t.Position), t.Text, t.Height * factor) : e.Transformed(Map);
            g.Layer = e.Layer;
            sheet.Add(g);
        }

        // ---- border + title block
        AddRect(sheet, Margin, Margin, W - Margin, H - Margin, "BORDER");
        if (s.TitleBlock) TitleBlock(sheet, doc, W, s.Paper.ToUpperInvariant(), scaleDenom);

        var fmt = s.Format.Equals("svg", StringComparison.OrdinalIgnoreCase) ? "svg" : "pdf";
        using var ms = new MemoryStream();
        if (fmt == "svg") SvgFormat.Write(ms, sheet, 0, 0, W, H, doc.Name);
        else PdfExporter.Write(ms, sheet, 0, 0, W, H, doc.Name, "Gravicode CAD Studio");
        var safe = string.Concat(doc.Name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')).Trim('-');
        return new PlotResult(ms.ToArray(), $"{(safe.Length > 0 ? safe : "drawing")}-{s.Paper.ToUpperInvariant()}.{fmt}",
            fmt == "svg" ? "image/svg+xml" : "application/pdf", s.Paper.ToUpperInvariant(), scaleDenom);
    }

    /// <summary>"1:50" → 50, "2:1" → 0.5, "50" → 50, "Fit"/"" → null.</summary>
    public static double? ParseScale(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Equals("fit", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = text.Split(':');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, Inv, out var a) && double.TryParse(parts[1], NumberStyles.Float, Inv, out var b) && a > 0 && b > 0)
            return b / a;
        return double.TryParse(text, NumberStyles.Float, Inv, out var n) && n > 0 ? n : null;
    }

    public static string FormatScale(double denom) =>
        denom >= 1 ? $"1:{Round(denom)}" : $"{Round(1 / denom)}:1";

    private static string Round(double v) => (v >= 10 ? Math.Round(v) : Math.Round(v, 2)).ToString("0.##", Inv);

    private static (Vec2, Vec2) Extents(IEnumerable<DrawingEntity> ents)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var e in ents)
        {
            var (a, b) = e.Extents();
            x0 = Math.Min(x0, a.X); y0 = Math.Min(y0, a.Y); x1 = Math.Max(x1, b.X); y1 = Math.Max(y1, b.Y);
        }
        return (new Vec2(x0, y0), new Vec2(x1, y1));
    }

    private static void AddRect(List<DrawingEntity> list, double x0, double y0, double x1, double y1, string layer) =>
        list.Add(new PolylineEntity([new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)], true) { Layer = layer });

    private static void TitleBlock(List<DrawingEntity> list, StudioDocument doc, double W, string paper, double scaleDenom)
    {
        double x1 = W - Margin, y0 = Margin, w = Math.Min(180, W - 2 * Margin), x0 = x1 - w, y1 = y0 + TitleHeight, mid = y0 + TitleHeight * 0.5;
        AddRect(list, x0, y0, x1, y1, "BORDER");
        // top row: title across; bottom row: scale / sheet / units / date
        list.Add(new LineEntity(new(x0, mid), new(x1, mid)) { Layer = "TITLE" });
        double[] cols = [x0, x0 + w * 0.25, x0 + w * 0.45, x0 + w * 0.62, x1];
        for (int i = 1; i < cols.Length - 1; i++) list.Add(new LineEntity(new(cols[i], y0), new(cols[i], mid)) { Layer = "TITLE" });

        void Label(double x, double y, string text) => list.Add(new TextEntity(new(x + 2, y), text, 1.8) { Layer = "0" });
        void Value(double x, double y, string text, double h = 3.2) => list.Add(new TextEntity(new(x + 2, y), text, h) { Layer = "0" });

        Label(x0, y1 - 3.5, "TITLE");
        Value(x0, mid + 3, Trim(doc.Name, 48), 5);
        Label(x1 - 62, y1 - 3.5, "Gravicode CAD Studio");
        string[] labels = ["SCALE", "SHEET", "UNITS", "DATE"];
        string[] values = [FormatScale(scaleDenom), paper, doc.Units.Symbol(), DateTime.Now.ToString("yyyy-MM-dd", Inv)];
        for (int i = 0; i < 4; i++) { Label(cols[i], mid - 3.5, labels[i]); Value(cols[i], y0 + 3, values[i]); }
        list.Add(new TextEntity(new(x0 + 2, y0 - 4.5), GravicodeCad.Credits, 1.8) { Layer = "0" });
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";   // PDF fonts are Latin-1
}

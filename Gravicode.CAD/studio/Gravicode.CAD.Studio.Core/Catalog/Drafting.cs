using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Studio.Catalog;

/// <summary>Drafting helpers used to build templates and samples.</summary>
internal static class Draft
{
    public static void Line(StudioDocument d, string layer, double x0, double y0, double x1, double y1, string? color = null) =>
        d.Add(new LineEntity(new(x0, y0), new(x1, y1)), layer, color);

    public static void Rect(StudioDocument d, string layer, double x, double y, double w, double h, string? color = null) =>
        d.Add(new PolylineEntity([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)], true), layer, color);

    public static void Poly(StudioDocument d, string layer, bool closed, params (double X, double Y)[] pts) =>
        d.Add(new PolylineEntity(pts.Select(p => new Vec2(p.X, p.Y)), closed), layer);

    public static void Circle(StudioDocument d, string layer, double x, double y, double r) => d.Add(new CircleEntity(new(x, y), r), layer);
    public static void Arc(StudioDocument d, string layer, double x, double y, double r, double a0Deg, double a1Deg) =>
        d.Add(new ArcEntity(new(x, y), r, Units.Deg(a0Deg), Units.Deg(a1Deg)), layer);
    public static void Text(StudioDocument d, string layer, double x, double y, string text, double h) => d.Add(new TextEntity(new(x, y), text, h), layer);
    public static void Dim(StudioDocument d, double x0, double y0, double x1, double y1, double offset, double h = 3.5) =>
        d.Add(new DimensionEntity(new(x0, y0), new(x1, y1), offset, h), "Dimensions");

    /// <summary>Drawing sheet frame with a title block (ISO-style) placed at (x, y).</summary>
    public static void Sheet(StudioDocument d, double x, double y, double w, double h, string title, string scale, double textScale = 1)
    {
        d.EnsureLayer("Sheet", "#5F7FA8");
        Rect(d, "Sheet", x, y, w, h);
        double m = 10 * textScale;
        Rect(d, "Sheet", x + m, y + m, w - 2 * m, h - 2 * m);
        double tw = 180 * textScale, th = 32 * textScale, tx = x + w - m - tw, ty = y + m;
        Rect(d, "Sheet", tx, ty, tw, th);
        Line(d, "Sheet", tx, ty + th * 0.55, tx + tw, ty + th * 0.55);
        Line(d, "Sheet", tx + tw * 0.65, ty, tx + tw * 0.65, ty + th * 0.55);
        Text(d, "Sheet", tx + 4 * textScale, ty + th * 0.68, title.ToUpperInvariant(), 5 * textScale);
        Text(d, "Sheet", tx + 4 * textScale, ty + th * 0.30, "Gravicode CAD Studio", 2.8 * textScale);
        Text(d, "Sheet", tx + 4 * textScale, ty + th * 0.08, GravicodeCad.CreditsId, 2.2 * textScale);
        Text(d, "Sheet", tx + tw * 0.68, ty + th * 0.30, $"SCALE {scale}", 2.8 * textScale);
        Text(d, "Sheet", tx + tw * 0.68, ty + th * 0.08, DateTime.Today.ToString("yyyy-MM-dd"), 2.4 * textScale);
    }

    public static StudioDocument New(string name, string category, string description, params (string Name, string Color)[] layers)
    {
        var d = new StudioDocument { Name = name, Category = category, Description = description };
        d.Layers.Clear();
        d.Layers.Add(new Layer("0"));
        foreach (var (n, c) in layers) d.Layers.Add(new Layer(n, c));
        if (d.FindLayer("Dimensions") is null) d.Layers.Add(new Layer("Dimensions", "#7FD1FF"));
        if (d.FindLayer("Solids") is null) d.Layers.Add(new Layer("Solids", "#9AA7B4"));
        d.CurrentLayer = layers.Length > 0 ? layers[0].Name : "0";
        return d;
    }

    public static Mesh Box(double x, double y, double z, double w, double dpt, double h) => Shapes.Box(w, dpt, h, false).Translated(x, y, z);
    public static Mesh Cyl(double x, double y, double z, double r, double h, int seg = 32) => Shapes.Cylinder(r, h, seg).Translated(x, y, z);
    /// <summary>Cylinder lying along +X.</summary>
    public static Mesh CylX(double x, double y, double z, double r, double len, int seg = 32) => Shapes.Cylinder(r, len, seg).Transformed(Mat4.Translate(x, y, z) * Mat4.RotateY(Math.PI / 2));
    /// <summary>Cylinder lying along +Y.</summary>
    public static Mesh CylY(double x, double y, double z, double r, double len, int seg = 32) => Shapes.Cylinder(r, len, seg).Transformed(Mat4.Translate(x, y, z) * Mat4.RotateX(-Math.PI / 2));
}

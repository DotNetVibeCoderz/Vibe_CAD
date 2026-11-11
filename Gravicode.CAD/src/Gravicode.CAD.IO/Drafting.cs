using Gravicode.CAD.Topology;

namespace Gravicode.CAD.IO;

public enum ViewDirection { Front, Back, Top, Bottom, Right, Left, Isometric }

/// <summary>
/// Projects 3D bodies to 2D line work for technical drawings: feature + silhouette edges with
/// hidden-line removal against a rasterised depth buffer.
/// </summary>
public static class DrawingProjector
{
    /// <summary>World→view rotation; view looks down -Z of the returned frame (X right, Y up).</summary>
    public static Mat4 ViewMatrix(ViewDirection v) => v switch
    {
        ViewDirection.Front => Mat4.RotateX(-Math.PI / 2),                  // look along +Y, Z up
        ViewDirection.Back => Mat4.RotateX(-Math.PI / 2) * Mat4.RotateZ(Math.PI),
        ViewDirection.Top => Mat4.Identity,                                  // look along -Z
        ViewDirection.Bottom => Mat4.RotateX(Math.PI),
        ViewDirection.Right => Mat4.RotateX(-Math.PI / 2) * Mat4.RotateZ(-Math.PI / 2), // look along -X
        ViewDirection.Left => Mat4.RotateX(-Math.PI / 2) * Mat4.RotateZ(Math.PI / 2),
        _ => Mat4.RotateX(-Units.Deg(54.7356)) * Mat4.RotateZ(-Math.PI / 4)  // isometric
    };

    public sealed record ProjectedView(List<(Vec2 A, Vec2 B)> Visible, List<(Vec2 A, Vec2 B)> Hidden, Vec2 Min, Vec2 Max)
    {
        public Vec2 Size => Max - Min;
    }

    public static ProjectedView Project(CadModel model, ViewDirection dir, bool hiddenLines = true, double featureAngle = 30)
    {
        var view = ViewMatrix(dir);
        var visible = new List<(Vec2, Vec2)>(); var hidden = new List<(Vec2, Vec2)>();
        var bodies = model.Bodies.Where(b => b.Visible && !b.Mesh.IsEmpty).ToList();
        var all = Mesh.Merge(bodies.Select(b => b.Mesh)).Transformed(view);
        var bb = all.Bounds;
        if (bb.IsEmpty) return new([], [], Vec2.Zero, Vec2.Zero);

        // depth buffer (z larger = closer to viewer, since the view looks down -Z)
        const int Res = 1400;
        double span = Math.Max(bb.Size.X, bb.Size.Y);
        double scale = (Res - 4) / Math.Max(span, 1e-9);
        int W = (int)(bb.Size.X * scale) + 4, H = (int)(bb.Size.Y * scale) + 4;
        var depth = new float[W * H];
        Array.Fill(depth, float.NegativeInfinity);
        (double X, double Y, double Z) S(Vec3 p) => ((p.X - bb.Min.X) * scale + 2, (p.Y - bb.Min.Y) * scale + 2, p.Z);
        for (int t = 0; t < all.TriangleCount; t++)
        {
            var (a, b, c) = all.GetTriangle(t);
            Raster(S(a), S(b), S(c));
        }

        foreach (var body in bodies)
        {
            var he = new HalfEdgeMesh(body.Mesh.Transformed(view));
            var m = he.Mesh;
            var fn = he.FaceNormals();
            double cosT = Math.Cos(Units.Deg(featureAngle));
            for (int h = 0; h < he.HalfEdgeCount; h++)
            {
                int tw = he.Twin[h];
                if (tw >= 0 && tw < h) continue;
                int f0 = HalfEdgeMesh.FaceOf(h);
                bool edge;
                if (tw < 0) edge = true;
                else
                {
                    int f1 = HalfEdgeMesh.FaceOf(tw);
                    if (fn[f0] == Vec3.Zero || fn[f1] == Vec3.Zero) continue; // sliver triangles
                    bool sil = (fn[f0].Z > 0) != (fn[f1].Z > 0);
                    edge = sil || Vec3.Dot(fn[f0], fn[f1]) < cosT;
                }
                if (!edge) continue;
                var pa = m.Positions[he.Origin[h]]; var pb = m.Positions[he.Target(h)];
                Classify(pa, pb);
            }
        }
        return new(visible, hiddenLines ? hidden : [], new(bb.Min.X, bb.Min.Y), new(bb.Max.X, bb.Max.Y));

        void Classify(Vec3 a, Vec3 b)
        {
            var sa = S(a); var sb = S(b);
            double len = Math.Sqrt((sb.X - sa.X) * (sb.X - sa.X) + (sb.Y - sa.Y) * (sb.Y - sa.Y));
            int n = Math.Max(2, (int)(len / 1.5));
            bool? state = null; double tStart = 0;
            double eps = 2.5 / scale + bb.Size.Z * 1e-4;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double x = sa.X + (sb.X - sa.X) * t, y = sa.Y + (sb.Y - sa.Y) * t, z = sa.Z + (sb.Z - sa.Z) * t;
                bool vis = IsVisible(x, y, z, eps);
                if (state == null) { state = vis; tStart = 0; continue; }
                if (vis != state || i == n)
                {
                    double tEnd = i == n ? 1 : t;
                    var p0 = Vec3.Lerp(a, b, tStart).XY; var p1 = Vec3.Lerp(a, b, tEnd).XY;
                    if ((p1 - p0).LengthSquared > 1e-18) (state.Value ? visible : hidden).Add((p0, p1));
                    state = vis; tStart = t;
                }
            }
        }

        bool IsVisible(double x, double y, double z, double eps)
        {
            int cx = (int)x, cy = (int)y;
            // conservative: visible if any neighbouring depth sample is not in front
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int px = cx + dx, py = cy + dy;
                    if (px < 0 || py < 0 || px >= W || py >= H) return true;
                    if (z >= depth[py * W + px] - eps) return true;
                }
            return false;
        }

        void Raster((double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c)
        {
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            double area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Math.Abs(area) < 1e-12) return;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                    double w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                    double w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = (float)(w0 * a.Z + w1 * b.Z + w2 * c.Z);
                    ref float d = ref depth[y * W + x];
                    if (z > d) d = z;
                }
        }
    }
}

/// <summary>Builds a standard engineering sheet (front/top/right/iso views, border, title block).</summary>
public static class StandardDrawing
{
    public sealed record Sheet(double Width, double Height, List<DrawingEntity> Entities);

    /// <summary>A3 landscape sheet in millimeters by default.</summary>
    public static Sheet Create(CadModel model, string? title = null, double sheetW = 420, double sheetH = 297, bool hiddenLines = true)
    {
        var ents = new List<DrawingEntity>();
        const double margin = 10, tbH = 32;
        Rect(ents, margin, margin, sheetW - 2 * margin, sheetH - 2 * margin, "BORDER");

        var views = new[] { ViewDirection.Front, ViewDirection.Top, ViewDirection.Right, ViewDirection.Isometric }
            .Select(v => (Dir: v, P: DrawingProjector.Project(model, v, hiddenLines && v != ViewDirection.Isometric))).ToArray();
        double areaW = sheetW - 2 * margin - 20, areaH = sheetH - 2 * margin - tbH - 20;
        double cellW = areaW / 2, cellH = areaH / 2;
        double maxDim = views.Max(v => Math.Max(v.P.Size.X / cellW, v.P.Size.Y / cellH));
        double scale = maxDim > 0 ? 0.78 / maxDim : 1;
        scale = NiceScale(scale);

        // first-angle style layout: Front (top-left), Right (top-right), Top (bottom-left), Iso (bottom-right)
        var cells = new Dictionary<ViewDirection, (double X, double Y)>
        {
            [ViewDirection.Front] = (margin + 10, margin + tbH + 10 + cellH),
            [ViewDirection.Right] = (margin + 10 + cellW, margin + tbH + 10 + cellH),
            [ViewDirection.Top] = (margin + 10, margin + tbH + 10),
            [ViewDirection.Isometric] = (margin + 10 + cellW, margin + tbH + 10)
        };
        foreach (var (dir, p) in views)
        {
            var (cx, cy) = cells[dir];
            var c = (p.Min + p.Max) * 0.5;
            var off = new Vec2(cx + cellW / 2, cy + cellH / 2);
            Vec2 Map(Vec2 q) => (q - c) * scale + off;
            foreach (var (a, b) in p.Hidden) ents.Add(new LineEntity(Map(a), Map(b)) { Layer = "HIDDEN" });
            foreach (var (a, b) in p.Visible) ents.Add(new LineEntity(Map(a), Map(b)) { Layer = "VISIBLE" });
            ents.Add(new TextEntity(new Vec2(cx + 4, cy + 4), dir.ToString().ToUpperInvariant() + (dir == ViewDirection.Isometric ? "" : " VIEW"), 3.5) { Layer = "TEXT" });
            if (dir == ViewDirection.Front && p.Size.X > 0)
            {
                // overall dimensions of the front view
                double y0 = Map(p.Min).Y - 8;
                var l = Map(p.Min).X; var r = Map(p.Max).X;
                ents.Add(new LineEntity(new(l, y0), new(r, y0)) { Layer = "DIM" });
                ents.Add(new LineEntity(new(l, y0 - 2), new(l, y0 + 2)) { Layer = "DIM" });
                ents.Add(new LineEntity(new(r, y0 - 2), new(r, y0 + 2)) { Layer = "DIM" });
                ents.Add(new TextEntity(new((l + r) / 2 - 8, y0 + 1.5), $"{p.Size.X:0.##}", 3) { Layer = "DIM" });
                double x0 = Map(p.Max).X + 8;
                var bt = Map(p.Min).Y; var tp = Map(p.Max).Y;
                ents.Add(new LineEntity(new(x0, bt), new(x0, tp)) { Layer = "DIM" });
                ents.Add(new LineEntity(new(x0 - 2, bt), new(x0 + 2, bt)) { Layer = "DIM" });
                ents.Add(new LineEntity(new(x0 - 2, tp), new(x0 + 2, tp)) { Layer = "DIM" });
                ents.Add(new TextEntity(new(x0 + 2, (bt + tp) / 2), $"{p.Size.Y:0.##}", 3) { Layer = "DIM" });
            }
        }

        // title block
        double tbW = 180, tx = sheetW - margin - tbW, ty = margin;
        Rect(ents, tx, ty, tbW, tbH, "TITLE");
        ents.Add(new LineEntity(new(tx, ty + 20), new(tx + tbW, ty + 20)) { Layer = "TITLE" });
        ents.Add(new LineEntity(new(tx + 120, ty), new(tx + 120, ty + 20)) { Layer = "TITLE" });
        ents.Add(new TextEntity(new(tx + 4, ty + 24), (title ?? model.Name).ToUpperInvariant(), 5) { Layer = "TITLE" });
        var bb = model.Bounds;
        ents.Add(new TextEntity(new(tx + 4, ty + 13), $"SIZE {bb.Size.X:0.#} x {bb.Size.Y:0.#} x {bb.Size.Z:0.#} {model.Units.Symbol()}", 3) { Layer = "TITLE" });
        ents.Add(new TextEntity(new(tx + 4, ty + 7), GravicodeCad.CreditsId, 2.4) { Layer = "TITLE" });
        ents.Add(new TextEntity(new(tx + 4, ty + 2.5), $"{GravicodeCad.ProductName} {GravicodeCad.Version}", 2.2) { Layer = "TITLE" });
        ents.Add(new TextEntity(new(tx + 124, ty + 13), $"SCALE {(scale >= 1 ? $"{scale:0.##}:1" : $"1:{1 / scale:0.##}")}", 3) { Layer = "TITLE" });
        ents.Add(new TextEntity(new(tx + 124, ty + 7), DateTime.Now.ToString("yyyy-MM-dd"), 3) { Layer = "TITLE" });
        ents.Add(new TextEntity(new(tx + 124, ty + 2.5), "SHEET 1/1  A3", 2.4) { Layer = "TITLE" });
        return new Sheet(sheetW, sheetH, ents);
    }

    private static double NiceScale(double s)
    {
        double[] nice = [0.01, 0.02, 0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 2.5, 5, 10, 20, 50];
        return nice.Where(n => n <= s).DefaultIfEmpty(s).Max();
    }

    private static void Rect(List<DrawingEntity> e, double x, double y, double w, double h, string layer) =>
        e.Add(new PolylineEntity([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)], true) { Layer = layer });
}

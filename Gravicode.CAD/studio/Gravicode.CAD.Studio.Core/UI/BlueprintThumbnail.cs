using Gravicode.CAD.Rendering;

namespace Gravicode.CAD.Studio.UI;

/// <summary>Rasterises a document's plan view onto blueprint paper (used for template thumbnails and 2D PNG previews).</summary>
public static class BlueprintThumbnail
{
    public static RgbaImage Render(StudioDocument doc, int width = 640, int height = 400, double margin = 0.08)
    {
        const int ss = 3;                                   // supersampling factor
        int w = width * ss, h = height * ss;
        var acc = new float[w * h * 3];
        var paper = Hex("#0F2747");
        for (int i = 0; i < w * h; i++) { acc[i * 3] = (float)paper.R; acc[i * 3 + 1] = (float)paper.G; acc[i * 3 + 2] = (float)paper.B; }

        var items = DisplayList.Build(doc, new HashSet<Guid>());
        var bb = doc.Bounds;
        double minX = bb.IsEmpty ? -50 : bb.Min.X, minY = bb.IsEmpty ? -50 : bb.Min.Y, maxX = bb.IsEmpty ? 250 : bb.Max.X, maxY = bb.IsEmpty ? 200 : bb.Max.Y;
        double scale = Math.Min(w * (1 - 2 * margin) / Math.Max(maxX - minX, 1e-9), h * (1 - 2 * margin) / Math.Max(maxY - minY, 1e-9));
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        (double X, double Y) P(double x, double y) => (w / 2.0 + (x - cx) * scale, h / 2.0 - (y - cy) * scale);

        // grid: minor every "nice" step so that ~24 cells span the width, major every 5
        double step = Nice((maxX - minX) / 24);
        var minor = new CadColor(1, 1, 1, .028); var major = new CadColor(1, 1, 1, .06);
        double gx0 = Math.Floor((cx - w / 2.0 / scale) / step) * step, gy0 = Math.Floor((cy - h / 2.0 / scale) / step) * step;
        for (double x = gx0; P(x, 0).X < w; x += step)
        {
            int px = (int)Math.Round(P(x, 0).X); bool maj = Math.Abs(Math.Round(x / step) % 5) < 0.5;
            for (int y = 0; y < h; y++) Plot(px, y, maj ? major : minor, ss);
        }
        for (double y = gy0; P(0, y).Y > 0; y += step)
        {
            int py = (int)Math.Round(P(0, y).Y); bool maj = Math.Abs(Math.Round(y / step) % 5) < 0.5;
            for (int x = 0; x < w; x++) Plot(x, py, maj ? major : minor, ss);
        }

        foreach (var it in items)
        {
            var c = Hex(it.Color);
            var p = it.Points;
            switch (it.Kind)
            {
                case "segs":
                    for (int i = 0; i + 3 < p.Length; i += 4) Line(P(p[i], p[i + 1]), P(p[i + 2], p[i + 3]), c);
                    break;
                case "poly":
                    for (int i = 0; i + 3 < p.Length; i += 2) Line(P(p[i], p[i + 1]), P(p[i + 2], p[i + 3]), c);
                    if (it.Closed && p.Length >= 4) Line(P(p[^2], p[^1]), P(p[0], p[1]), c);
                    break;
                case "circle":
                    {
                        int n = Math.Clamp((int)(p[2] * scale / 2), 24, 720);
                        for (int i = 0; i < n; i++)
                        {
                            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                            Line(P(p[0] + p[2] * Math.Cos(a0), p[1] + p[2] * Math.Sin(a0)), P(p[0] + p[2] * Math.Cos(a1), p[1] + p[2] * Math.Sin(a1)), c);
                        }
                        break;
                    }
                case "text":
                    {
                        // text as a thin underline bar at its height — readable as "label here" at thumbnail size
                        var (x, y) = P(p[0], p[1]);
                        double len = (it.Text?.Length ?? 0) * it.Height * 0.62 * scale, th = Math.Max(it.Height * scale * 0.55, ss);
                        for (double dy = 0; dy < th; dy += ss) Line((x, y - dy - th * 0.2), (x + len, y - dy - th * 0.2), c with { A = 0.35 });
                        break;
                    }
                case "point":
                    {
                        var (x, y) = P(p[0], p[1]);
                        Line((x - 3 * ss, y), (x + 3 * ss, y), c); Line((x, y - 3 * ss), (x, y + 3 * ss), c);
                        break;
                    }
            }
        }

        // downsample
        var img = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        int i = ((y * ss + sy) * w + x * ss + sx) * 3;
                        r += acc[i]; g += acc[i + 1]; b += acc[i + 2];
                    }
                float k = 1f / (ss * ss);
                img.Set(x, y, new CadColor(r * k, g * k, b * k, 1));
            }
        return img;

        void Line((double X, double Y) a, (double X, double Y) b, CadColor c)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            int n = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            if (n > 20000) n = 20000;
            for (int i = 0; i <= n; i++)
            {
                double t = n == 0 ? 0 : (double)i / n;
                Plot((int)Math.Round(a.X + dx * t) - 1, (int)Math.Round(a.Y + dy * t) - 1, c, ss + 1);
            }
        }

        void Plot(int x, int y, CadColor c, int thick)
        {
            for (int oy = 0; oy < thick; oy++)
                for (int ox = 0; ox < thick; ox++)
                {
                    int px = x + ox, py = y + oy;
                    if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
                    int i = (py * w + px) * 3; float a = (float)c.A;
                    acc[i] += (float)(c.R - acc[i]) * a; acc[i + 1] += (float)(c.G - acc[i + 1]) * a; acc[i + 2] += (float)(c.B - acc[i + 2]) * a;
                }
        }
    }

    private static double Nice(double v)
    {
        if (v <= 0) return 10;
        double e = Math.Pow(10, Math.Floor(Math.Log10(v))), f = v / e;
        return (f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10) * e;
    }

    private static CadColor Hex(string s)
    {
        s = s.TrimStart('#');
        if (s.Length < 6) return new CadColor(0.9, 0.93, 0.95, 1);
        return new CadColor(Convert.ToInt32(s[..2], 16) / 255.0, Convert.ToInt32(s[2..4], 16) / 255.0, Convert.ToInt32(s[4..6], 16) / 255.0, 1);
    }
}

using Gravicode.CAD.Topology;

namespace Gravicode.CAD.Rendering;

/// <summary>
/// Deterministic CPU renderer for CAD models: deferred shading with a three-point light rig,
/// hemispheric ambient, Blinn-Phong/metallic speculars, filmic tone mapping, a soft ground
/// shadow, an infinite fading grid and crisp depth-tested feature edges. Multi-threaded and
/// supersampled. Ideal for thumbnails, CI screenshots, documentation images and server-side previews.
/// </summary>
public static class SoftwareRenderer
{
    private struct Tri
    {
        public double X0, Y0, Z0, X1, Y1, Z1, X2, Y2, Z2; // screen x,y + view depth
        public Vec3 N0, N1, N2;
        public int Body;
        public double MinY, MaxY;
    }

    public static RgbaImage Render(CadModel model, RenderOptions? options = null)
    {
        var o = options ?? new RenderOptions();
        int ss = Math.Clamp(o.Supersample, 1, 4);
        int W = o.Width * ss, H = o.Height * ss;
        var bodies = model.Bodies.Where(b => b.Visible && !b.Mesh.IsEmpty).ToList();
        var bb = BoundingBox.Empty;
        foreach (var b in bodies) bb = bb.Union(b.Mesh.Bounds);
        if (bb.IsEmpty) bb = new BoundingBox(new Vec3(-50, -50, 0), new Vec3(50, 50, 50));
        var cam = o.Camera ?? new OrbitCamera().Fit(bb);
        var (right, up, fwd) = cam.Basis();
        var eye = cam.Position;
        double aspect = (double)W / H;
        double tanHalf = Math.Tan(Units.Deg(cam.FieldOfView) / 2);
        double near = Math.Max(cam.Distance * 1e-3, 1e-6);

        (double X, double Y, double Z) Project(Vec3 p)
        {
            var d = p - eye;
            double z = Vec3.Dot(d, fwd);
            double x = Vec3.Dot(d, right), y = Vec3.Dot(d, up);
            double sx, sy;
            if (cam.Orthographic) { double hh = cam.OrthoHeight / 2; sx = x / (hh * aspect); sy = y / hh; }
            else { double zz = Math.Max(z, near); sx = x / (zz * tanHalf * aspect); sy = y / (zz * tanHalf); }
            return ((sx * 0.5 + 0.5) * W, (0.5 - sy * 0.5) * H, z);
        }

        Vec3 RayDir(double px, double py)
        {
            double sx = (px / W) * 2 - 1, sy = 1 - (py / H) * 2;
            return (fwd + right * (sx * tanHalf * aspect) + up * (sy * tanHalf)).Normalized();
        }

        // ---------- triangles (with crease-aware normals) ----------
        var tris = new List<Tri>();
        var colors = new CadColor[bodies.Count];
        var metallic = new double[bodies.Count];
        var rough = new double[bodies.Count];
        for (int bi = 0; bi < bodies.Count; bi++)
        {
            var b = bodies[bi];
            colors[bi] = ToLinear(o.OverrideColor ?? b.Color);
            metallic[bi] = Math.Clamp(b.Metallic, 0, 1);
            rough[bi] = Math.Clamp(b.Roughness, 0.05, 1);
            var sm = ShadedMesh.FromMesh(b.Mesh, 35);
            for (int i = 0; i < sm.Indices.Length; i += 3)
            {
                Vec3 P(uint k) => new(sm.Positions[k * 3], sm.Positions[k * 3 + 1], sm.Positions[k * 3 + 2]);
                Vec3 N(uint k) => new(sm.Normals[k * 3], sm.Normals[k * 3 + 1], sm.Normals[k * 3 + 2]);
                var a = Project(P(sm.Indices[i])); var bq = Project(P(sm.Indices[i + 1])); var c = Project(P(sm.Indices[i + 2]));
                if (!cam.Orthographic && (a.Z < near || bq.Z < near || c.Z < near)) continue;
                tris.Add(new Tri
                {
                    X0 = a.X, Y0 = a.Y, Z0 = a.Z, X1 = bq.X, Y1 = bq.Y, Z1 = bq.Z, X2 = c.X, Y2 = c.Y, Z2 = c.Z,
                    N0 = N(sm.Indices[i]), N1 = N(sm.Indices[i + 1]), N2 = N(sm.Indices[i + 2]), Body = bi,
                    MinY = Math.Min(a.Y, Math.Min(bq.Y, c.Y)), MaxY = Math.Max(a.Y, Math.Max(bq.Y, c.Y))
                });
            }
        }

        // ---------- G-buffer ----------
        var depth = new float[W * H];
        var nx = new float[W * H]; var ny = new float[W * H]; var nz = new float[W * H];
        var bodyId = new short[W * H];
        Array.Fill(depth, float.PositiveInfinity);
        Array.Fill(bodyId, (short)-1);
        int bands = Math.Max(1, Environment.ProcessorCount * 2);
        int bandH = (H + bands - 1) / bands;
        var triArr = tris.ToArray();
        Parallel.For(0, bands, band =>
        {
            int y0 = band * bandH, y1 = Math.Min(H, y0 + bandH);
            for (int t = 0; t < triArr.Length; t++)
            {
                ref var tr = ref triArr[t];
                if (tr.MaxY < y0 || tr.MinY >= y1) continue;
                RasterTri(ref tr, y0, y1);
            }
        });

        void RasterTri(ref Tri t, int yBand0, int yBand1)
        {
            double area = (t.X1 - t.X0) * (t.Y2 - t.Y0) - (t.Y1 - t.Y0) * (t.X2 - t.X0);
            if (Math.Abs(area) < 1e-12) return;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(t.X0, Math.Min(t.X1, t.X2))));
            int maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(t.X0, Math.Max(t.X1, t.X2))));
            int minY = Math.Max(yBand0, (int)Math.Floor(t.MinY));
            int maxY = Math.Min(yBand1 - 1, (int)Math.Ceiling(t.MaxY));
            double inv = 1 / area;
            // perspective-correct interpolation uses 1/z
            double iz0 = 1 / Math.Max(t.Z0, 1e-12), iz1 = 1 / Math.Max(t.Z1, 1e-12), iz2 = 1 / Math.Max(t.Z2, 1e-12);
            bool ortho = cam.Orthographic;
            for (int y = minY; y <= maxY; y++)
            {
                double py = y + 0.5;
                for (int x = minX; x <= maxX; x++)
                {
                    double px = x + 0.5;
                    double w0 = ((t.X1 - px) * (t.Y2 - py) - (t.Y1 - py) * (t.X2 - px)) * inv;
                    double w1 = ((t.X2 - px) * (t.Y0 - py) - (t.Y2 - py) * (t.X0 - px)) * inv;
                    double w2 = 1 - w0 - w1;
                    if (w0 < -1e-9 || w1 < -1e-9 || w2 < -1e-9) continue;
                    double z, a0 = w0, a1 = w1, a2 = w2;
                    if (ortho) z = w0 * t.Z0 + w1 * t.Z1 + w2 * t.Z2;
                    else
                    {
                        double iz = w0 * iz0 + w1 * iz1 + w2 * iz2;
                        z = 1 / iz;
                        a0 = w0 * iz0 * z; a1 = w1 * iz1 * z; a2 = w2 * iz2 * z;
                    }
                    int idx = y * W + x;
                    if (z >= depth[idx]) continue;
                    depth[idx] = (float)z;
                    var n = t.N0 * a0 + t.N1 * a1 + t.N2 * a2;
                    nx[idx] = (float)n.X; ny[idx] = (float)n.Y; nz[idx] = (float)n.Z;
                    bodyId[idx] = (short)t.Body;
                }
            }
        }

        // ---------- ground shadow map ----------
        double groundZ = bb.Min.Z;
        var lightDir = new Vec3(-0.35, 0.45, 1).Normalized(); // towards the light
        double ext = Math.Max(bb.Size.X, bb.Size.Y) * 1.2 + bb.Size.Z * 1.5 + 1e-6;
        var gc = bb.Center;
        const int SM = 384;
        float[]? shadow = null;
        if (o.GroundShadow && bodies.Count > 0)
        {
            shadow = new float[SM * SM];
            (double X, double Y) ToMap(Vec3 p)
            {
                var q = p - lightDir * ((p.Z - groundZ) / lightDir.Z);
                return ((q.X - gc.X) / (2 * ext) * SM + SM / 2.0, (q.Y - gc.Y) / (2 * ext) * SM + SM / 2.0);
            }
            foreach (var b in bodies)
                foreach (var (a, b2, c) in b.Mesh.Triangles())
                {
                    var pa = ToMap(a); var pb = ToMap(b2); var pc = ToMap(c);
                    FillMap(shadow, pa, pb, pc);
                }
            BoxBlur(shadow, SM, Math.Max(2, SM / 60), 3);
        }

        // ---------- shading ----------
        var img = new RgbaImage(W, H);
        var keyL = (right * -0.45 + up * 0.75 - fwd * 0.6).Normalized();
        var fillL = (right * 0.8 + up * 0.15 - fwd * 0.4).Normalized();
        var rimL = (up * 0.35 + fwd * 0.9).Normalized();
        var bgTop = ToLinear(o.BackgroundTop); var bgBot = ToLinear(o.BackgroundBottom); var grid = ToLinear(o.GridColor);
        double gridStep = NiceStep(Math.Max(bb.Size.X, bb.Size.Y) / 8);
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                int idx = y * W + x;
                CadColor col; double alpha = 1;
                var view = RayDir(x + 0.5, y + 0.5);
                if (bodyId[idx] >= 0)
                {
                    int bi = bodyId[idx];
                    var n = new Vec3(nx[idx], ny[idx], nz[idx]).Normalized();
                    if (Vec3.Dot(n, view) > 0) n = -n; // back faces of open shells
                    col = Shade(colors[bi], metallic[bi], rough[bi], n, -view, keyL, fillL, rimL);
                }
                else
                {
                    var bg = CadColor.Lerp(bgTop, bgBot, (double)y / H);
                    col = bg; alpha = o.TransparentBackground ? 0 : 1;
                    // ground plane
                    if (Math.Abs(view.Z) > 1e-9)
                    {
                        double t = (groundZ - eye.Z) / view.Z;
                        if (t > 0)
                        {
                            var p = eye + view * t;
                            double dist = Math.Sqrt((p.X - gc.X) * (p.X - gc.X) + (p.Y - gc.Y) * (p.Y - gc.Y));
                            double fade = Math.Clamp(1 - dist / (ext * 1.6), 0, 1);
                            fade *= fade;
                            if (o.Grid && fade > 0)
                            {
                                double pxw = t * tanHalf * 2 / H * 1.2; // world size of a pixel
                                double gx = Math.Abs(p.X / gridStep - Math.Round(p.X / gridStep)) * gridStep;
                                double gy = Math.Abs(p.Y / gridStep - Math.Round(p.Y / gridStep)) * gridStep;
                                double line = Math.Max(Math.Clamp(1 - gx / pxw, 0, 1), Math.Clamp(1 - gy / pxw, 0, 1));
                                col = CadColor.Lerp(col, grid, line * fade * 0.8);
                                if (o.TransparentBackground) alpha = Math.Max(alpha, line * fade * 0.6);
                            }
                            if (shadow != null && fade > 0)
                            {
                                double mx = (p.X - gc.X) / (2 * ext) * SM + SM / 2.0, my = (p.Y - gc.Y) / (2 * ext) * SM + SM / 2.0;
                                double s = SampleBilinear(shadow, SM, mx, my);
                                col = col.Scale(1 - 0.55 * s * fade);
                                if (o.TransparentBackground) alpha = Math.Max(alpha, 0.55 * s * fade);
                            }
                        }
                    }
                }
                img.Set(x, y, ToDisplay(col, o.Exposure, alpha, bodyId[idx] >= 0));
            }
        });

        // ---------- feature edges ----------
        if (o.DrawEdges)
        {
            var edgeCol = o.EdgeColor;
            double thick = Math.Max(1, ss * 0.9);
            foreach (var b in bodies)
            {
                var he = new HalfEdgeMesh(b.Mesh);
                foreach (var (a, c) in he.FeatureEdges(o.EdgeAngle))
                {
                    var pa = Project(a); var pc = Project(c);
                    if (!cam.Orthographic && (pa.Z < near || pc.Z < near)) continue;
                    DrawLine(img, pa, pc, depth, W, H, edgeCol, thick, cam.Distance * 2e-3 + bb.Diagonal * 1e-3);
                }
            }
        }
        return img.Downsample(ss);
    }

    private static CadColor Shade(CadColor baseC, double metal, double rough, Vec3 n, Vec3 v, Vec3 key, Vec3 fill, Vec3 rim)
    {
        double shininess = 2 / Math.Max(rough * rough, 1e-3) - 2;
        double specScale = (shininess + 8) / (8 * Math.PI);
        var diffuseC = baseC.Scale(1 - metal * 0.85);
        var specC = CadColor.Lerp(new CadColor(0.04, 0.04, 0.04), baseC, metal);
        // hemispheric ambient
        double hemi = 0.5 + 0.5 * n.Z;
        var amb = CadColor.Lerp(new CadColor(0.10, 0.09, 0.08), new CadColor(0.32, 0.35, 0.42), hemi);
        double r = diffuseC.R * amb.R, g = diffuseC.G * amb.G, b = diffuseC.B * amb.B;
        // environment-ish reflection for metals
        double fres = Math.Pow(1 - Math.Max(0, Vec3.Dot(n, v)), 5);
        var env = 0.18 + 0.25 * hemi;
        r += specC.R * env * (0.4 + fres); g += specC.G * env * (0.4 + fres); b += specC.B * env * (0.4 + fres);
        foreach (var (l, intensity) in new[] { (key, 2.6), (fill, 0.9), (rim, 1.4) })
        {
            double ndl = Vec3.Dot(n, l);
            if (ndl <= 0) continue;
            var h = (l + v).Normalized();
            double spec = Math.Pow(Math.Max(0, Vec3.Dot(n, h)), shininess) * specScale * ndl;
            r += (diffuseC.R * ndl / Math.PI * 3 + specC.R * spec) * intensity;
            g += (diffuseC.G * ndl / Math.PI * 3 + specC.G * spec) * intensity;
            b += (diffuseC.B * ndl / Math.PI * 3 + specC.B * spec) * intensity;
        }
        return new CadColor(r, g, b);
    }

    private static CadColor ToLinear(CadColor c) => new(Math.Pow(c.R, 2.2), Math.Pow(c.G, 2.2), Math.Pow(c.B, 2.2), c.A);

    private static CadColor ToDisplay(CadColor c, double exposure, double alpha, bool tonemap)
    {
        static double Aces(double x) { x *= 0.8; return Math.Clamp(x * (2.51 * x + 0.03) / (x * (2.43 * x + 0.59) + 0.14), 0, 1); }
        double r = c.R * exposure, g = c.G * exposure, b = c.B * exposure;
        if (tonemap) { r = Aces(r); g = Aces(g); b = Aces(b); }
        return new CadColor(Math.Pow(Math.Clamp(r, 0, 1), 1 / 2.2), Math.Pow(Math.Clamp(g, 0, 1), 1 / 2.2), Math.Pow(Math.Clamp(b, 0, 1), 1 / 2.2), alpha);
    }

    private static void DrawLine(RgbaImage img, (double X, double Y, double Z) a, (double X, double Y, double Z) b, float[] depth, int W, int H, CadColor col, double thick, double bias)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)(len * 1.5));
        int r = (int)Math.Ceiling(thick / 2);
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double x = a.X + dx * t, y = a.Y + dy * t, z = a.Z + (b.Z - a.Z) * t;
            for (int oy = -r; oy <= r; oy++)
                for (int ox = -r; ox <= r; ox++)
                {
                    int px = (int)x + ox, py = (int)y + oy;
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    double d = Math.Sqrt((px + 0.5 - x) * (px + 0.5 - x) + (py + 0.5 - y) * (py + 0.5 - y));
                    double cov = Math.Clamp(thick / 2 + 0.5 - d, 0, 1);
                    if (cov <= 0) continue;
                    if (z > depth[py * W + px] + bias * Math.Max(1, z * 0.01)) continue;
                    var under = img.Get(px, py);
                    img.Set(px, py, CadColor.Lerp(under, col, cov * 0.85) with { A = Math.Max(under.A, cov) });
                }
        }
    }

    private static void FillMap(float[] map, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        int S = (int)Math.Sqrt(map.Length);
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = Math.Min(S - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = Math.Min(S - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        double area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(area) < 1e-12) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                double w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                if (w0 >= 0 && w1 >= 0 && 1 - w0 - w1 >= 0) map[y * S + x] = 1;
            }
    }

    private static void BoxBlur(float[] m, int S, int radius, int passes)
    {
        var tmp = new float[m.Length];
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < S; y++)
            {
                double acc = 0; int cnt = 0;
                for (int x = -radius; x < S + radius; x++)
                {
                    if (x + radius < S) { acc += m[y * S + x + radius]; cnt++; }
                    if (x - radius - 1 >= 0) { acc -= m[y * S + x - radius - 1]; cnt--; }
                    if (x >= 0 && x < S) tmp[y * S + x] = (float)(acc / Math.Max(cnt, 1));
                }
            }
            for (int x = 0; x < S; x++)
            {
                double acc = 0; int cnt = 0;
                for (int y = -radius; y < S + radius; y++)
                {
                    if (y + radius < S) { acc += tmp[(y + radius) * S + x]; cnt++; }
                    if (y - radius - 1 >= 0) { acc -= tmp[(y - radius - 1) * S + x]; cnt--; }
                    if (y >= 0 && y < S) m[y * S + x] = (float)(acc / Math.Max(cnt, 1));
                }
            }
        }
    }

    private static double SampleBilinear(float[] m, int S, double x, double y)
    {
        x -= 0.5; y -= 0.5;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double V(int xx, int yy) => xx < 0 || yy < 0 || xx >= S || yy >= S ? 0 : m[yy * S + xx];
        return (V(x0, y0) * (1 - fx) + V(x0 + 1, y0) * fx) * (1 - fy) + (V(x0, y0 + 1) * (1 - fx) + V(x0 + 1, y0 + 1) * fx) * fy;
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0) return 10;
        double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / p;
        return (f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10) * p;
    }

    /// <summary>Convenience: render straight to a PNG file.</summary>
    public static void RenderToPng(CadModel model, string path, RenderOptions? options = null) => Render(model, options).SavePng(path);
}

using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.CAM;

/// <summary>FDM print settings.</summary>
public sealed record PrintSettings
{
    public double LayerHeight { get; init; } = 0.2;
    public double FirstLayerHeight { get; init; } = 0.25;
    public double NozzleDiameter { get; init; } = 0.4;
    public double ExtrusionWidth { get; init; } = 0.45;
    public double FilamentDiameter { get; init; } = 1.75;
    public int Perimeters { get; init; } = 2;
    /// <summary>Infill density 0..1.</summary>
    public double InfillDensity { get; init; } = 0.2;
    public double PrintSpeed { get; init; } = 2400;   // mm/min
    public double TravelSpeed { get; init; } = 9000;
    public double NozzleTemperature { get; init; } = 210;
    public double BedTemperature { get; init; } = 60;
}

public sealed record SliceLayer(double Z, List<Polygon2> Loops)
{
    public List<Profile2> Regions => Profile2.FromLoops(Loops);
}

/// <summary>Mesh slicer for additive manufacturing (uses the active mesh kernel — Rust when enabled).</summary>
public static class Slicer
{
    /// <summary>Slices the mesh into closed loops at evenly spaced Z levels.</summary>
    public static List<SliceLayer> Slice(Mesh mesh, double layerHeight, double firstLayer = 0)
    {
        var bb = mesh.Bounds;
        var layers = new List<SliceLayer>();
        double z = bb.Min.Z + (firstLayer > 0 ? firstLayer : layerHeight) * 0.5;
        while (z < bb.Max.Z)
        {
            var segs = mesh.SliceZ(z);
            layers.Add(new SliceLayer(z, ChainLoops(segs)));
            z += layerHeight;
        }
        return layers;
    }

    /// <summary>Joins unordered segments into closed loops by matching end points.</summary>
    public static List<Polygon2> ChainLoops(List<(Vec3 A, Vec3 B)> segs, double tol = 1e-6)
    {
        // undirected end-point matching on a hash grid (neighbour cells searched for robustness)
        (long, long) Key(Vec2 p) => ((long)Math.Floor(p.X / tol), (long)Math.Floor(p.Y / tol));
        var ends = new Dictionary<(long, long), List<(int Seg, int End)>>();
        for (int i = 0; i < segs.Count; i++)
            for (int e = 0; e < 2; e++)
            {
                var k = Key(e == 0 ? segs[i].A.XY : segs[i].B.XY);
                if (!ends.TryGetValue(k, out var l)) ends[k] = l = [];
                l.Add((i, e));
            }
        var used = new bool[segs.Count];
        var loops = new List<Polygon2>();
        for (int i = 0; i < segs.Count; i++)
        {
            if (used[i]) continue;
            var pts = new List<Vec2> { segs[i].A.XY };
            used[i] = true;
            var cur = segs[i].B.XY;
            int guard = 0;
            while (guard++ <= segs.Count)
            {
                if (pts.Count > 2 && cur.ApproxEquals(pts[0], tol * 4)) break;
                pts.Add(cur);
                var next = FindNext(cur);
                if (next is not { } n) break;
                used[n.Seg] = true;
                cur = n.End == 0 ? segs[n.Seg].B.XY : segs[n.Seg].A.XY;
            }
            if (pts.Count >= 3) loops.Add(new Polygon2(pts).Simplified(1e-9));
        }
        return loops;

        (int Seg, int End)? FindNext(Vec2 p)
        {
            var (kx, ky) = Key(p);
            (int, int)? best = null; double bd = tol * tol * 16;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (ends.TryGetValue((kx + dx, ky + dy), out var list))
                        foreach (var (s, e) in list)
                        {
                            if (used[s]) continue;
                            var q = e == 0 ? segs[s].A.XY : segs[s].B.XY;
                            var d = (q - p).LengthSquared;
                            if (d <= bd) { bd = d; best = (s, e); }
                        }
            return best;
        }
    }

    /// <summary>Generates an FDM toolpath: perimeters (outermost last) plus alternating ±45° rectilinear infill.</summary>
    public static Toolpath PrintPath(Mesh mesh, PrintSettings? settings = null)
    {
        var s = settings ?? new PrintSettings();
        var tool = new Tool($"Nozzle Ø{s.NozzleDiameter}", s.NozzleDiameter, ToolType.Extruder, s.PrintSpeed, s.PrintSpeed / 2, 0);
        var path = new Toolpath("FDM print", tool) { SafeZ = 0 };
        var bb = mesh.Bounds;
        // place on the bed
        var m = mesh.Translated(0, 0, -bb.Min.Z);
        double filArea = Math.PI * s.FilamentDiameter * s.FilamentDiameter / 4;
        var layers = Slice(m, s.LayerHeight, s.FirstLayerHeight);
        int li = 0;
        foreach (var layer in layers)
        {
            double h = li == 0 ? s.FirstLayerHeight : s.LayerHeight;
            double zTop = li == 0 ? s.FirstLayerHeight : s.FirstLayerHeight + li * s.LayerHeight;
            double ePerMm = s.ExtrusionWidth * h / filArea;
            foreach (var region in layer.Regions)
            {
                // perimeters
                for (int p = s.Perimeters - 1; p >= 0; p--)
                {
                    double inset = s.ExtrusionWidth * (p + 0.5);
                    foreach (var loop in new[] { region.Outer.Offset(-inset) }.Concat(region.Holes.Select(hh => hh.AsCounterClockwise().Offset(inset))))
                        Extrude(loop.Points, closed: true);
                }
                // infill
                double inner = s.ExtrusionWidth * (s.Perimeters + 0.3);
                var infillRegion = new Profile2(region.Outer.Offset(-inner), region.Holes.Select(hh => hh.AsCounterClockwise().Offset(inner)));
                bool solid = li < 3 || li >= layers.Count - 3;
                double density = solid ? 1 : s.InfillDensity;
                if (density > 0)
                {
                    var tp = Milling.Pocket(infillRegion, new Tool("infill", s.ExtrusionWidth / Math.Max(density, 0.05) / 0.95), 0.001, 1, 0.95, li % 2 == 0 ? 45 : -45, 0, finishingPass: false);
                    // convert the pocket raster into extrusion moves (skip its finishing contour)
                    Vec2? last = null;
                    foreach (var mv in tp.Moves)
                    {
                        if (mv.Kind == MoveKind.Rapid) { last = null; continue; }
                        var p2 = mv.To.XY;
                        if (last is null) { path.Rapid(new Vec3(p2.X, p2.Y, zTop)); last = p2; continue; }
                        path.Feed(new Vec3(p2.X, p2.Y, zTop), s.PrintSpeed, Vec2.Distance(last.Value, p2) * ePerMm);
                        last = p2;
                    }
                }
            }
            li++;

            void Extrude(IReadOnlyList<Vec2> pts, bool closed)
            {
                if (pts.Count < 2) return;
                path.Rapid(new Vec3(pts[0].X, pts[0].Y, zTop));
                for (int i = 1; i <= pts.Count - (closed ? 0 : 1); i++)
                {
                    var a = pts[i - 1]; var b = pts[i % pts.Count];
                    path.Feed(new Vec3(b.X, b.Y, zTop), s.PrintSpeed * (li == 0 ? 0.5 : 1), Vec2.Distance(a, b) * ePerMm);
                }
            }
        }
        return path;
    }
}

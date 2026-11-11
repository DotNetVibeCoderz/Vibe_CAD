namespace Gravicode.CAD.Studio;

/// <summary>
/// Hatched or solid-filled region bounded by one or more closed loops (even-odd: loops inside loops are islands).
/// Patterns: ANSI31 (45° lines, 3.175 mm × scale), ANSI37 (cross-hatch), SOLID.
/// </summary>
public sealed class HatchEntity(IEnumerable<IReadOnlyList<Vec2>> loops, string pattern = "ANSI31", double scale = 1, double angle = 0) : DrawingEntity
{
    public static readonly string[] Patterns = ["ANSI31", "ANSI37", "SOLID"];
    public const int MaxLines = 6000;

    public IReadOnlyList<IReadOnlyList<Vec2>> Loops { get; } = [.. loops.Select(l => (IReadOnlyList<Vec2>)[.. l])];
    public string Pattern { get; } = Patterns.Contains(pattern.ToUpperInvariant()) ? pattern.ToUpperInvariant() : "ANSI31";
    public double Scale { get; } = scale > 0 ? scale : 1;
    /// <summary>Extra rotation of the pattern, radians.</summary>
    public double Angle { get; } = angle;
    public bool IsSolid => Pattern == "SOLID";

    private List<(Vec2 A, Vec2 B)>? _lines;

    /// <summary>Pattern line segments clipped to the region (empty for SOLID).</summary>
    public IReadOnlyList<(Vec2 A, Vec2 B)> Lines => _lines ??= BuildLines();

    private List<(Vec2, Vec2)> BuildLines()
    {
        var result = new List<(Vec2, Vec2)>();
        if (IsSolid || Loops.Count == 0) return result;
        double spacing = 3.175 * Scale;
        double[] families = Pattern == "ANSI37" ? [Math.PI / 4, -Math.PI / 4] : [Math.PI / 4];
        foreach (var fam in families) Scan(fam + Angle, spacing, result);
        return result;
    }

    /// <summary>Even-odd scanline fill along lines at <paramref name="ang"/>.</summary>
    private void Scan(double ang, double spacing, List<(Vec2, Vec2)> output)
    {
        // rotate the region so the pattern lines are horizontal
        double c = Math.Cos(-ang), s = Math.Sin(-ang);
        Vec2 Rot(Vec2 p) => new(p.X * c - p.Y * s, p.X * s + p.Y * c);
        Vec2 Back(Vec2 p) => new(p.X * c + p.Y * s, -p.X * s + p.Y * c);
        var edges = new List<(Vec2 A, Vec2 B)>();
        double y0 = double.MaxValue, y1 = double.MinValue;
        foreach (var loop in Loops)
            for (int i = 0; i < loop.Count; i++)
            {
                var a = Rot(loop[i]); var b = Rot(loop[(i + 1) % loop.Count]);
                edges.Add((a, b));
                y0 = Math.Min(y0, a.Y); y1 = Math.Max(y1, a.Y);
            }
        if (edges.Count == 0 || y1 - y0 < 1e-12) return;
        // keep the line count bounded on very large regions
        while ((y1 - y0) / spacing > MaxLines / 2.0) spacing *= 2;
        var xs = new List<double>();
        for (double y = Math.Ceiling(y0 / spacing) * spacing; y <= y1; y += spacing)
        {
            xs.Clear();
            foreach (var (a, b) in edges)
            {
                if ((a.Y > y) == (b.Y > y)) continue;          // half-open rule avoids double hits at vertices
                xs.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                if (xs[i + 1] - xs[i] > 1e-9) output.Add((Back(new Vec2(xs[i], y)), Back(new Vec2(xs[i + 1], y))));
            if (output.Count > MaxLines) return;
        }
    }

    /// <summary>Pattern scale giving roughly <paramref name="linesAcross"/> lines over the region, rounded to 1/2/5×10ⁿ.</summary>
    public static double AutoScale(IEnumerable<IReadOnlyList<Vec2>> loops, double linesAcross = 40)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var l in loops) foreach (var p in l) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        double size = Math.Max(x1 - x0, y1 - y0);
        if (!(size > 0)) return 1;
        double raw = size / (3.175 * linesAcross);
        double e = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        return new[] { 1, 2, 5, 10 }.Select(m => m * e).OrderBy(c => Math.Abs(Math.Log(c / raw))).First();
    }

    /// <summary>Even-odd point containment.</summary>
    public bool Contains(Vec2 p)
    {
        bool inside = false;
        foreach (var loop in Loops)
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                if ((loop[i].Y > p.Y) != (loop[j].Y > p.Y) && p.X < (loop[j].X - loop[i].X) * (p.Y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                    inside = !inside;
        return inside;
    }

    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05) =>
        Loops.Count == 0 ? [] : [.. Loops[0], Loops[0][0]];

    public override DrawingEntity Transformed(Func<Vec2, Vec2> map)
    {
        var o = map(Vec2.Zero); var ux = map(new Vec2(1, 0)) - o;
        return new HatchEntity(Loops.Select(l => (IReadOnlyList<Vec2>)[.. l.Select(map)]), Pattern, Scale * ux.Length, Angle + ux.Angle) { Layer = Layer, Color = Color };
    }
}

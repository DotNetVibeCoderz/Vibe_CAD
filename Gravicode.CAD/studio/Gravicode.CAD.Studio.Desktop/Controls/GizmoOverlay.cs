using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Gravicode.CAD.Studio.Desktop.Controls;

/// <summary>
/// Move / rotate gizmo drawn over the 3D view: three axis arrows (move) or rings (rotate) at <see cref="Pivot"/>.
/// Only the handles are hit-testable, so everywhere else the pointer reaches the orbit camera underneath.
/// Moves snap to <see cref="Snap"/> and rotations to 15°; hold Alt to drag freely.
/// </summary>
public sealed class GizmoOverlay : Control, ICustomHitTest
{
    private static readonly (string Name, Vec3 Dir, Color Color)[] Axes =
    [
        ("X", Vec3.UnitX, Color.Parse("#FF5A4E")), ("Y", Vec3.UnitY, Color.Parse("#5AD17A")), ("Z", Vec3.UnitZ, Color.Parse("#4F8DF7"))
    ];
    private static readonly Color Hover = Color.Parse("#FFD84D");
    private static readonly Typeface Mono = new(new FontFamily("avares://GravicodeCadStudio/Assets/Fonts#JetBrains Mono"), FontStyle.Normal, FontWeight.Medium);
    private const double ArrowPx = 95, RingFactor = 0.8, HitPx = 8;

    /// <summary>null (hidden), "move" or "rotate".</summary>
    public string? Mode { get; set; }
    /// <summary>Gizmo origin in CAD space; null hides it.</summary>
    public Vec3? Pivot { get; set; }
    public ViewCamera Camera { get; set; }
    /// <summary>Move step in model units (0 = free).</summary>
    public double Snap { get; set; }

    public event Action<Vec3>? Moved;
    public event Action<Vec3, double, Vec3>? Rotated;   // axis, radians, pivot

    private int _hover = -1;
    private (int Axis, Point Start, double StartAngle, double Value)? _drag;

    private bool Active => Mode is not null && Pivot is not null && Camera.Viewport.Width > 1;

    // ------------------------------------------------------------------ geometry

    private double Length(Vec3 pivot) => Camera.UnitsForPixels(pivot, ArrowPx);

    private Point? Screen(Vec3 p) { var (q, z) = Camera.Project(p); return z > 0 ? q : null; }

    private static (Vec3 U, Vec3 V) Perp(Vec3 a)
    {
        var u = Vec3.Cross(a, Math.Abs(a.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX).Normalized();
        return (u, Vec3.Cross(a, u).Normalized());
    }

    private List<Point> Ring(Vec3 pivot, Vec3 axis, double radius)
    {
        var (u, v) = Perp(axis);
        var pts = new List<Point>(65);
        for (int i = 0; i <= 64; i++)
        {
            double t = 2 * Math.PI * i / 64;
            if (Screen(pivot + (u * Math.Cos(t) + v * Math.Sin(t)) * radius) is { } q) pts.Add(q);
        }
        return pts;
    }

    private static double SegDist(Point p, Point a, Point b)
    {
        var d = b - a; double len2 = d.X * d.X + d.Y * d.Y;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / len2, 0, 1);
        var c = a + d * t; return Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y));
    }

    private int HandleAt(Point p)
    {
        if (!Active) return -1;
        var pivot = Pivot!.Value; double L = Length(pivot);
        if (Screen(pivot) is not { } pc) return -1;
        int best = -1; double bd = HitPx;
        for (int i = 0; i < 3; i++)
        {
            double d = double.MaxValue;
            if (Mode == "move") { if (Screen(pivot + Axes[i].Dir * L) is { } tip) d = SegDist(p, pc, tip); }
            else { var ring = Ring(pivot, Axes[i].Dir, L * RingFactor); for (int k = 0; k + 1 < ring.Count; k++) d = Math.Min(d, SegDist(p, ring[k], ring[k + 1])); }
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    public bool HitTest(Point point) => _drag is not null || HandleAt(point) >= 0;

    // ------------------------------------------------------------------ input

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag is { } d)
        {
            var pivot = Pivot!.Value; var axis = Axes[d.Axis].Dir;
            double value;
            if (Mode == "move")
            {
                var pc = Screen(pivot)!.Value; var tip = Screen(pivot + axis * Length(pivot))!.Value;
                var s = tip - pc; double len2 = s.X * s.X + s.Y * s.Y;
                value = len2 < 1e-9 ? 0 : ((p.X - d.Start.X) * s.X + (p.Y - d.Start.Y) * s.Y) / len2 * Length(pivot);
                if (Snap > 0 && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)) value = Math.Round(value / Snap) * Snap;
            }
            else
            {
                double delta = ScreenAngle(p) - d.StartAngle;
                while (delta > Math.PI) delta -= 2 * Math.PI;
                while (delta < -Math.PI) delta += 2 * Math.PI;
                // counter-clockwise on screen is positive about an axis that points at the viewer
                value = Camera.FacesViewer(axis) ? delta : -delta;
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt)) value = Math.Round(value / (Math.PI / 12)) * (Math.PI / 12);
            }
            _drag = d with { Value = value };
            InvalidateVisual();
            return;
        }
        int h = HandleAt(p);
        if (h != _hover) { _hover = h; Cursor = h >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default; InvalidateVisual(); }
    }

    private double ScreenAngle(Point p)
    {
        var pc = Screen(Pivot!.Value) ?? default;
        return Math.Atan2(-(p.Y - pc.Y), p.X - pc.X);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        int h = HandleAt(p);
        if (h < 0) return;
        _drag = (h, p, ScreenAngle(p), 0);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not { } d) return;
        _drag = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        var axis = Axes[d.Axis].Dir;
        if (Math.Abs(d.Value) > 1e-9)
        {
            if (Mode == "move") Moved?.Invoke(axis * d.Value);
            else Rotated?.Invoke(axis, d.Value, Pivot!.Value);
        }
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ drawing

    public override void Render(DrawingContext g)
    {
        if (!Active) return;
        var pivot = Pivot!.Value;
        double L = Length(pivot);
        // while moving, draw the gizmo where the selection will land
        if (_drag is { } d && Mode == "move") pivot += Axes[d.Axis].Dir * d.Value;
        if (Screen(pivot) is not { } pc) return;

        for (int i = 0; i < 3; i++)
        {
            bool hot = i == _hover || _drag?.Axis == i;
            var color = hot ? Hover : Axes[i].Color;
            var pen = new Pen(new SolidColorBrush(color), hot ? 3 : 2.2, lineCap: PenLineCap.Round);
            if (Mode == "move")
            {
                if (Screen(pivot + Axes[i].Dir * L) is not { } tip) continue;
                g.DrawLine(pen, pc, tip);
                var dir = tip - pc; double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                if (len < 1e-6) continue;
                var u = dir / len; var n = new Point(-u.Y, u.X);
                var head = new StreamGeometry();
                using (var k = head.Open())
                {
                    k.BeginFigure(tip + u * 12, true);
                    k.LineTo(tip + n * 6); k.LineTo(tip - n * 6);
                    k.EndFigure(true);
                }
                g.DrawGeometry(new SolidColorBrush(color), null, head);
                g.DrawText(new FormattedText(Axes[i].Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 11, pen.Brush), tip + u * 16 - new Point(4, 7));
            }
            else
            {
                var ring = Ring(pivot, Axes[i].Dir, L * RingFactor);
                if (ring.Count < 2) continue;
                var geo = new StreamGeometry();
                using (var k = geo.Open())
                {
                    k.BeginFigure(ring[0], false);
                    foreach (var q in ring.Skip(1)) k.LineTo(q);
                    k.EndFigure(false);
                }
                g.DrawGeometry(null, pen, geo);
            }
        }
        g.DrawEllipse(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(Color.Parse("#0A1A30")), 1), pc, 3.5, 3.5);

        if (_drag is { } dd)
        {
            string text = Mode == "move"
                ? $"{Axes[dd.Axis].Name} {(dd.Value >= 0 ? "+" : "")}{dd.Value.ToString("0.###", CultureInfo.InvariantCulture)}"
                : $"{Axes[dd.Axis].Name} {Units.ToDegrees(dd.Value).ToString("0.#", CultureInfo.InvariantCulture)}°";
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 13, new SolidColorBrush(Colors.White));
            var at = pc + new Point(14, -30);
            g.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 14, 16, 20)), new Pen(new SolidColorBrush(Hover), 1), new Rect(at.X - 6, at.Y - 3, ft.Width + 12, ft.Height + 6), 3, 3);
            g.DrawText(ft, at);
        }
    }
}

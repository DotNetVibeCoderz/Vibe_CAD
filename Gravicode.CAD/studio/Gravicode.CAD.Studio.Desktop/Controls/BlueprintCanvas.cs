using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Gravicode.CAD.Studio.Commands;
using Gravicode.CAD.Studio.UI;

namespace Gravicode.CAD.Studio.Desktop.Controls;

/// <summary>
/// The blueprint drafting surface: model space on cyanotype paper with rulers, grid, snap markers, crosshair
/// and window/crossing selection. Talks to the <see cref="CommandEngine"/> directly (same process).
/// Mirrors the web edition's studioCanvas.js so both editions feel identical.
/// </summary>
public sealed class BlueprintCanvas : Control
{
    private const double Ruler = 22;
    private static readonly Color Paper = Color.Parse("#0F2747"), PaperEdge = Color.Parse("#0B1E38");
    private static readonly IBrush RulerBg = new SolidColorBrush(Color.Parse("#0A1A30"));
    private static readonly IBrush RulerCorner = new SolidColorBrush(Color.Parse("#081426"));
    private static readonly IBrush RulerText = new SolidColorBrush(Color.FromArgb(158, 214, 231, 255));
    private static readonly IPen RulerTick = new Pen(new SolidColorBrush(Color.FromArgb(64, 214, 231, 255)), 1);
    private static readonly IPen GridMinor = new Pen(new SolidColorBrush(Color.FromArgb(14, 214, 231, 255)), 1);
    private static readonly IPen GridMajor = new Pen(new SolidColorBrush(Color.FromArgb(33, 214, 231, 255)), 1);
    private static readonly Color AxisX = Color.FromArgb(140, 255, 120, 110), AxisY = Color.FromArgb(140, 120, 230, 160);
    private static readonly Color Select = Color.Parse("#FF8A3D"), PreviewColor = Color.Parse("#FFB46B"), SnapColor = Color.Parse("#FFD84D");
    private static readonly Typeface Mono = new(new FontFamily("avares://GravicodeCadStudio/Assets/Fonts#JetBrains Mono"));
    private static readonly Typeface MonoBold = new(new FontFamily("avares://GravicodeCadStudio/Assets/Fonts#JetBrains Mono"), FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface Label = new(new FontFamily("avares://GravicodeCadStudio/Assets/Fonts#Barlow Condensed"), FontStyle.Normal, FontWeight.Medium);

    private readonly Dictionary<string, (IBrush Brush, IPen Pen, IPen Solid)> _paint = [];
    private List<DisplayItem> _items = [];
    private List<DisplayItem> _preview = [];
    private SnapResult? _snap;
    private Point? _mouse;
    private (Point Start, bool Pan, bool Moved, double Cx, double Cy)? _drag;
    private double _cx, _cy, _scale = 2;
    private double[]? _fit;
    private Size _lastSize;
    private int _shownVersion = -1, _shownSelection = -1;
    private StudioDocument? _shownDoc;
    private bool _space, _zoomPending;

    public CommandEngine? Engine { get; set; }
    public event Action<SnapResult>? CursorMoved;

    public BlueprintCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.None);
    }

    // ------------------------------------------------------------------ scene
    /// <summary>Rebuilds the display list if the document, its version or the selection changed.</summary>
    public void Refresh(bool force = false)
    {
        if (Engine is null) return;
        var doc = Engine.Document;
        // grips depend on the selection, the hot grip and whether a command is running
        int sel = Engine.Selection.Aggregate(HashCode.Combine(Engine.Selection.Count, Engine.HotGrip, Engine.IsBusy), (h, g) => h ^ g.GetHashCode());
        if (force || doc != _shownDoc || doc.Version != _shownVersion || sel != _shownSelection)
        {
            _shownDoc = doc; _shownVersion = doc.Version; _shownSelection = sel;
            _items = DisplayList.Build(Engine);
        }
        UpdatePreview();
        InvalidateVisual();
    }

    /// <summary>Fits the drawing on the next render, when the control's final size is known.</summary>
    public void ZoomExtents() { _zoomPending = true; InvalidateVisual(); }

    private void ApplyZoomExtents(bool invalidate = true)
    {
        _zoomPending = false;
        if (Engine is null) return;
        var bb = Engine.Document.Bounds;
        if (bb.IsEmpty) ZoomTo(-50, -50, 250, 200, invalidate);
        else ZoomTo(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y, invalidate);
    }

    /// <param name="invalidate">False when called from <see cref="Render"/> (Avalonia forbids invalidating mid-render).</param>
    private void ZoomTo(double minX, double minY, double maxX, double maxY, bool invalidate = true)
    {
        _fit = [minX, minY, maxX, maxY];
        double w = Math.Max(1, Bounds.Width - Ruler), h = Math.Max(1, Bounds.Height - Ruler);
        double sx = Math.Max(maxX - minX, 1e-6), sy = Math.Max(maxY - minY, 1e-6);
        _scale = Math.Clamp(Math.Min(w / sx, h / sy) * 0.88, 1e-5, 1e5);
        _cx = (minX + maxX) / 2; _cy = (minY + maxY) / 2;
        if (invalidate) InvalidateVisual();
    }

    private Point ToScreen(double x, double y) => new(Ruler + (Bounds.Width - Ruler) / 2 + (x - _cx) * _scale, Ruler + (Bounds.Height - Ruler) / 2 - (y - _cy) * _scale);
    private Vec2 ToWorld(Point p) => new(_cx + (p.X - Ruler - (Bounds.Width - Ruler) / 2) / _scale, _cy - (p.Y - Ruler - (Bounds.Height - Ruler) / 2) / _scale);

    private void UpdatePreview()
    {
        if (Engine is null || _mouse is null) { _preview = []; return; }
        var snap = Engine.Snap(ToWorld(_mouse.Value), 10 / _scale);
        _snap = snap;
        _preview = Engine.Preview(snap.Point).SelectMany(g => DisplayList.Items(g, "#FFB46B", false)).ToList();
        CursorMoved?.Invoke(snap);
    }

    // ------------------------------------------------------------------ input
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        var p = pt.Position;
        if (pt.Properties.IsMiddleButtonPressed || (pt.Properties.IsLeftButtonPressed && _space))
        {
            if (e.ClickCount == 2 && pt.Properties.IsMiddleButtonPressed) { ZoomExtents(); return; }
            _drag = (p, true, false, _cx, _cy); e.Pointer.Capture(this); e.Handled = true; return;
        }
        if (pt.Properties.IsRightButtonPressed) { Engine?.Enter(); Changed(); e.Handled = true; return; }
        if (pt.Properties.IsLeftButtonPressed) { _drag = (p, false, false, 0, 0); e.Pointer.Capture(this); }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _mouse = p;
        if (_drag is { Pan: true } d)
        {
            _fit = null;
            _cx = d.Cx - (p.X - d.Start.X) / _scale;
            _cy = d.Cy + (p.Y - d.Start.Y) / _scale;
            InvalidateVisual(); return;
        }
        if (_drag is { } d2 && !d2.Moved && Distance(p, d2.Start) > 5) _drag = d2 with { Moved = true };
        UpdatePreview();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var d = _drag; _drag = null;
        e.Pointer.Capture(null);
        if (d is null || d.Value.Pan || Engine is null) { InvalidateVisual(); return; }
        var p = e.GetPosition(this);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (d.Value.Moved) Engine.SelectWindow(ToWorld(d.Value.Start), ToWorld(p), shift);
        else
        {
            var w = _snap is { Kind: not SnapKind.None } s ? s.Point : ToWorld(p);
            Engine.Click(w, 7 / _scale, shift);
        }
        Changed();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouse = null; _snap = null; _preview = [];
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var before = ToWorld(p);
        _scale = Math.Clamp(_scale * Math.Exp(e.Delta.Y * 0.18), 1e-5, 1e5);
        var after = ToWorld(p);
        _fit = null;
        _cx += before.X - after.X; _cy += before.Y - after.Y;
        UpdatePreview();
        InvalidateVisual();
        e.Handled = true;
    }

    public void SetSpace(bool down) => _space = down;

    private void Changed() { Refresh(); ChangedByUser?.Invoke(); }
    /// <summary>Raised after a click, window or Enter changed engine state.</summary>
    public event Action? ChangedByUser;

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ------------------------------------------------------------------ painting
    public override void Render(DrawingContext g)
    {
        double W = Bounds.Width, H = Bounds.Height;
        if (W < 2 || H < 2) return;
        if (_zoomPending) { _lastSize = Bounds.Size; ApplyZoomExtents(invalidate: false); }
        else if (Bounds.Size != _lastSize)
        {
            _lastSize = Bounds.Size;
            if (_fit is { } f) ZoomTo(f[0], f[1], f[2], f[3], invalidate: false);
        }

        var bg = new RadialGradientBrush
        {
            Center = new RelativePoint(0.55, 0.45, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.55, 0.45, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Paper, 0), new GradientStop(PaperEdge, 1) }
        };
        g.DrawRectangle(bg, null, new Rect(0, 0, W, H));

        using (g.PushClip(new Rect(Ruler, Ruler, W - Ruler, H - Ruler)))
        {
            if (Engine?.Settings.ShowGrid != false) DrawGrid(g);
            DrawAxes(g);
            foreach (var it in _items) if (it.Solid) DrawItem(g, it, false);
            foreach (var it in _items) if (!it.Solid && it.Kind != "grip") DrawItem(g, it, false);
            foreach (var it in _items) if (it.Selected) DrawItem(g, it, true);
            foreach (var it in _items) if (it.Kind == "grip") DrawItem(g, it, false);
            foreach (var it in _preview) DrawItem(g, it, false, preview: true);
            DrawWindow(g);
            DrawSnap(g);
            DrawCrosshair(g);
            DrawUcs(g);
        }
        DrawRulers(g);
    }

    private double GridStep()
    {
        double step = Engine?.Settings.GridSize ?? 10;
        if (step <= 0) step = 10;
        while (step * _scale < 8) step *= 5;
        while (step * _scale > 80) step /= 5;
        return step;
    }

    private void DrawGrid(DrawingContext g)
    {
        double step = GridStep();
        var tl = ToWorld(new Point(Ruler, Ruler)); var br = ToWorld(new Point(Bounds.Width, Bounds.Height));
        foreach (var major in new[] { false, true })
        {
            double s = major ? step * 5 : step;
            var pen = major ? GridMajor : GridMinor;
            for (double x = Math.Floor(tl.X / s) * s; x <= br.X; x += s)
            {
                double px = Math.Round(ToScreen(x, 0).X) + 0.5;
                g.DrawLine(pen, new Point(px, Ruler), new Point(px, Bounds.Height));
            }
            for (double y = Math.Floor(br.Y / s) * s; y <= tl.Y; y += s)
            {
                double py = Math.Round(ToScreen(0, y).Y) + 0.5;
                g.DrawLine(pen, new Point(Ruler, py), new Point(Bounds.Width, py));
            }
        }
    }

    private void DrawAxes(DrawingContext g)
    {
        var o = ToScreen(0, 0);
        g.DrawLine(new Pen(new SolidColorBrush(AxisX), 1), new Point(Ruler, o.Y), new Point(Bounds.Width, o.Y));
        g.DrawLine(new Pen(new SolidColorBrush(AxisY), 1), new Point(o.X, Ruler), new Point(o.X, Bounds.Height));
    }

    private (IBrush Brush, IPen Pen, IPen Solid) PaintFor(string color)
    {
        if (_paint.TryGetValue(color, out var p)) return p;
        var c = Color.TryParse(color, out var parsed) ? parsed : Colors.White;
        var brush = new SolidColorBrush(c);
        p = (brush, new Pen(brush, 1.35), new Pen(new SolidColorBrush(c, 0.85), 1.1));
        _paint[color] = p;
        return p;
    }

    private static readonly IPen SelectPen = new Pen(new SolidColorBrush(Select), 2.2, new DashStyle([3.2, 1.8], 0));
    private static readonly IPen SelectGlow = new Pen(new SolidColorBrush(Select, 0.28), 6);
    private static readonly IPen GripOutline = new Pen(new SolidColorBrush(Color.Parse("#0A1A30")), 1);
    private static readonly IPen PreviewPen = new Pen(new SolidColorBrush(PreviewColor), 1.35, new DashStyle([4.5, 3], 0));

    private void DrawItem(DrawingContext g, DisplayItem it, bool highlight, bool preview = false)
    {
        var paint = PaintFor(it.Color);
        IPen pen = preview ? PreviewPen : highlight ? SelectPen : it.Solid ? paint.Solid : paint.Pen;
        IBrush brush = preview ? PreviewPen.Brush! : highlight ? SelectPen.Brush! : paint.Brush;
        var p = it.Points;
        switch (it.Kind)
        {
            case "grip":
                {
                    var c = ToScreen(p[0], p[1]); double r = it.Hot ? 5.5 : 4.5;
                    g.DrawRectangle(paint.Brush, GripOutline, new Rect(Math.Round(c.X) - r, Math.Round(c.Y) - r, 2 * r, 2 * r));
                    break;
                }
            case "fill":
                {
                    var geo = new StreamGeometry();
                    using (var k = geo.Open())
                    {
                        k.SetFillRule(FillRule.EvenOdd);
                        int at = 0;
                        foreach (var n in it.Rings ?? [p.Length / 2])
                        {
                            for (int i = 0; i < n; i++, at++)
                            {
                                var q = ToScreen(p[2 * at], p[2 * at + 1]);
                                if (i == 0) k.BeginFigure(q, true); else k.LineTo(q);
                            }
                            k.EndFigure(true);
                        }
                    }
                    var fill = new SolidColorBrush(((ISolidColorBrush)brush).Color, highlight ? 0.35 : 0.5);
                    g.DrawGeometry(fill, pen, geo);
                    break;
                }
            case "poly":
            case "segs":
                {
                    var geo = new StreamGeometry();
                    using (var c = geo.Open())
                    {
                        if (it.Kind == "poly")
                        {
                            if (p.Length < 4) break;
                            c.BeginFigure(ToScreen(p[0], p[1]), false);
                            for (int i = 2; i + 1 < p.Length; i += 2) c.LineTo(ToScreen(p[i], p[i + 1]));
                            c.EndFigure(it.Closed);
                        }
                        else
                            for (int i = 0; i + 3 < p.Length; i += 4)
                            {
                                c.BeginFigure(ToScreen(p[i], p[i + 1]), false);
                                c.LineTo(ToScreen(p[i + 2], p[i + 3]));
                                c.EndFigure(false);
                            }
                    }
                    if (highlight) g.DrawGeometry(null, SelectGlow, geo);
                    g.DrawGeometry(null, pen, geo);
                    break;
                }
            case "circle":
                {
                    var c = ToScreen(p[0], p[1]); double r = Math.Max(0.5, p[2] * _scale);
                    if (highlight) g.DrawEllipse(null, SelectGlow, c, r, r);
                    g.DrawEllipse(null, pen, c, r, r);
                    break;
                }
            case "point":
                {
                    var c = ToScreen(p[0], p[1]);
                    g.DrawLine(pen, new Point(c.X - 4, c.Y), new Point(c.X + 4, c.Y));
                    g.DrawLine(pen, new Point(c.X, c.Y - 4), new Point(c.X, c.Y + 4));
                    break;
                }
            case "text":
                {
                    double size = it.Height * _scale;
                    if (size < 3 || string.IsNullOrEmpty(it.Text)) break;
                    if (size > 400) size = 400;
                    var c = ToScreen(p[0], p[1]);
                    var ft = new FormattedText(it.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Label, size * 1.25, brush);
                    double a = p.Length > 2 ? -p[2] : 0;
                    if (a < -Math.PI / 2 || a > Math.PI / 2) a += Math.PI;
                    // baseline at the insertion point
                    var m = Matrix.CreateTranslation(0, -ft.Baseline) * Matrix.CreateRotation(a) * Matrix.CreateTranslation(c.X, c.Y);
                    using (g.PushTransform(m)) g.DrawText(ft, new Point(0, 0));
                    break;
                }
        }
    }

    private void DrawWindow(DrawingContext g)
    {
        if (_drag is not { Pan: false, Moved: true } d || _mouse is null) return;
        var m = _mouse.Value;
        bool crossing = m.X < d.Start.X;
        var rect = new Rect(d.Start, m).Normalize();
        var fill = new SolidColorBrush(crossing ? Color.FromArgb(36, 72, 199, 142) : Color.FromArgb(41, 79, 141, 247));
        var edge = new Pen(new SolidColorBrush(crossing ? Color.Parse("#48C78E") : Color.Parse("#4F8DF7")), 1, crossing ? new DashStyle([5, 4], 0) : null);
        g.DrawRectangle(fill, edge, rect);
    }

    private void DrawSnap(DrawingContext g)
    {
        if (_snap is not { } s || s.Kind == SnapKind.None || _mouse is null) return;
        var c = ToScreen(s.Point.X, s.Point.Y); const double r = 6;
        var pen = new Pen(new SolidColorBrush(SnapColor), 2);
        var geo = new StreamGeometry();
        using (var k = geo.Open())
        {
            switch (s.Kind)
            {
                case SnapKind.Endpoint: Poly(k, true, (c.X - r, c.Y - r), (c.X + r, c.Y - r), (c.X + r, c.Y + r), (c.X - r, c.Y + r)); break;
                case SnapKind.Midpoint: Poly(k, true, (c.X, c.Y - r - 1), (c.X + r + 1, c.Y + r), (c.X - r - 1, c.Y + r)); break;
                case SnapKind.Quadrant: Poly(k, true, (c.X, c.Y - r - 1), (c.X + r + 1, c.Y), (c.X, c.Y + r + 1), (c.X - r - 1, c.Y)); break;
                case SnapKind.Intersection: Poly(k, false, (c.X - r, c.Y - r), (c.X + r, c.Y + r)); Poly(k, false, (c.X + r, c.Y - r), (c.X - r, c.Y + r)); break;
                case SnapKind.Center: break;
                default: Poly(k, false, (c.X - 3, c.Y), (c.X + 3, c.Y)); Poly(k, false, (c.X, c.Y - 3), (c.X, c.Y + 3)); break;
            }
        }
        if (s.Kind == SnapKind.Center) g.DrawEllipse(null, pen, c, r, r);
        else g.DrawGeometry(null, pen, geo);
        if (s.Kind.ToString() != "Grid")
        {
            var ft = new FormattedText(s.Kind.ToString().ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoBold, 10, pen.Brush);
            g.DrawText(ft, new Point(c.X + 10, c.Y - 22));
        }
    }

    private static void Poly(StreamGeometryContext k, bool closed, params (double X, double Y)[] pts)
    {
        k.BeginFigure(new Point(pts[0].X, pts[0].Y), false);
        foreach (var p in pts.Skip(1)) k.LineTo(new Point(p.X, p.Y));
        k.EndFigure(closed);
    }

    private static readonly IPen CrossPen = new Pen(new SolidColorBrush(Color.FromArgb(217, 234, 242, 255)), 1);

    private void DrawCrosshair(DrawingContext g)
    {
        if (_mouse is null || _drag is { Pan: true }) return;
        var p = _snap is { Kind: not SnapKind.None } s ? ToScreen(s.Point.X, s.Point.Y) : _mouse.Value;
        double x = Math.Round(p.X) + 0.5, y = Math.Round(p.Y) + 0.5;
        g.DrawLine(CrossPen, new Point(x - 46, y), new Point(x - 5, y));
        g.DrawLine(CrossPen, new Point(x + 5, y), new Point(x + 46, y));
        g.DrawLine(CrossPen, new Point(x, y - 46), new Point(x, y - 5));
        g.DrawLine(CrossPen, new Point(x, y + 5), new Point(x, y + 46));
        g.DrawRectangle(null, CrossPen, new Rect(x - 4.5, y - 4.5, 9, 9));
    }

    private void DrawUcs(DrawingContext g)
    {
        double x = Ruler + 26, y = Bounds.Height - 26;
        var px = new Pen(new SolidColorBrush(AxisX), 1.6); var py = new Pen(new SolidColorBrush(AxisY), 1.6);
        g.DrawLine(px, new Point(x, y), new Point(x + 34, y)); g.DrawLine(px, new Point(x + 34, y), new Point(x + 28, y - 4)); g.DrawLine(px, new Point(x + 34, y), new Point(x + 28, y + 4));
        g.DrawLine(py, new Point(x, y), new Point(x, y - 34)); g.DrawLine(py, new Point(x, y - 34), new Point(x - 4, y - 28)); g.DrawLine(py, new Point(x, y - 34), new Point(x + 4, y - 28));
        g.DrawText(new FormattedText("X", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoBold, 10, px.Brush), new Point(x + 38, y - 7));
        g.DrawText(new FormattedText("Y", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoBold, 10, py.Brush), new Point(x - 3, y - 52));
        g.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(153, 234, 242, 255)), 1), new Rect(x - 3, y - 3, 6, 6));
    }

    private void DrawRulers(DrawingContext g)
    {
        double W = Bounds.Width, H = Bounds.Height, step = GridStep() * 5;
        g.DrawRectangle(RulerBg, null, new Rect(0, 0, W, Ruler));
        g.DrawRectangle(RulerBg, null, new Rect(0, 0, Ruler, H));
        var tl = ToWorld(new Point(Ruler, Ruler)); var br = ToWorld(new Point(W, H));
        static string Fmt(double v) => Math.Abs(v) >= 1e4 ? (v / 1000).ToString("0", CultureInfo.InvariantCulture) + "k" : Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);
        for (double x = Math.Floor(tl.X / step) * step; x <= br.X; x += step)
        {
            double sx = ToScreen(x, 0).X; if (sx < Ruler) continue;
            g.DrawLine(RulerTick, new Point(sx + 0.5, Ruler - 8), new Point(sx + 0.5, Ruler));
            g.DrawText(new FormattedText(Fmt(x), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, RulerText), new Point(sx + 3, 2));
            for (int k = 1; k < 5; k++) { double t = sx + k * step * _scale / 5; g.DrawLine(RulerTick, new Point(t + 0.5, Ruler - 4), new Point(t + 0.5, Ruler)); }
        }
        for (double y = Math.Floor(br.Y / step) * step; y <= tl.Y; y += step)
        {
            double sy = ToScreen(0, y).Y; if (sy < Ruler) continue;
            g.DrawLine(RulerTick, new Point(Ruler - 8, sy + 0.5), new Point(Ruler, sy + 0.5));
            var ft = new FormattedText(Fmt(y), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, RulerText);
            using (g.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(2, sy - 3))) g.DrawText(ft, new Point(0, 0));
            for (int k = 1; k < 5; k++) { double t = sy - k * step * _scale / 5; g.DrawLine(RulerTick, new Point(Ruler - 4, t + 0.5), new Point(Ruler, t + 0.5)); }
        }
        if (_mouse is { } m)
        {
            var sel = new SolidColorBrush(Select);
            g.DrawRectangle(sel, null, new Rect(m.X - 0.5, 0, 1.5, Ruler));
            g.DrawRectangle(sel, null, new Rect(0, m.Y - 0.5, Ruler, 1.5));
        }
        g.DrawRectangle(RulerCorner, null, new Rect(0, 0, Ruler, Ruler));
    }
}

namespace Gravicode.CAD.Rendering;

/// <summary>
/// UI-framework-neutral interactive viewport logic (orbit / pan / zoom + progressive rendering) shared by the
/// WPF, WinForms and MAUI viewers. Hosts forward pointer events and display the produced <see cref="RgbaImage"/>.
/// </summary>
public sealed class InteractiveView
{
    private CadModel? _model;
    private CancellationTokenSource? _cts;
    private int _version;

    public OrbitCamera Camera { get; } = new();
    public RenderOptions Options { get; set; } = new() { Supersample = 2 };
    public CadModel? Model
    {
        get => _model;
        set { _model = value; if (value != null) Camera.Fit(value.Bounds); Invalidate(); }
    }

    /// <summary>Raised (on a worker thread) when a new frame is ready.</summary>
    public event Action<RgbaImage>? FrameReady;

    public int Width { get; private set; } = 800;
    public int Height { get; private set; } = 600;

    public void Resize(int width, int height) { Width = Math.Max(16, width); Height = Math.Max(16, height); Invalidate(); }

    private (double X, double Y)? _last; private bool _pan;
    public void PointerDown(double x, double y, bool pan) { _last = (x, y); _pan = pan; }
    public void PointerUp() { _last = null; Invalidate(); }
    public void PointerMove(double x, double y)
    {
        if (_last is not { } l) return;
        double dx = x - l.X, dy = y - l.Y;
        _last = (x, y);
        if (_pan)
        {
            var (r, u, _) = Camera.Basis();
            double s = 2 * Camera.Distance * Math.Tan(Units.Deg(Camera.FieldOfView) / 2) / Height;
            Camera.Target = Camera.Target - r * (dx * s) + u * (dy * s);
        }
        else
        {
            Camera.Yaw -= dx * 0.45;
            Camera.Pitch = Math.Clamp(Camera.Pitch + dy * 0.45, -89, 89);
        }
        Invalidate(interactive: true);
    }
    public void Wheel(double delta) { Camera.Distance *= Math.Exp(-delta * 0.0012); Invalidate(); }
    public void Fit() { if (_model != null) Camera.Fit(_model.Bounds); Invalidate(); }
    public void SetView(double yaw, double pitch) { Camera.Yaw = yaw; Camera.Pitch = pitch; Fit(); }

    /// <summary>Schedules a render; interactive frames use no supersampling for responsiveness.</summary>
    public void Invalidate(bool interactive = false)
    {
        if (_model is null) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        int v = Interlocked.Increment(ref _version);
        var opts = Options with { Width = Width, Height = Height, Camera = Clone(Camera), Supersample = interactive ? 1 : Options.Supersample };
        var model = _model;
        Task.Run(() =>
        {
            if (cts.IsCancellationRequested) return;
            var img = SoftwareRenderer.Render(model, opts);
            if (!cts.IsCancellationRequested && v == Volatile.Read(ref _version)) FrameReady?.Invoke(img);
        });
    }

    private static OrbitCamera Clone(OrbitCamera c) => new()
    {
        Target = c.Target, Distance = c.Distance, Yaw = c.Yaw, Pitch = c.Pitch, FieldOfView = c.FieldOfView,
        Orthographic = c.Orthographic, OrthoHeight = c.OrthoHeight
    };
}

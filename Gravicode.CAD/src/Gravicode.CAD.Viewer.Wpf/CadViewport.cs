using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gravicode.CAD.IO;
using Gravicode.CAD.Rendering;

namespace Gravicode.CAD.Viewer.Wpf;

/// <summary>
/// WPF CAD viewport. <c>&lt;gcad:CadViewport Model="{Binding Model}" /&gt;</c> or <c>Source="engine.step"</c>.
/// Left-drag orbits, right/middle-drag pans, wheel zooms, double-click fits.
/// </summary>
public class CadViewport : Border
{
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly InteractiveView _view = new();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(CadModel), typeof(CadViewport), new PropertyMetadata(null, (d, e) => ((CadViewport)d)._view.Model = (CadModel?)e.NewValue));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(CadViewport), new PropertyMetadata(null, (d, e) => ((CadViewport)d).LoadSource((string?)e.NewValue)));

    public CadModel? Model { get => (CadModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    /// <summary>Path of a CAD file to load (STEP, STL, OBJ, glTF, 3MF, DXF, IGES).</summary>
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public InteractiveView View => _view;

    public CadViewport()
    {
        Child = _image;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
        ClipToBounds = true;
        _view.FrameReady += img => Dispatcher.BeginInvoke(() => _image.Source = ToBitmap(img));
        SizeChanged += (_, e) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _view.Resize((int)(e.NewSize.Width * dpi.DpiScaleX), (int)(e.NewSize.Height * dpi.DpiScaleY));
        };
        MouseDown += (_, e) =>
        {
            if (e.ClickCount == 2) { _view.Fit(); return; }
            var p = e.GetPosition(this);
            _view.PointerDown(p.X, p.Y, e.ChangedButton != MouseButton.Left);
            CaptureMouse();
        };
        MouseMove += (_, e) => { var p = e.GetPosition(this); _view.PointerMove(p.X, p.Y); };
        MouseUp += (_, _) => { _view.PointerUp(); ReleaseMouseCapture(); };
        MouseWheel += (_, e) => _view.Wheel(e.Delta);
    }

    /// <summary>Loads a CAD file into the viewport (same as setting <see cref="Source"/>).</summary>
    public void Load(string path) => Source = path;

    private void LoadSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        CadIO.RegisterAll();
        Task.Run(() => CadModel.Load(path)).ContinueWith(t =>
        {
            if (t.Exception is null) Model = t.Result;
            else MessageBox.Show(t.Exception.InnerException?.Message, "Gravicode.CAD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static BitmapSource ToBitmap(RgbaImage img)
    {
        var bgra = new byte[img.Pixels.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = img.Pixels[i + 2]; bgra[i + 1] = img.Pixels[i + 1]; bgra[i + 2] = img.Pixels[i]; bgra[i + 3] = img.Pixels[i + 3];
        }
        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, bgra, img.Width * 4);
        bmp.Freeze();
        return bmp;
    }
}

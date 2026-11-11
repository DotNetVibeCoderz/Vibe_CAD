using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Gravicode.CAD.Rendering;
using Gravicode.CAD.ThreeNet;
using ThreeNet.Avalonia;

namespace Gravicode.CAD.Gallery.Desktop;

public partial class MainWindow : Window
{
    private readonly CadScene _scene;
    private readonly OrbitController _orbit;
    private readonly Dictionary<string, CadModel> _cache = [];
    private CadModel? _model;
    private string _title = "";
    private bool _gpuFailed;

    public MainWindow()
    {
        InitializeComponent();
        _scene = new CadScene(light: true);
        View.Scene = _scene.Scene;
        View.Camera = _scene.Camera;
        _orbit = new OrbitController(View, _scene.Camera);
        View.Frame += (_, _) => _orbit.Apply();
        View.RenderFailed += (_, e) => Dispatcher.UIThread.Post(() => { _gpuFailed = true; ShowFallback(); });
        RendererInfo.Text = $"kernel: {MeshKernel.Current.Name}";

        // index grouped by category: headers are non-selectable text rows
        var items = new List<Control>();
        foreach (var cat in SampleCatalog.Categories)
        {
            var group = SampleCatalog.All.Where(s => s.Category == cat).ToList();
            if (group.Count == 0) continue;
            items.Add(new ListBoxItem { Content = new TextBlock { Text = cat.ToUpperInvariant(), FontWeight = FontWeight.SemiBold, FontSize = 12, Foreground = (IBrush)this.FindResource("Ink")! }, IsEnabled = false, Margin = new(0, 10, 0, 0) });
            foreach (var s in group)
                items.Add(new ListBoxItem
                {
                    Tag = s,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = $"{SampleCatalog.All.ToList().IndexOf(s) + 1:00}", FontFamily = (FontFamily)this.FindResource("Mono")!, FontSize = 11, Foreground = (IBrush)this.FindResource("Pencil")!, VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = s.Title, FontSize = 13.5, Foreground = (IBrush)this.FindResource("Ink")! }
                        }
                    }
                });
        }
        Index.ItemsSource = items;
        foreach (var ext in new[] { "step", "stl", "glb", "3mf", "obj", "dxf", "pdf", "svg" })
        {
            var b = new Button { Content = "." + ext, Classes = { "export" }, Margin = new(0, 0, 6, 6), Tag = ext };
            b.Click += OnExport;
            Exports.Children.Add(b);
        }
        Opened += (_, _) => Index.SelectedItem = items.OfType<ListBoxItem>().First(i => i.Tag is Sample);
    }

    private async void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (Index.SelectedItem is not ListBoxItem { Tag: Sample s }) return;
        Summary.Text = s.Summary;
        Code.Text = s.Code;
        TbCat.Text = s.Category;
        TbSheet.Text = $"{SampleCatalog.All.ToList().IndexOf(s) + 1:00} / {SampleCatalog.All.Count}";
        Building.IsVisible = true;
        var sw = Stopwatch.StartNew();
        if (!_cache.TryGetValue(s.Id, out var model))
            _cache[s.Id] = model = await Task.Run(s.Build);
        Show(model, s.Title, $"built in {sw.ElapsedMilliseconds} ms");
    }

    private void Show(CadModel model, string title, string status)
    {
        _model = model; _title = title;
        Building.IsVisible = false;
        TbTitle.Text = title.ToUpperInvariant();
        var bb = model.Bounds;
        TbSize.Text = $"{bb.Size.X:0.#} × {bb.Size.Y:0.#} × {bb.Size.Z:0.#} {model.Units.Symbol()}";
        Facts.ItemsSource = Sample.Facts(model).Select(f => new TextBlock
        {
            Text = $"{f.Label,-11} {f.Value}", FontFamily = (FontFamily)this.FindResource("Mono")!, FontSize = 12, Foreground = (IBrush)this.FindResource("Ink")!
        }).ToList();
        _scene.SetModel(model);
        var (center, radius) = _scene.Frame();
        _orbit.Target = center;
        _orbit.Distance = radius / MathF.Sin(17.5f * MathF.PI / 180f) * 1.1f;
        _orbit.Yaw = -0.6f; _orbit.Pitch = 0.45f;
        Status.Text = $"{model.TriangleCount:N0} triangles · {status}";
        if (_gpuFailed) ShowFallback();
    }

    /// <summary>Software-rendered still image when no GPU adapter is available (e.g. remote desktop, VMs).</summary>
    private void ShowFallback()
    {
        View.IsVisible = false;
        Fallback.IsVisible = true;
        if (_model is null) return;
        var img = SoftwareRenderer.Render(_model, Gravicode.CAD.Rendering.RenderOptions.Light with { Width = 1200, Height = 760 });
        using var ms = new MemoryStream(img.EncodePng());
        Fallback.Source = new Bitmap(ms);
        RendererInfo.Text = "GPU unavailable — software renderer";
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_model is null || sender is not Button { Tag: string ext }) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"{_title.Replace(' ', '_')}.{ext}",
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(ext.ToUpperInvariant()) { Patterns = [$"*.{ext}"] }]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        _model.Save(stream, ext, new ExportOptions { Title = _title });
        Status.Text = $"saved {file.Name}";
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a CAD file",
            FileTypeFilter = [new FilePickerFileType("CAD files") { Patterns = ["*.step", "*.stp", "*.stl", "*.obj", "*.glb", "*.gltf", "*.3mf", "*.dxf", "*.igs", "*.iges", "*.svg"] }]
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;
        Building.IsVisible = true;
        try
        {
            var sw = Stopwatch.StartNew();
            var model = await Task.Run(() => CadModel.Load(path));
            Index.SelectedItem = null;
            Summary.Text = string.Join("\n", model.Warnings.DefaultIfEmpty($"Opened {Path.GetFileName(path)}."));
            Code.Text = $"var model = CadModel.Load(@\"{path}\");";
            TbCat.Text = "File"; TbSheet.Text = "—";
            Show(model, model.Name, $"loaded in {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { Building.IsVisible = false; Status.Text = "Could not open the file: " + ex.Message; }
    }
}

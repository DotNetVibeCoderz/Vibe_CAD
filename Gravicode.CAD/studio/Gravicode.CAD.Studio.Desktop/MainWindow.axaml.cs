using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Rendering;
using Gravicode.CAD.Studio.Catalog;
using Gravicode.CAD.Studio.Commands;
using Gravicode.CAD.Studio.IO;
using Gravicode.CAD.Studio.UI;
using Gravicode.CAD.ThreeNet;
using ThreeNet.Avalonia;

namespace Gravicode.CAD.Studio.Desktop;

/// <summary>Gravicode CAD Studio desktop shell: start screen + editor (ribbon, panels, 2D/3D viewports, command line).</summary>
public partial class MainWindow : Window
{
    private readonly CommandEngine _engine;
    private readonly CadScene _scene;
    private readonly OrbitController _orbit;
    private bool _gpuFailed, _refreshQueued, _id;
    private string _view = "2d";
    private int _tab;
    private string? _filePath, _category;
    private StudioDocument? _hooked, _shown3DDoc;
    private int _shown3DKey = -1, _shownHistory = -1;
    private CadModel? _pendingModel;
    private bool _pendingRefit;
    private readonly object _sceneLock = new();
    private string _panelKey = "";
    private DispatcherTimer? _toastTimer;
    private List<CommandInfo> _suggestions = [];
    private double _cardW = 252, _tplW = 196;

    private StudioDocument Doc => _engine.Document;
    private string T(string en, string id) => _id ? id : en;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new CommandEngine(StudioCatalog.Blank());
        _engine.StateChanged += QueueRefresh;
        _engine.ViewRequest += v =>
        {
            if (v == "zoom-extents") Canvas.ZoomExtents();
            if (v == "view-3d" && _view == "2d") SetView("split");
            if (v == "view-2d") SetView("2d");
        };
        Hook(Doc);

        Canvas.Engine = _engine;
        Canvas.CursorMoved += s =>
        {
            Coords.Text = string.Create(CultureInfo.InvariantCulture, $"{s.Point.X:0.###}, {s.Point.Y:0.###}");
            SnapKindText.Text = s.Kind is SnapKind.None or SnapKind.Grid ? "" : s.Kind.ToString().ToUpperInvariant();
        };
        Canvas.ChangedByUser += QueueRefresh;

        _scene = new CadScene(light: false);
        View3D.Scene = _scene.Scene;
        View3D.Camera = _scene.Camera;
        _orbit = new OrbitController(View3D, _scene.Camera);
        // scene edits are applied between frames, on the renderer's own callback (rebuilding mid-frame stalls ThreeNet)
        View3D.Frame += (_, _) =>
        {
            CadModel? m; bool refit;
            lock (_sceneLock) { m = _pendingModel; refit = _pendingRefit; _pendingModel = null; _pendingRefit = false; }
            if (m != null) { _scene.SetModel(m); if (refit) SetCamera("iso"); }
            _orbit.Apply();
        };
        View3D.RenderFailed += (_, _) => Dispatcher.UIThread.Post(() => { _gpuFailed = true; Update3D(force: true); });
        if (Program.Software3D) { _gpuFailed = true; Pane3D.Children.Remove(View3D); }

        CmdBox.KeyDown += OnCmdKeyDown;
        CmdBox.TextChanged += (_, _) => UpdateSuggestions();
        GridSizeBox.LostFocus += (_, _) => ApplyGridSize();
        GridSizeBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyGridSize(); Canvas.Focus(); e.Handled = true; } };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space) Canvas.SetSpace(false); }, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnWindowTextInput, RoutingStrategies.Tunnel);

        Hero.Background = BlueprintBrush();
        // keep the start screen exactly as wide as its viewport so the card shelves wrap
        StartScreen.SizeChanged += (_, e) =>
        {
            StartContent.Width = e.NewSize.Width;
            double avail = e.NewSize.Width - 48 - 34 - 14;   // shelf margins + scrollbar
            int cols = Math.Max(1, (int)(avail / 250)), tcols = Math.Max(1, (int)(avail / 200));
            double w = Math.Floor(avail / cols) - 14, tw = Math.Floor(avail / tcols) - 14;
            if (Math.Abs(w - _cardW) > 1 || Math.Abs(tw - _tplW) > 1) { _cardW = w; _tplW = tw; BuildCards(); }
        };
        StartVersion.Text = $"v{typeof(GravicodeCad).Assembly.GetName().Version?.ToString(3)} · desktop";
        HeroImage.Source = Thumb("gearbox");
        BuildStart();
        ApplyLanguage();
        SetView("2d");

        Opened += async (_, _) =>
        {
            if (Program.StartupFile is { } f) await OpenPathAsync(f);
            if (Environment.GetEnvironmentVariable("GCAD_STUDIO_DEMO") is { Length: > 0 } demo)
                try { await RunDemoAsync(demo); } catch (Exception ex) { ShowToast("demo: " + ex.Message); }
        };
        Closing += OnClosing;
    }

    // ================================================================== document & refresh

    private void Hook(StudioDocument doc)
    {
        if (_hooked != null) _hooked.Changed -= QueueRefresh;
        _hooked = doc;
        doc.Changed += QueueRefresh;
    }

    private void Load(StudioDocument doc, string? path = null)
    {
        _engine.SetDocument(doc);
        Hook(doc);
        _filePath = path;
        _engine.Print($"Opened \"{doc.Name}\" — {doc.Entities.Count} objects, {doc.Solids.Count} solids. Type HELP for commands.");
        StartScreen.IsVisible = false;
        SetView(doc.Solids.Count > 0 ? "split" : "2d");
        _shown3DDoc = null;
        Dispatcher.UIThread.Post(() => { Canvas.ZoomExtents(); Canvas.Refresh(true); }, DispatcherPriority.Background);
        RefreshNow();
        CmdBox.Focus();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(RefreshNow, DispatcherPriority.Render);
    }

    private void RefreshNow()
    {
        _refreshQueued = false;
        Canvas.Refresh();
        Title = $"{Doc.Name}{(Doc.IsDirty ? " •" : "")} — Gravicode CAD Studio";
        DocName.Text = Doc.Name + (Doc.IsDirty ? " •" : "");
        DocCat.Text = (Doc.Category ?? "Drawing").ToUpperInvariant();
        UpdateRibbonState();
        UpdateCommandLine();
        UpdateStatus();
        string key = $"{Doc.GetHashCode()}|{Doc.Version}|{string.Join(",", _engine.Selection)}|{_id}|{Doc.CurrentLayer}";
        if (key != _panelKey) { _panelKey = key; BuildLeftPanel(); BuildRightPanel(); }
        Update3D();
    }

    private void SetView(string view)
    {
        _view = view;
        // The GPU view must never be collapsed to zero size or hidden (ThreeNet stalls the compositor), so the
        // panes keep their columns and the active one is stretched over the other instead.
        Grid.SetColumn(Pane2D, 0); Grid.SetColumnSpan(Pane2D, view == "2d" ? 3 : 1);
        Grid.SetColumn(Pane3D, view == "3d" ? 0 : 2); Grid.SetColumnSpan(Pane3D, view == "3d" ? 3 : 1);
        Pane2D.ZIndex = view == "2d" ? 2 : 1;
        Pane3D.ZIndex = view == "3d" ? 2 : 1;
        Pane2D.IsVisible = view != "3d";
        Splitter.IsVisible = view == "split";
        Update3D();
        UpdateRibbonState();
    }

    private void Update3D(bool force = false)
    {
        Tag3D.Text = $"3D · {Doc.Solids.Count} {T("SOLIDS", "SOLID")}";
        bool empty = Doc.Solids.Count == 0;
        Empty3D.IsVisible = empty;
        Fallback3DCover.IsVisible = !empty && _gpuFailed;
        int key = SolidsKey();
        if (empty || (!force && _shown3DDoc == Doc && _shown3DKey == key)) return;
        bool refit = _shown3DDoc != Doc;
        _shown3DDoc = Doc; _shown3DKey = key;
        var model = Doc.ToCadModel(includeDrawing: false);
        if (_gpuFailed)
        {
            var img = SoftwareRenderer.Render(model, new Rendering.RenderOptions { Width = 1200, Height = 900 });
            using var ms = new MemoryStream(img.EncodePng());
            Fallback3D.Source = new Bitmap(ms);
            return;
        }
        lock (_sceneLock) { _pendingModel = model; _pendingRefit |= refit; }
    }

    /// <summary>Changes whenever a visible solid's mesh, colour or material changes (drawing edits don't rebuild 3D).</summary>
    private int SolidsKey()
    {
        var h = new HashCode();
        foreach (var s in Doc.VisibleSolids) { h.Add(s.Id); h.Add(s.Mesh); h.Add(s.Color); h.Add(s.Metallic); h.Add(s.Roughness); }
        return h.ToHashCode();
    }

    private void SetCamera(string view)
    {
        var (center, radius) = _scene.Frame();
        _orbit.Target = center;
        _orbit.MinDistance = radius * 0.05f;      // models are in mm: the 0.5 – 500 defaults suit metre scenes
        _orbit.MaxDistance = radius * 30f;
        _orbit.Distance = radius / MathF.Sin(17.5f * MathF.PI / 180f) * 1.08f;
        (_orbit.Yaw, _orbit.Pitch) = view switch
        {
            "top" => (0f, 1.553f),
            "front" => (0f, 0f),
            "right" => (MathF.PI / 2, 0f),
            _ => (-0.6f, 0.45f),
        };
    }

    private void OnView3D(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string v }) SetCamera(v); }
    private void OnBox(object? sender, RoutedEventArgs e) => Run("BOX");

    // ================================================================== ribbon

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        for (int i = 0; i < StudioRibbon.Tabs.Count; i++)
        {
            var tab = StudioRibbon.Tabs[i]; int idx = i;
            var b = new Button { Content = T(tab.Title, tab.TitleId).ToUpperInvariant(), Classes = { "tab" } };
            if (i == _tab) b.Classes.Add("on");
            b.Click += (_, _) => { _tab = idx; BuildTabs(); };
            Tabs.Children.Add(b);
        }
        BuildRibbonPanel();
    }

    private readonly List<(Button Button, RibbonButton Def, Path Icon)> _ribbonButtons = [];

    private void BuildRibbonPanel()
    {
        RibbonPanel.Children.Clear();
        _ribbonButtons.Clear();
        var groups = StudioRibbon.Tabs[_tab].Groups;
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var group = groups[gi];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var b in group.Buttons.Where(b => b.Large)) row.Children.Add(RibbonButtonFor(b));
            var small = group.Buttons.Where(b => !b.Large).ToList();
            for (int i = 0; i < small.Count; i += 3)
            {
                var col = new StackPanel { Spacing = 1, MinWidth = 86 };
                foreach (var b in small.Skip(i).Take(3)) col.Children.Add(RibbonButtonFor(b));
                row.Children.Add(col);
            }
            var title = new TextBlock
            {
                Text = group.Title.ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 2, 0, 0),
                FontFamily = (FontFamily)this.FindResource("LabelFont")!, FontWeight = FontWeight.Medium, FontSize = 11.5, LetterSpacing = 2,
                Foreground = (IBrush)this.FindResource("MutedBrush")!
            };
            var stack = new DockPanel { Margin = new(8, 0) };
            DockPanel.SetDock(title, Dock.Bottom);
            stack.Children.Add(title);
            stack.Children.Add(row);
            RibbonPanel.Children.Add(new Border
            {
                Child = stack,
                BorderBrush = (IBrush)this.FindResource("G700")!, BorderThickness = new(0, 0, gi < groups.Count - 1 ? 1 : 0, 0)
            });
        }
        UpdateRibbonState();
    }

    private Button RibbonButtonFor(RibbonButton def)
    {
        var path = new Path
        {
            Data = StreamGeometry.Parse(StudioRibbon.Icons.GetValueOrDefault(def.Icon, "M4 4h16v16H4z")),
            Stroke = (IBrush)this.FindResource("TextBrush")!, StrokeThickness = def.Large ? 1.35 : 1.6,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        var icon = new Viewbox { Width = def.Large ? 28 : 16, Height = def.Large ? 28 : 16, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
        Control content = def.Large
            ? new StackPanel
            {
                Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    icon,
                    new TextBlock { Text = def.Label.ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center, FontFamily = (FontFamily)this.FindResource("LabelFont")!, FontWeight = FontWeight.SemiBold, FontSize = 13, LetterSpacing = 0.5 }
                }
            }
            : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, new TextBlock { Text = def.Label, VerticalAlignment = VerticalAlignment.Center } } };
        var btn = new Button { Content = content, Classes = { "rbtn" } };
        if (def.Large) btn.Classes.Add("lg");
        ToolTip.SetTip(btn, def.Tooltip);
        btn.Click += (_, _) => Run(def.Command);
        _ribbonButtons.Add((btn, def, path));
        return btn;
    }

    private void UpdateRibbonState()
    {
        foreach (var (btn, def, icon) in _ribbonButtons)
        {
            bool on = def.Command switch
            {
                "ORTHO" => _engine.Settings.Ortho,
                "GRID" => _engine.Settings.GridSnap,
                "OSNAP" => _engine.Settings.ObjectSnap,
                "app:view-2d" => _view == "2d",
                "app:view-split" => _view == "split",
                "app:view-3d" => _view == "3d",
                _ => _engine.ActiveCommand?.Name == def.Command
            };
            btn.Classes.Set("on", on);
            icon.Stroke = (IBrush)this.FindResource(on ? "SelectBrush" : "TextBrush")!;
        }
        TgSnap.Classes.Set("on", _engine.Settings.GridSnap);
        TgOrtho.Classes.Set("on", _engine.Settings.Ortho);
        TgOsnap.Classes.Set("on", _engine.Settings.ObjectSnap);
    }

    // ================================================================== commands & app actions

    private async void Run(string command)
    {
        if (!command.StartsWith("app:")) { _engine.Submit(command); CmdBox.Focus(); return; }
        var action = command[4..];
        switch (action)
        {
            case "view-2d": SetView("2d"); break;
            case "view-3d": SetView("3d"); break;
            case "view-split": SetView("split"); break;
            case "new": ShowStart(); break;
            case "open": await OpenDialogAsync(); break;
            case "save": await SaveAsync(); break;
            default:
                if (action.StartsWith("export:")) await ExportAsync(action[7..]);
                break;
        }
    }

    private void OnCmdKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                {
                    var text = CmdBox.Text ?? "";
                    CmdBox.Text = "";
                    _engine.Submit(text);
                    e.Handled = true;
                    break;
                }
            case Key.Tab when _suggestions.Count > 0:
                CmdBox.Text = _suggestions[0].Name; CmdBox.CaretIndex = CmdBox.Text.Length; e.Handled = true; break;
            case Key.Up when string.IsNullOrEmpty(CmdBox.Text):
                {
                    var last = _engine.History.LastOrDefault(h => h.StartsWith("Command: ") && h.Length > 9);
                    if (last != null) { CmdBox.Text = last[9..]; CmdBox.CaretIndex = CmdBox.Text.Length; }
                    e.Handled = true; break;
                }
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (StartScreen.IsVisible) return;
        var focused = FocusManager?.GetFocusedElement();
        bool inCmd = focused == CmdBox;
        bool typing = focused is TextBox && !inCmd;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.Escape) { CmdBox.Text = ""; _engine.Escape(); e.Handled = true; return; }
        if (typing) return;
        switch (e.Key)
        {
            case Key.F3: _engine.Settings.ObjectSnap = !_engine.Settings.ObjectSnap; _engine.Print($"<Osnap {(_engine.Settings.ObjectSnap ? "on" : "off")}>"); e.Handled = true; break;
            case Key.F8: _engine.Settings.Ortho = !_engine.Settings.Ortho; _engine.Print($"<Ortho {(_engine.Settings.Ortho ? "on" : "off")}>"); e.Handled = true; break;
            case Key.F9: _engine.Settings.GridSnap = !_engine.Settings.GridSnap; _engine.Print($"<Snap {(_engine.Settings.GridSnap ? "on" : "off")}>"); e.Handled = true; break;
            case Key.Z when ctrl: Doc.Undo(); e.Handled = true; break;
            case Key.Y when ctrl: Doc.Redo(); e.Handled = true; break;
            case Key.S when ctrl: Run("app:save"); e.Handled = true; break;
            case Key.O when ctrl: Run("app:open"); e.Handled = true; break;
            case Key.N when ctrl: Run("app:new"); e.Handled = true; break;
            case Key.A when ctrl && !inCmd: _engine.SelectAll(); e.Handled = true; break;
            case Key.Delete when !inCmd || string.IsNullOrEmpty(CmdBox.Text):
                if (!_engine.IsBusy) { _engine.DeleteSelection(); e.Handled = true; }
                break;
            case Key.Space when !inCmd:
                Canvas.SetSpace(true); break;
            case Key.Enter when !inCmd:
                _engine.Enter(); e.Handled = true; break;
        }
        QueueRefresh();
    }

    /// <summary>AutoCAD habit: start typing anywhere and the keys land on the command line.</summary>
    private void OnWindowTextInput(object? sender, TextInputEventArgs e)
    {
        if (StartScreen.IsVisible || FocusManager?.GetFocusedElement() is TextBox || string.IsNullOrEmpty(e.Text) || e.Text == " ") return;
        CmdBox.Focus();
        CmdBox.Text = (CmdBox.Text ?? "") + e.Text;
        CmdBox.CaretIndex = CmdBox.Text.Length;
        e.Handled = true;
    }

    private void UpdateSuggestions()
    {
        var text = CmdBox.Text ?? "";
        _suggestions = _engine.IsBusy || text.Length == 0 || text.Contains(' ') ? [] :
            _engine.Commands.Where(c => c.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) || c.Aliases.Any(a => a.Equals(text, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.Name.Length).Take(6).ToList();
        Suggestions.Children.Clear();
        for (int i = 0; i < _suggestions.Count; i++)
        {
            var c = _suggestions[i];
            var grid = new Grid { ColumnDefinitions = new("110,80,*") };
            grid.Children.Add(new TextBlock { Text = c.Name, Classes = { "mono" }, FontWeight = FontWeight.SemiBold, Foreground = (IBrush)this.FindResource("SelectBrush")! });
            var al = new TextBlock { Text = string.Join(", ", c.Aliases), Classes = { "mono", "muted" } }; Grid.SetColumn(al, 1); grid.Children.Add(al);
            var de = new TextBlock { Text = c.Description, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(de, 2); grid.Children.Add(de);
            var b = new Button { Content = grid, Background = i == 0 ? (IBrush)this.FindResource("G700")! : Brushes.Transparent, BorderThickness = new(0), Padding = new(8, 4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new(3) };
            var name = c.Name;
            b.Click += (_, _) => { CmdBox.Text = ""; _engine.Submit(name); CmdBox.Focus(); };
            Suggestions.Children.Add(b);
        }
        SuggestBox.IsVisible = _suggestions.Count > 0;
    }

    private void UpdateCommandLine()
    {
        PromptLabel.Text = "▸ " + _engine.PromptText;
        Keywords.Children.Clear();
        if (_engine.CurrentPrompt is { Keywords.Count: > 0 } p)
            foreach (var k in p.Keywords)
            {
                var b = new Button { Content = k, Classes = { "chip" } };
                b.Click += (_, _) => { _engine.Submit(k); CmdBox.Focus(); };
                Keywords.Children.Add(b);
            }
        CmdBox.PlaceholderText = _engine.IsBusy ? "" : T("Type a command — LINE, CIRCLE, EXTRUDE, BOX, HELP…", "Ketik perintah — LINE, CIRCLE, EXTRUDE, BOX, HELP…");

        int count = _engine.History.Count;
        string last = count > 0 ? _engine.History[^1] : "";
        int hash = HashCode.Combine(count, last);
        if (hash == _shownHistory) return;
        _shownHistory = hash;
        History.Children.Clear();
        foreach (var line in _engine.History.TakeLast(80))
            History.Children.Add(new TextBlock
            {
                Text = line, Classes = { "mono" }, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = line.StartsWith("Command:") ? (IBrush)this.FindResource("TextBrush")! : line.StartsWith("  ") ? (IBrush)this.FindResource("OutBrush")! : (IBrush)this.FindResource("MutedBrush")!
            });
        Dispatcher.UIThread.Post(() => HistoryScroll.ScrollToEnd(), DispatcherPriority.Background);
        UpdateSuggestions();
    }

    private void UpdateStatus()
    {
        StatusMid.Text = $"{T("Layer", "Layer")}: {Doc.CurrentLayer} · {T("Colour", "Warna")}: {_engine.Settings.CurrentColor} · {Doc.Units.Symbol()}";
        StatusRight.Text = $"{Doc.Entities.Count} {T("objects", "objek")} · {Doc.Solids.Count} {T("solids", "solid")} · {MeshKernel.Current.Name.Split('(')[0].Trim()} · {GravicodeCad.CreditsId}";
        if (FocusManager?.GetFocusedElement() != GridSizeBox) GridSizeBox.Text = _engine.Settings.GridSize.ToString(CultureInfo.InvariantCulture);
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        var s = _engine.Settings;
        switch ((sender as Button)?.Tag as string)
        {
            case "snap": s.GridSnap = !s.GridSnap; break;
            case "ortho": s.Ortho = !s.Ortho; break;
            case "osnap": s.ObjectSnap = !s.ObjectSnap; break;
        }
        QueueRefresh();
    }

    private void ApplyGridSize()
    {
        if (double.TryParse(GridSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g > 0) _engine.Settings.GridSize = g;
        Canvas.Refresh(true);
        QueueRefresh();
    }

    // ================================================================== side panels

    private TextBlock Heading(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "h" }, VerticalAlignment = VerticalAlignment.Center };

    private Control PanelHead(string title, Control? right = null)
    {
        var d = new DockPanel { Margin = new(12, 12, 12, 6) };
        if (right != null) { DockPanel.SetDock(right, Dock.Right); d.Children.Add(right); }
        d.Children.Add(Heading(title));
        return d;
    }

    private TextBlock Count(int n) => new() { Text = n.ToString(), Classes = { "mono", "muted" }, VerticalAlignment = VerticalAlignment.Center };

    private void BuildLeftPanel()
    {
        LeftPanel.Children.Clear();
        var add = new Button { Content = "+", Classes = { "flat" }, Padding = new(7, 0), Height = 22 };
        ToolTip.SetTip(add, T("New layer", "Layer baru"));
        add.Click += (_, _) =>
        {
            int n = Doc.Layers.Count;
            string[] palette = ["#F6AD55", "#68D391", "#63B3ED", "#F687B3", "#B794F4", "#FC8181", "#4FD1C5"];
            Doc.Edit("New layer", d => { var name = $"Layer{n}"; d.Layers.Add(new Layer(name, palette[n % palette.Length])); d.CurrentLayer = name; });
        };
        LeftPanel.Children.Add(PanelHead(T("Layers", "Layer"), add));
        foreach (var l in Doc.Layers)
        {
            var layer = l;
            bool current = l.Name == Doc.CurrentLayer;
            var row = new Grid { ColumnDefinitions = new("22,22,20,*,Auto"), Margin = new(6, 0), Height = 24, Background = current ? (IBrush)this.FindResource("G800")! : Brushes.Transparent };
            var eye = Tiny(l.Visible ? "◉" : "○", l.Visible ? "TextBrush" : "G600", T(l.Visible ? "Hide" : "Show", l.Visible ? "Sembunyikan" : "Tampilkan"));
            eye.Click += (_, _) => UpdateLayer(layer, layer with { Visible = !layer.Visible });
            var lk = Tiny(l.Locked ? "■" : "□", l.Locked ? "SelectBrush" : "G600", T(l.Locked ? "Unlock" : "Lock", l.Locked ? "Buka kunci" : "Kunci"));
            lk.Click += (_, _) => UpdateLayer(layer, layer with { Locked = !layer.Locked });
            var swatch = new Button { Width = 14, Height = 14, MinHeight = 0, Padding = new(0), CornerRadius = new(2), BorderThickness = new(1), BorderBrush = (IBrush)this.FindResource("G600")!, Background = Brush(l.Color), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(swatch, T("Cycle layer colour", "Ganti warna layer"));
            swatch.Click += (_, _) => UpdateLayer(layer, layer with { Color = NextColor(layer.Color) });
            var name = new Button { Content = l.Name, Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(4, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 13 };
            ToolTip.SetTip(name, T("Make current", "Jadikan aktif"));
            name.Click += (_, _) => { Doc.CurrentLayer = layer.Name; QueueRefresh(); _panelKey = ""; };
            var cnt = Count(Doc.Entities.Count(e => e.Layer == layer.Name) + Doc.Solids.Count(s => s.Layer == layer.Name));
            cnt.FontSize = 11;
            Grid.SetColumn(lk, 1); Grid.SetColumn(swatch, 2); Grid.SetColumn(name, 3); Grid.SetColumn(cnt, 4);
            row.Children.AddRange([eye, lk, swatch, name, cnt]);
            LeftPanel.Children.Add(current ? new Border { BorderBrush = (IBrush)this.FindResource("SelectBrush")!, BorderThickness = new(2, 0, 0, 0), Child = row } : row);
        }

        LeftPanel.Children.Add(PanelHead(T("Solids", "Solid"), Count(Doc.Solids.Count)));
        foreach (var s in Doc.Solids.Take(200))
        {
            var id = s.Id;
            bool sel = _engine.Selection.Contains(id);
            var b = new Button
            {
                Background = sel ? (IBrush)this.FindResource("SelectDim")! : Brushes.Transparent, BorderThickness = new(0), Padding = new(8, 3), Margin = new(6, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 10, Height = 10, CornerRadius = new(2), Background = Brush(s.Color), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = s.Name, FontSize = 13, Foreground = sel ? Brush("#FFD2B3") : (IBrush)this.FindResource("TextBrush")! } } }
            };
            b.Click += (_, _) =>
            {
                if (_engine.IsBusy && _engine.CurrentPrompt?.Kind != PromptKind.Select) return;
                if (!_engine.Selection.Remove(id)) _engine.Selection.Add(id);
                QueueRefresh();
            };
            LeftPanel.Children.Add(b);
        }
        if (Doc.Solids.Count == 0)
            LeftPanel.Children.Add(new TextBlock { Text = T("No solids yet — try BOX, EXTRUDE or the Solid tab.", "Belum ada solid — coba BOX, EXTRUDE atau tab Solid."), Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new(14, 2, 12, 0) });

        LeftPanel.Children.Add(PanelHead(T("Drawing", "Gambar"), Count(Doc.Entities.Count)));
        foreach (var g in Doc.Entities.GroupBy(e => e.Geometry.GetType().Name.Replace("Entity", "")).OrderByDescending(g => g.Count()))
        {
            var d = new DockPanel { Margin = new(14, 1, 12, 1) };
            var c = Count(g.Count()); DockPanel.SetDock(c, Dock.Right); d.Children.Add(c);
            d.Children.Add(new TextBlock { Text = g.Key, FontSize = 13 });
            LeftPanel.Children.Add(d);
        }
    }

    private Button Tiny(string glyph, string brushKey, string tip)
    {
        var b = new Button { Content = glyph, Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0), Width = 20, FontSize = 13, Foreground = (IBrush)this.FindResource(brushKey)!, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(b, tip);
        return b;
    }

    private static readonly string[] Palette = ["#E6EDF3", "#F6AD55", "#68D391", "#63B3ED", "#F687B3", "#B794F4", "#FC8181", "#4FD1C5", "#F5D76E", "#9AA5B1"];
    private static string NextColor(string c) { int i = Array.FindIndex(Palette, p => p.Equals(c, StringComparison.OrdinalIgnoreCase)); return Palette[(i + 1) % Palette.Length]; }

    private void UpdateLayer(Layer old, Layer updated) =>
        Doc.Edit("Layer", d => { int i = d.Layers.FindIndex(x => x.Name == old.Name); if (i >= 0) d.Layers[i] = updated; });

    private void BuildRightPanel()
    {
        RightPanel.Children.Clear();
        var ents = Doc.Entities.Where(e => _engine.Selection.Contains(e.Id)).ToList();
        var sols = Doc.Solids.Where(s => _engine.Selection.Contains(s.Id)).ToList();
        int n = ents.Count + sols.Count;
        RightPanel.Children.Add(PanelHead(T("Properties", "Properti"), new TextBlock { Text = n == 0 ? T("No selection", "Tidak ada seleksi") : $"{n} {T("selected", "dipilih")}", Classes = { "mono", "muted" } }));
        string unit = Doc.Units.Symbol();

        if (sols.Count == 1 && ents.Count == 0)
        {
            var s = sols[0];
            var p = s.Mesh.Properties; var bb = s.Mesh.Bounds;
            var props = Props();
            Prop(props, T("Type", "Tipe"), new TextBlock { Text = "Solid" });
            Prop(props, T("Name", "Nama"), Field(s.Name, v => ReplaceSolid(s, new StudioSolid(s.Id, v, s.Layer, s.Mesh, s.Color, s.Metallic, s.Roughness))));
            Prop(props, "Layer", LayerCombo(s.Layer, v => ReplaceSolid(s, new StudioSolid(s.Id, s.Name, v, s.Mesh, s.Color, s.Metallic, s.Roughness))));
            Prop(props, T("Colour", "Warna"), ColorRow(s.Color, v => ReplaceSolid(s, new StudioSolid(s.Id, s.Name, s.Layer, s.Mesh, v, s.Metallic, s.Roughness))));
            Prop(props, T("Metallic", "Metalik"), SliderFor(s.Metallic, 0, 1, v => ReplaceSolid(s, new StudioSolid(s.Id, s.Name, s.Layer, s.Mesh, s.Color, v, s.Roughness))));
            Prop(props, T("Roughness", "Kekasaran"), SliderFor(s.Roughness, 0.05, 1, v => ReplaceSolid(s, new StudioSolid(s.Id, s.Name, s.Layer, s.Mesh, s.Color, s.Metallic, v))));
            RightPanel.Children.Add(props);
            RightPanel.Children.Add(SubHead(T("Mass properties", "Properti massa")));
            var num = Props();
            Prop(num, "Volume", Mono($"{p.Volume:N1} {unit}³"));
            Prop(num, T("Surface", "Luas"), Mono($"{p.Area:N1} {unit}²"));
            Prop(num, T("Size", "Ukuran"), Mono($"{bb.Size.X:0.##} × {bb.Size.Y:0.##} × {bb.Size.Z:0.##}"));
            Prop(num, "Centroid", Mono($"{p.Centroid.X:0.##}, {p.Centroid.Y:0.##}, {p.Centroid.Z:0.##}"));
            Prop(num, T("Triangles", "Segitiga"), Mono(s.Mesh.TriangleCount.ToString("N0")));
            Prop(num, T("Steel", "Baja"), Mono($"{p.Volume * 7.85e-3:N0} g"));
            RightPanel.Children.Add(num);
        }
        else if (ents.Count == 1 && sols.Count == 0)
        {
            var e = ents[0];
            var props = Props();
            Prop(props, T("Type", "Tipe"), new TextBlock { Text = e.Geometry.GetType().Name.Replace("Entity", "") });
            Prop(props, "Layer", LayerCombo(e.Layer, v => ReplaceEntity(e, e with { Layer = v })));
            var colorRow = ColorRow(e.Color ?? Doc.LayerOf(e.Layer).Color, v => ReplaceEntity(e, e with { Color = v }));
            var byLayer = new Button { Content = "ByLayer", Classes = { "chip" } };
            byLayer.Click += (_, _) => ReplaceEntity(e, e with { Color = null });
            ((StackPanel)colorRow).Children.Add(byLayer);
            Prop(props, T("Colour", "Warna"), colorRow);
            if (e.Geometry is TextEntity t)
                Prop(props, T("Text", "Teks"), Field(t.Text, v => ReplaceEntity(e, e with { Geometry = new TextEntity(t.Position, v, t.Height) })));
            RightPanel.Children.Add(props);
            RightPanel.Children.Add(new TextBlock { Text = StudioCommands.Describe(e.Geometry), Classes = { "mono" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new(12, 4, 12, 10) });
            if (Geometry2D.AsLoop(e.Geometry, 0.01) is { } loop)
            {
                var num = Props();
                Prop(num, T("Area", "Luas"), Mono($"{loop.Area:N2} {unit}²"));
                Prop(num, T("Perimeter", "Keliling"), Mono($"{loop.Perimeter:N2} {unit}"));
                RightPanel.Children.Add(num);
            }
        }
        else if (n > 1)
        {
            RightPanel.Children.Add(new TextBlock { Text = $"{ents.Count} {T("drawing objects", "objek gambar")}, {sols.Count} {T("solids", "solid")}", Classes = { "mono" }, Margin = new(12, 4, 12, 10) });
            var props = Props();
            Prop(props, T("Move to layer", "Pindah layer"), LayerCombo(null, MoveAll));
            RightPanel.Children.Add(props);
            var quick = new WrapPanel { Margin = new(12, 0) };
            foreach (var (label, cmd, enabled) in new[] { ("UNION", "UNION", sols.Count > 1), ("INTERSECT", "INTERSECT", sols.Count > 1), ("EXTRUDE", "EXTRUDE", ents.Count > 0), (T("ERASE", "HAPUS"), "ERASE", true) })
            {
                var b = new Button { Content = label, Classes = { "flat" }, Margin = new(0, 0, 6, 6), IsEnabled = enabled };
                b.Click += (_, _) => Run(cmd);
                quick.Children.Add(b);
            }
            RightPanel.Children.Add(quick);
        }
        else
        {
            var props = Props();
            Prop(props, T("Drawing", "Gambar"), Field(Doc.Name, v => { Doc.Name = v; Doc.Touch(); }));
            var units = new ComboBox { Classes = { "field" }, ItemsSource = Enum.GetValues<LengthUnit>(), SelectedItem = Doc.Units, HorizontalAlignment = HorizontalAlignment.Stretch };
            units.SelectionChanged += (_, _) => { if (units.SelectedItem is LengthUnit u && u != Doc.Units) { Doc.Units = u; Doc.Touch(); } };
            Prop(props, T("Units", "Satuan"), units);
            Prop(props, T("Category", "Kategori"), new TextBlock { Text = Doc.Category ?? "—" });
            RightPanel.Children.Add(props);
            if (Doc.Description is { } desc) RightPanel.Children.Add(new TextBlock { Text = desc, Classes = { "mono" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new(12, 4, 12, 10) });
            RightPanel.Children.Add(SubHead(T("Getting around", "Cara pakai")));
            foreach (var tip in new[]
            {
                T("Type a command anywhere (L, C, REC, EXT, BOX…) and press Enter.", "Ketik perintah di mana saja (L, C, REC, EXT, BOX…) lalu Enter."),
                T("Coordinates: 10,20 · relative @50,0 · polar @100<45 · or a distance.", "Koordinat: 10,20 · relatif @50,0 · polar @100<45 · atau jarak."),
                T("Wheel zooms, middle-drag or Space+drag pans, right-click = Enter.", "Roda untuk zoom, seret tengah atau Spasi+seret untuk geser, klik kanan = Enter."),
                T("Drag left→right for window, right→left for crossing selection.", "Seret kiri→kanan untuk window, kanan→kiri untuk seleksi crossing."),
            })
                RightPanel.Children.Add(new DockPanel
                {
                    Margin = new(12, 0, 12, 6),
                    Children = { new TextBlock { Text = "▪", Foreground = (IBrush)this.FindResource("SelectBrush")!, Margin = new(0, 0, 8, 0), [DockPanel.DockProperty] = Dock.Left }, new TextBlock { Text = tip, Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap } }
                });
        }
    }

    private static Grid Props() => new() { ColumnDefinitions = new("92,*"), Margin = new(12, 4, 12, 10) };

    private void Prop(Grid grid, string label, Control value)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var l = new TextBlock { Text = label.ToUpperInvariant(), FontFamily = (FontFamily)this.FindResource("LabelFont")!, FontWeight = FontWeight.Medium, FontSize = 12, LetterSpacing = 1.2, Foreground = (IBrush)this.FindResource("MutedBrush")!, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 3) };
        Grid.SetRow(l, row); Grid.SetRow(value, row); Grid.SetColumn(value, 1);
        value.Margin = new(0, 3);
        value.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(l); grid.Children.Add(value);
    }

    private Control SubHead(string text) => new Border
    {
        BorderBrush = (IBrush)this.FindResource("G700")!, BorderThickness = new(0, 1, 0, 0), Margin = new(12, 10, 12, 4), Padding = new(0, 12, 0, 0),
        Child = Heading(text)
    };

    private static TextBlock Mono(string text) => new() { Text = text, Classes = { "mono" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };

    private static TextBox Field(string value, Action<string> commit)
    {
        var tb = new TextBox { Text = value, Classes = { "field" } };
        void Commit() { if (tb.Text is { Length: > 0 } t && t != value) commit(t); }
        tb.LostFocus += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return tb;
    }

    private ComboBox LayerCombo(string? current, Action<string> commit)
    {
        var names = Doc.Layers.Select(l => l.Name).ToList();
        var cb = new ComboBox { Classes = { "field" }, ItemsSource = names, SelectedItem = current, HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "—" };
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is string s && s != current) commit(s); };
        return cb;
    }

    private Control ColorRow(string color, Action<string> commit)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var sw = new Button { Width = 18, Height = 18, MinHeight = 0, Padding = new(0), CornerRadius = new(2), BorderThickness = new(1), BorderBrush = (IBrush)this.FindResource("G600")!, Background = Brush(color) };
        ToolTip.SetTip(sw, T("Next colour", "Warna berikutnya"));
        sw.Click += (_, _) => commit(NextColor(color));
        var hex = new TextBox { Text = color, Classes = { "field" }, Width = 92, FontFamily = (FontFamily)this.FindResource("MonoFont")!, FontSize = 12 };
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter && Color.TryParse(hex.Text, out Color _)) { commit(hex.Text!); e.Handled = true; } };
        panel.Children.Add(sw); panel.Children.Add(hex);
        return panel;
    }

    private static Slider SliderFor(double value, double min, double max, Action<double> commit)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = 0.05, LargeChange = 0.1, MinHeight = 0 };
        s.PointerCaptureLost += (_, _) => { if (Math.Abs(s.Value - value) > 1e-6) commit(Math.Round(s.Value, 2)); };
        s.KeyUp += (_, _) => { if (Math.Abs(s.Value - value) > 1e-6) commit(Math.Round(s.Value, 2)); };
        return s;
    }

    private void ReplaceSolid(StudioSolid old, StudioSolid updated) =>
        Doc.Edit("Properties", d => { int i = d.Solids.FindIndex(x => x.Id == old.Id); if (i >= 0) d.Solids[i] = updated; });

    private void ReplaceEntity(StudioEntity old, StudioEntity updated) =>
        Doc.Edit("Properties", d => { int i = d.Entities.FindIndex(x => x.Id == old.Id); if (i >= 0) d.Entities[i] = updated; });

    private void MoveAll(string layer)
    {
        var ids = _engine.Selection.ToHashSet();
        Doc.Edit("Change layer", d =>
        {
            for (int i = 0; i < d.Entities.Count; i++) if (ids.Contains(d.Entities[i].Id)) d.Entities[i] = d.Entities[i] with { Layer = layer };
            for (int i = 0; i < d.Solids.Count; i++) if (ids.Contains(d.Solids[i].Id)) { var s = d.Solids[i]; d.Solids[i] = new StudioSolid(s.Id, s.Name, layer, s.Mesh, s.Color, s.Metallic, s.Roughness); }
        });
    }

    private static readonly Dictionary<string, IBrush> Brushes2 = [];
    private static IBrush Brush(string hex)
    {
        if (Brushes2.TryGetValue(hex, out var b)) return b;
        return Brushes2[hex] = new SolidColorBrush(Color.TryParse(hex, out var c) ? c : Colors.White);
    }

    // ================================================================== start screen

    private void ShowStart()
    {
        BtnBack.IsVisible = Doc.Entities.Count + Doc.Solids.Count > 0;
        ApplyLanguage();
        StartScreen.IsVisible = true;
    }

    private void OnBack(object? sender, RoutedEventArgs e) { StartScreen.IsVisible = false; CmdBox.Focus(); }
    private void OnBrand(object? sender, RoutedEventArgs e) => ShowStart();
    private void OnNewBlank(object? sender, RoutedEventArgs e) => Load(StudioCatalog.Blank());
    private async void OnOpen(object? sender, RoutedEventArgs e) => await OpenDialogAsync();

    private void OnLang(object? sender, RoutedEventArgs e)
    {
        _id = (sender as Button)?.Tag as string == "id";
        ApplyLanguage();
        _panelKey = "";
        QueueRefresh();
    }

    private void ApplyLanguage()
    {
        foreach (var (en, id) in new[] { (LangEn, LangId), (SLangEn, SLangId) }) { en.Classes.Set("on", !_id); id.Classes.Set("on", _id); }
        HeroEyebrow.Text = T("2D DRAFTING · 3D SOLIDS · CAD FORMATS", "DRAFTING 2D · SOLID 3D · FORMAT CAD");
        HeroLine1.Text = T("TYPE A COMMAND.", "KETIK PERINTAH.");
        HeroLine2.Text = T("BUILD THE PART.", "BANGUN KOMPONENNYA.");
        HeroLine2.Inlines = null;
        HeroLine2.Foreground = (IBrush)this.FindResource("SelectBrush")!;
        HeroLede.Text = T("A command-line CAD studio for your desktop: draw with LINE, CIRCLE and PLINE, extrude to solids, cut holes with SUBTRACT, then export STEP, STL, 3MF, DXF or PDF.",
                          "Studio CAD berbasis baris perintah di desktop: gambar dengan LINE, CIRCLE dan PLINE, extrude menjadi solid, buat lubang dengan SUBTRACT, lalu ekspor STEP, STL, 3MF, DXF atau PDF.");
        BtnNew.Content = T("NEW BLANK DRAWING", "GAMBAR KOSONG BARU");
        BtnOpen.Content = T("OPEN A FILE…", "BUKA FILE…");
        BtnBack.Content = $"{T("BACK TO", "KEMBALI KE")} “{Doc.Name.ToUpperInvariant()}”";
        TplTitle.Text = T("START FROM A TEMPLATE", "MULAI DARI TEMPLATE");
        TplSub.Text = T("Sheets, layers, units and grid set up for the job.", "Lembar, layer, satuan dan grid sudah disiapkan.");
        var cats = StudioCatalog.Categories.Where(c => StudioCatalog.Samples.Any(s => s.Category == c)).ToList();
        SmpTitle.Text = T("OPEN A SAMPLE", "BUKA CONTOH");
        SmpSub.Text = T($"{StudioCatalog.Samples.Count} finished models across {cats.Count} industries — every one is editable.", $"{StudioCatalog.Samples.Count} model jadi dari {cats.Count} industri — semuanya bisa diedit.");
        FootLeft.Text = T("Built on the open-source Gravicode.CAD library for .NET.", "Dibangun di atas pustaka open-source Gravicode.CAD untuk .NET.");
        FootRight.Text = GravicodeCad.CreditsId;
        Empty3DText.Text = T("Nothing to show in 3D yet.", "Belum ada yang tampil di 3D.");
        Empty3DHint.Text = T("Draw a closed shape and EXTRUDE it, or start with BOX.", "Gambar bentuk tertutup lalu EXTRUDE, atau mulai dengan BOX.");
        Tag2D.Text = T("TOP · MODEL", "ATAS · MODEL");
        BuildTabs();
        BuildCards();
    }

    private void BuildStart()
    {
        Tape.Children.Clear();
        foreach (var (cmd, rest, kind) in new[] { ("Command:", " CIRCLE 0,0 40", 0), ("Command:", " EXTRUDE L ; 25", 0), ("Command:", " GEAR 0,0 24 2 18", 0), ("", "  Gear: 24 teeth, module 2, pitch Ø48", 1), ("Command:", " SUBTRACT ALL", 0), ("Command:", " ", 2) })
        {
            var tb = new TextBlock { Classes = { "mono" }, FontSize = 13, LineHeight = 22 };
            tb.Inlines = [new Avalonia.Controls.Documents.Run(cmd) { Foreground = (IBrush)this.FindResource("TextBrush")!, FontWeight = FontWeight.Medium }, new Avalonia.Controls.Documents.Run(rest) { Foreground = kind == 1 ? (IBrush)this.FindResource("OutBrush")! : (IBrush)this.FindResource("MutedBrush")! }];
            if (kind == 2) tb.Inlines.Add(new Avalonia.Controls.Documents.Run("█") { Foreground = (IBrush)this.FindResource("SelectBrush")! });
            Tape.Children.Add(tb);
        }
    }

    private void BuildCards()
    {
        TemplateCards.Children.Clear();
        foreach (var t in StudioCatalog.Templates) TemplateCards.Children.Add(Card(t, _tplW));
        CategoryChips.Children.Clear();
        var all = new Button { Content = T("ALL", "SEMUA"), Classes = { "pill" }, Margin = new(0, 0, 6, 6) };
        all.Classes.Set("on", _category is null);
        all.Click += (_, _) => { _category = null; BuildCards(); };
        CategoryChips.Children.Add(all);
        foreach (var c in StudioCatalog.Categories.Where(c => StudioCatalog.Samples.Any(s => s.Category == c)))
        {
            var cat = c;
            var b = new Button { Content = c.ToUpperInvariant(), Classes = { "pill" }, Margin = new(0, 0, 6, 6) };
            b.Classes.Set("on", _category == c);
            b.Click += (_, _) => { _category = cat; BuildCards(); };
            CategoryChips.Children.Add(b);
        }
        SampleCards.Children.Clear();
        foreach (var s in StudioCatalog.Samples.Where(s => _category is null || s.Category == _category)) SampleCards.Children.Add(Card(s, _cardW));
    }

    private Button Card(CatalogItem item, double width)
    {
        var stack = new StackPanel { Width = width };
        stack.Children.Add(new Border { Height = width * 10 / 16, Background = (IBrush)this.FindResource("PaperBrush")!, BorderBrush = (IBrush)this.FindResource("G700")!, BorderThickness = new(0, 0, 0, 1), ClipToBounds = true, Child = new Image { Source = Thumb(item.Id), Stretch = Stretch.UniformToFill } });
        stack.Children.Add(new TextBlock { Text = item.Category.ToUpperInvariant(), FontFamily = (FontFamily)this.FindResource("LabelFont")!, FontWeight = FontWeight.SemiBold, FontSize = 11.5, LetterSpacing = 1.8, Foreground = (IBrush)this.FindResource("SelectBrush")!, Margin = new(12, 10, 12, 2) });
        stack.Children.Add(new TextBlock { Text = item.Title, FontFamily = (FontFamily)this.FindResource("LabelFont")!, FontWeight = FontWeight.SemiBold, FontSize = item.IsTemplate ? 16 : 18, Margin = new(12, 0, 12, 4), TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = _id ? item.SummaryId : item.Summary, FontSize = 12.5, Foreground = (IBrush)this.FindResource("MutedBrush")!, TextWrapping = TextWrapping.Wrap, MaxLines = 3, Height = 54, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(12, 0, 12, 12) });
        var b = new Button { Content = stack, Classes = { "card" } };
        b.Click += async (_, _) => await OpenItemAsync(item);
        return b;
    }

    private static readonly Dictionary<string, Bitmap?> ThumbCache = [];
    private static Bitmap? Thumb(string id)
    {
        if (ThumbCache.TryGetValue(id, out var b)) return b;
        try { using var s = AssetLoader.Open(new Uri($"avares://GravicodeCadStudio/Assets/thumbs/{id}.png")); b = new Bitmap(s); }
        catch { b = null; }
        return ThumbCache[id] = b;
    }

    private async Task OpenItemAsync(CatalogItem item)
    {
        Busy.IsVisible = true; BusyText.Text = $"{T("BUILDING", "MEMBANGUN")} {item.Title.ToUpperInvariant()}…";
        try { var doc = await Task.Run(item.Create); Load(doc); }
        catch (Exception ex) { ShowToast(ex.Message); }
        finally { Busy.IsVisible = false; }
    }

    private static IBrush BlueprintBrush()
    {
        // 24 px minor / 120 px major grid on blueprint paper, as a tiled drawing
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Color.Parse("#0F2747")), Geometry = new RectangleGeometry(new Rect(0, 0, 120, 120)) });
        var minor = new Pen(new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), 1);
        for (int i = 0; i < 5; i++)
        {
            var pen = i == 0 ? major : minor;
            group.Children.Add(new GeometryDrawing { Pen = pen, Geometry = new LineGeometry(new Point(i * 24 + 0.5, 0), new Point(i * 24 + 0.5, 120)) });
            group.Children.Add(new GeometryDrawing { Pen = pen, Geometry = new LineGeometry(new Point(0, i * 24 + 0.5), new Point(120, i * 24 + 0.5)) });
        }
        return new DrawingBrush(group) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 120, 120, RelativeUnit.Absolute), Stretch = Stretch.None };
    }

    // ================================================================== files

    private static FilePickerFileType[] OpenTypes =>
    [
        new("Gravicode CAD project / CAD files") { Patterns = [.. ProjectFile.ImportExtensions.Select(e => "*." + e).Prepend("*.gcproj")] },
        new("Gravicode CAD project") { Patterns = ["*.gcproj"] },
    ];

    private async Task OpenDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = T("Open a drawing", "Buka gambar"), FileTypeFilter = OpenTypes });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path != null) await OpenPathAsync(path);
    }

    private async Task OpenPathAsync(string path)
    {
        Busy.IsVisible = true; BusyText.Text = $"{T("OPENING", "MEMBUKA")} {System.IO.Path.GetFileName(path).ToUpperInvariant()}…";
        try
        {
            var data = await File.ReadAllBytesAsync(path);
            var doc = await Task.Run(() => ProjectFile.Open(path, data));
            Load(doc, path.EndsWith(".gcproj", StringComparison.OrdinalIgnoreCase) ? path : null);
            ShowToast($"{T("Opened", "Dibuka")} {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex) { ShowToast($"{T("Could not open", "Tidak dapat membuka")} {System.IO.Path.GetFileName(path)}: {ex.Message}"); }
        finally { Busy.IsVisible = false; }
    }

    private string FileBase() => string.Concat(Doc.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')).Trim('-');

    private async Task<bool> SaveAsync()
    {
        string? path = _filePath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = T("Save project", "Simpan proyek"), SuggestedFileName = FileBase() + ".gcproj", DefaultExtension = "gcproj",
                FileTypeChoices = [new FilePickerFileType("Gravicode CAD project") { Patterns = ["*.gcproj"] }]
            });
            path = file?.TryGetLocalPath();
            if (path is null) return false;
        }
        ProjectFile.Save(Doc, path);
        _filePath = path;
        Doc.IsDirty = false;
        QueueRefresh();
        ShowToast($"{T("Saved", "Disimpan")} {System.IO.Path.GetFileName(path)}");
        return true;
    }

    private async Task ExportAsync(string ext)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"{T("Export", "Ekspor")} {ext.ToUpperInvariant()}", SuggestedFileName = $"{FileBase()}.{ext}", DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(ext.ToUpperInvariant()) { Patterns = [$"*.{ext}"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            var bytes = await Task.Run(() => ProjectFile.Export(Doc, ext));
            await File.WriteAllBytesAsync(path, bytes);
            ShowToast($"{T("Exported", "Diekspor")} {ext.ToUpperInvariant()} · {bytes.Length / 1024.0:N0} KB");
        }
        catch (Exception ex) { ShowToast($"{T("Export failed", "Ekspor gagal")}: {ex.Message}"); }
    }

    private bool _closeConfirmed;
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !Doc.IsDirty || Doc.Entities.Count + Doc.Solids.Count == 0) return;
        e.Cancel = true;
        var dlg = new Window
        {
            Title = "Gravicode CAD Studio", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush)this.FindResource("G900")!
        };
        string choice = "cancel";
        var save = new Button { Content = T("SAVE", "SIMPAN"), Classes = { "flat", "primary" } };
        var discard = new Button { Content = T("DON'T SAVE", "JANGAN SIMPAN"), Classes = { "flat" } };
        var cancel = new Button { Content = T("CANCEL", "BATAL"), Classes = { "flat" } };
        save.Click += (_, _) => { choice = "save"; dlg.Close(); };
        discard.Click += (_, _) => { choice = "discard"; dlg.Close(); };
        cancel.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel
        {
            Margin = new(20), Spacing = 16,
            Children =
            {
                new TextBlock { Text = T($"Save changes to “{Doc.Name}”?", $"Simpan perubahan pada “{Doc.Name}”?"), FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, discard, save } }
            }
        };
        await dlg.ShowDialog(this);
        if (choice == "cancel") return;
        if (choice == "save" && !await SaveAsync()) return;
        _closeConfirmed = true;
        Close();
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.IsVisible = true;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        _toastTimer.Tick += (_, _) => { Toast.IsVisible = false; _toastTimer?.Stop(); };
        _toastTimer.Start();
    }

    // ================================================================== screenshot / demo automation

    /// <summary>GCAD_STUDIO_DEMO="sample-id;COMMAND;COMMAND…" opens a sample and runs commands (used for docs screenshots).</summary>
    private async Task RunDemoAsync(string demo)
    {
        var parts = demo.Split('|');
        if (parts[0] == "start")
        {
            await Task.Delay(300);
            foreach (var p in parts.Skip(1))
                if (p.StartsWith("scroll:")) StartScreen.Offset = new Vector(0, double.Parse(p[7..], CultureInfo.InvariantCulture));
                else if (p == "id") { _id = true; ApplyLanguage(); }
            return;
        }
        if (StudioCatalog.Find(parts[0]) is { } item) await OpenItemAsync(item);
        await Task.Delay(400);
        foreach (var cmd in parts.Skip(1))
        {
            if (cmd.StartsWith("view:")) SetView(cmd[5..]);
            else if (cmd.StartsWith("type:")) { CmdBox.Text = cmd[5..]; CmdBox.CaretIndex = CmdBox.Text.Length; }
            else _engine.Submit(cmd);
            await Task.Delay(250);
        }
        RefreshNow();
    }
}

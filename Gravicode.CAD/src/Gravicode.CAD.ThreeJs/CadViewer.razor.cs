using Gravicode.CAD.IO;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Gravicode.CAD.ThreeJs;

/// <summary>A click in the viewer: the body index (in model order, visible non-empty bodies) or -1 for empty space,
/// the hit point (CAD space) and whether Shift/Ctrl was held.</summary>
public sealed record CadPickEventArgs(int BodyIndex, Vec3 Point, bool Additive);

/// <summary>A finished gizmo drag: <c>"move"</c> with <see cref="Delta"/>, or <c>"rotate"</c> by <see cref="Angle"/>
/// radians about <see cref="Axis"/> through <see cref="Pivot"/> (CAD space).</summary>
public sealed record CadGizmoEventArgs(string Mode, Vec3 Delta, Vec3 Axis, double Angle, Vec3 Pivot);

/// <summary>
/// three.js CAD viewer. Give it a <see cref="Model"/> (built in .NET) or a <see cref="Source"/> URL of any
/// supported CAD file (STEP, STL, OBJ, glTF/GLB, 3MF, DXF, IGES) — files are parsed by Gravicode.CAD in .NET,
/// so STEP works in the browser too.
/// <code>&lt;CadViewer Source="models/gear.step" Height="520px" /&gt;</code>
/// </summary>
public partial class CadViewer : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    /// <summary>URL (relative to the app base) of a CAD file to display.</summary>
    [Parameter] public string? Source { get; set; }
    /// <summary>Model to display (takes precedence over <see cref="Source"/>).</summary>
    [Parameter] public CadModel? Model { get; set; }
    [Parameter] public string Height { get; set; } = "480px";
    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }
    [Parameter] public bool ShowEdges { get; set; } = true;
    [Parameter] public bool ShowGrid { get; set; } = true;
    [Parameter] public bool ShowToolbar { get; set; } = true;
    [Parameter] public bool AutoRotate { get; set; }
    /// <summary>"dark" (default) or "light".</summary>
    [Parameter] public string Theme { get; set; } = "dark";
    /// <summary>Raised after a model has been loaded and shown.</summary>
    [Parameter] public EventCallback<CadModel> OnLoaded { get; set; }

    /// <summary>Clicks report bodies through <see cref="OnPick"/>.</summary>
    [Parameter] public bool Selectable { get; set; }
    /// <summary>Body indices drawn as selected (orange glow).</summary>
    [Parameter] public IReadOnlyCollection<int>? Highlighted { get; set; }
    /// <summary>Gizmo on the highlighted bodies: null (off), "move" or "rotate".</summary>
    [Parameter] public string? GizmoMode { get; set; }
    /// <summary>Move-gizmo step in model units (0 = free); rotations snap to 15°. Alt drags freely.</summary>
    [Parameter] public double GizmoSnap { get; set; }
    /// <summary>Keep the camera when <see cref="Model"/> changes (editors); otherwise every new model is framed.</summary>
    [Parameter] public bool KeepViewOnUpdate { get; set; }
    [Parameter] public EventCallback<CadPickEventArgs> OnPick { get; set; }
    [Parameter] public EventCallback<CadGizmoEventArgs> OnGizmo { get; set; }

    private ElementReference _host;
    private IJSObjectReference? _module;
    private DotNetObjectReference<CadViewer>? _self;
    private string? _interactionKey;

    [JSInvokable]
    public Task OnPickFromJs(int body, double x, double y, double z, bool additive) =>
        OnPick.InvokeAsync(new CadPickEventArgs(body, new Vec3(x, y, z), additive));

    [JSInvokable]
    public Task OnGizmoFromJs(string mode, double dx, double dy, double dz, double ax, double ay, double az, double angle, double px, double py, double pz) =>
        OnGizmo.InvokeAsync(new CadGizmoEventArgs(mode, new Vec3(dx, dy, dz), new Vec3(ax, ay, az), angle, new Vec3(px, py, pz)));

    private async Task PushInteractionAsync(bool force = false)
    {
        if (_viewer is null) return;
        var hl = Highlighted?.OrderBy(i => i).ToArray() ?? [];
        var key = $"{Selectable}|{GizmoMode}|{GizmoSnap}|{string.Join(",", hl)}";
        if (!force && key == _interactionKey) return;
        _interactionKey = key;
        await _viewer.InvokeVoidAsync("setInteraction", new { selectable = Selectable, highlighted = hl, gizmo = GizmoMode, snap = GizmoSnap });
    }
    private IJSObjectReference? _viewer;
    private string? _status;
    private object? _shownKey;
    public CadModel? CurrentModel { get; private set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Gravicode.CAD.ThreeJs/cadViewer.js");
            _self = DotNetObjectReference.Create(this);
            _viewer = await _module.InvokeAsync<IJSObjectReference>("createViewer", _host, new { theme = Theme, grid = ShowGrid, autoRotate = AutoRotate }, _self);
        }
        object? key = (object?)Model ?? Source;
        if (_viewer != null && !ReferenceEquals(key, _shownKey) && !Equals(key, _shownKey))
        {
            _shownKey = key;
            await ShowAsync();
            await PushInteractionAsync(force: true);
        }
        else await PushInteractionAsync();
    }

    private async Task ShowAsync()
    {
        try
        {
            var model = Model;
            if (model is null && !string.IsNullOrWhiteSpace(Source))
            {
                _status = "Loading…"; StateHasChanged();
                using var http = new HttpClient { BaseAddress = new Uri(Nav.BaseUri) };
                var bytes = await http.GetByteArrayAsync(Source);
                CadIO.RegisterAll();
                model = await Task.Run(() => CadModel.Load(new MemoryStream(bytes), Path.GetExtension(new Uri(new Uri(Nav.BaseUri), Source).AbsolutePath)));
                if (model.Name == "Untitled") model.Name = Path.GetFileNameWithoutExtension(Source);
            }
            if (model is null) return;
            CurrentModel = model;
            var (scene, buffer) = await Task.Run(() => ViewerPayload.Build(model, edges: true));
            await _viewer!.InvokeVoidAsync("setScene", scene, buffer, ShowEdges, KeepViewOnUpdate);
            _status = null;
            await OnLoaded.InvokeAsync(model);
        }
        catch (Exception ex)
        {
            _status = "Could not display model: " + ex.Message;
        }
        StateHasChanged();
    }

    /// <summary>Forces a refresh (call after mutating <see cref="Model"/> in place).</summary>
    public async Task RefreshAsync() { _shownKey = null; await ShowAsync(); }

    public async Task SetViewAsync(string view) { if (_viewer != null) await _viewer.InvokeVoidAsync("setView", view); }
    public async Task FitAsync() { if (_viewer != null) await _viewer.InvokeVoidAsync("fit"); }

    public async Task ToggleEdgesAsync()
    {
        ShowEdges = !ShowEdges;
        if (_viewer != null) await _viewer.InvokeVoidAsync("setEdges", ShowEdges);
    }

    /// <summary>Returns a PNG data URL of the current view.</summary>
    public async Task<string?> SnapshotAsync() => _viewer is null ? null : await _viewer.InvokeAsync<string>("snapshot");

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_viewer != null) { await _viewer.InvokeVoidAsync("dispose"); await _viewer.DisposeAsync(); }
            if (_module != null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        _self?.Dispose();
    }
}

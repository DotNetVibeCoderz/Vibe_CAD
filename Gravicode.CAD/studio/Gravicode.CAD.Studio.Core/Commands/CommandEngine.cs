using System.Globalization;
using System.Text.RegularExpressions;

namespace Gravicode.CAD.Studio.Commands;

/// <summary>Drafting aids (status-bar toggles).</summary>
public sealed class DraftSettings
{
    public bool GridSnap { get; set; }
    public double GridSize { get; set; } = 10;
    public bool Ortho { get; set; }
    public bool ObjectSnap { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public string CurrentColor { get; set; } = "ByLayer";
    // remembered command options (as AutoCAD does between runs)
    public double FilletRadius { get; set; } = 10;
    public double ChamferA { get; set; } = 5;
    public double ChamferB { get; set; } = 5;
    public string HatchPattern { get; set; } = "ANSI31";
    /// <summary>Hatch pattern scale; 0 = automatic (about 40 lines across the area, rounded to 1/2/5 steps).</summary>
    public double HatchScale { get; set; }
    public double HatchAngle { get; set; }
}

/// <summary>Command definition.</summary>
public sealed record CommandInfo(string Name, string[] Aliases, string Group, string Description, Func<CommandContext, IEnumerable<Prompt>> Run);

/// <summary>State handed to a running command.</summary>
public sealed class CommandContext(CommandEngine engine)
{
    public CommandEngine Engine { get; } = engine;
    public StudioDocument Doc => Engine.Document;
    public DraftSettings Settings => Engine.Settings;
    /// <summary>Response to the prompt that was just yielded.</summary>
    public Input Input { get; internal set; } = new Input.EndInput();
    public Vec2 Cursor => Engine.Cursor;
    public Vec2? LastPoint { get => Engine.LastPoint; set => Engine.LastPoint = value; }

    public bool IsEnd => Input is Input.EndInput;
    public Vec2 Point => Input is Input.PointInput p ? p.Value : throw new InvalidOperationException("A point was expected.");
    public double Number => Input switch
    {
        Input.NumberInput n => n.Value,
        _ => throw new InvalidOperationException("A number was expected.")
    };
    public string Text => Input switch { Input.TextInput t => t.Value, Input.KeywordInput k => k.Value, _ => "" };
    public string? Keyword => Input is Input.KeywordInput k ? k.Value : null;
    public Input.FaceInput? Face => Input as Input.FaceInput;

    public void Print(string message) => Engine.Print(message);
    public void RequestView(string what) => Engine.RequestView(what);

    public IReadOnlyList<StudioEntity> SelectedEntities => [.. Doc.Entities.Where(e => Engine.Selection.Contains(e.Id))];
    public IReadOnlyList<StudioSolid> SelectedSolids => [.. Doc.Solids.Where(s => Engine.Selection.Contains(s.Id))];
    public bool HasSelection => Engine.Selection.Count > 0;

    /// <summary>The drawing object under a picked point (null when nothing is close enough).</summary>
    public StudioEntity? PickAt(Vec2 p) =>
        Engine.Pick(p, Engine.EffectivePickTolerance) is { } id ? Doc.Entities.FirstOrDefault(e => e.Id == id) : null;
    public string? EntityColor => Settings.CurrentColor == "ByLayer" ? null : Settings.CurrentColor;
}

/// <summary>
/// AutoCAD-style command processor shared by the web and desktop editions: command line with aliases and inline
/// arguments, interactive prompts answered by clicks or typed coordinates, object snaps, ortho, selection,
/// repeat-last-command and undo/redo.
/// </summary>
public sealed partial class CommandEngine
{
    private readonly Dictionary<string, CommandInfo> _commands = new(StringComparer.OrdinalIgnoreCase);
    private IEnumerator<Prompt>? _running;
    private CommandContext? _ctx;
    private CommandInfo? _active;
    private readonly LinkedList<string> _queued = new();
    private string? _lastCommand;

    public StudioDocument Document { get; private set; }
    public DraftSettings Settings { get; } = new();
    public HashSet<Guid> Selection { get; } = [];
    public List<string> History { get; } = [];
    public Prompt? CurrentPrompt { get; private set; }
    public CommandInfo? ActiveCommand => _active;
    public bool IsBusy => _running != null;
    public Vec2 Cursor { get; set; }
    public Vec2? LastPoint { get; set; }
    public SnapResult? LastSnap { get; private set; }
    /// <summary>Most recently created object (script selection token "L").</summary>
    public Guid? LastCreated { get; set; }
    private readonly HashSet<Guid> _previous = [];
    private bool _stay;

    public event Action? StateChanged;
    public event Action<string>? ViewRequest;           // "zoom-extents", "view-3d", "view-2d", ...
    /// <summary>A command produced a file for the user (PLOT): file name, bytes, MIME type. The web edition
    /// downloads it, the desktop edition asks where to save it.</summary>
    public event Action<string, byte[], string>? FileProduced;

    /// <summary>Grip being dragged (shown red), or null.</summary>
    public (Guid Entity, int Index)? HotGrip { get; private set; }

    /// <summary>World-space pick radius from the last click (≈ 7 px); 0 until the first click.</summary>
    public double PickTolerance { get; private set; }

    /// <summary>Pick radius for commands; typed coordinates (scripts) fall back to a size relative to the drawing.</summary>
    public double EffectivePickTolerance
    {
        get
        {
            if (PickTolerance > 0) return PickTolerance;
            var bb = Document.Bounds;
            return bb.IsEmpty ? 0.5 : Math.Max(1e-3, bb.Diagonal * 0.003);
        }
    }

    internal void Deliver(string fileName, byte[] data, string mime)
    {
        if (FileProduced is null) { Print($"  {fileName} ready ({data.Length / 1024.0:N0} KB) — this host cannot save files."); return; }
        FileProduced(fileName, data, mime);
        Print($"  Output: {fileName} ({data.Length / 1024.0:N0} KB).");
    }

    public IReadOnlyCollection<CommandInfo> Commands => [.. _commands.Values.Distinct()];

    public CommandEngine(StudioDocument document)
    {
        Document = document;
        StudioCommands.RegisterAll(this);
    }

    public void SetDocument(StudioDocument doc)
    {
        Cancel(silent: true);
        Document = doc;
        Selection.Clear();
        LastPoint = null;
        Raise();
    }

    public void Register(CommandInfo info)
    {
        _commands[info.Name] = info;
        foreach (var a in info.Aliases) _commands[a] = info;
    }

    public CommandInfo? Find(string nameOrAlias) => _commands.GetValueOrDefault(nameOrAlias);

    public void Print(string message)
    {
        History.Add(message);
        if (History.Count > 400) History.RemoveRange(0, 100);
    }

    internal void RequestView(string what) => ViewRequest?.Invoke(what);
    private void Raise() => StateChanged?.Invoke();

    public string PromptText => CurrentPrompt?.Display ?? "Command:";

    // ===================================================================== input entry points

    /// <summary>Text typed in the command line (Enter pressed). Empty text = Enter.</summary>
    public void Submit(string text)
    {
        text = text.Trim();
        if (_running == null)
        {
            if (text.Length == 0)
            {
                if (_lastCommand != null) Start(_lastCommand);
                return;
            }
            var tokens = Tokenize(text);
            // ";" is Enter inside an inline command line, as in scripts ("EXTRUDE L ; 25")
            Start(tokens[0], tokens.Skip(1).Select(t => t == ";" ? "" : t));
            return;
        }
        Print($"{CurrentPrompt?.Display} {text}");
        if (text.Length == 0 || text == ";") { Feed(new Input.EndInput()); return; }
        var input = Parse(text, CurrentPrompt!);
        if (input is null) { Print($"  Invalid input \"{text}\" — {Hint(CurrentPrompt!)}"); Raise(); return; }
        Feed(input);
    }

    /// <summary>Starts a command by name or alias, optionally with queued inline arguments.</summary>
    public bool Start(string name, IEnumerable<string>? args = null)
    {
        var info = Find(name.TrimStart('_', '.'));
        if (info is null) { Print($"Unknown command \"{name}\". Type HELP for a list."); Raise(); return false; }
        Launch(info, args, remember: true);
        return true;
    }

    private void Launch(CommandInfo info, IEnumerable<string>? args, bool remember)
    {
        Cancel(silent: true);
        _active = info;
        if (remember) _lastCommand = info.Name;
        _queued.Clear();
        if (args != null) foreach (var a in args) _queued.AddLast(a);
        if (remember) Print($"Command: {info.Name}");
        _ctx = new CommandContext(this);
        _running = info.Run(_ctx).GetEnumerator();
        Advance();
    }

    // ===================================================================== 3D picking & gizmos

    /// <summary>
    /// Click in a 3D view: the ray (CAD space) selects the nearest solid, Shift adds/removes. Answers a running
    /// selection prompt like a 2D pick. Returns the hit (null = empty space, which clears the selection).
    /// </summary>
    public SolidHit? Pick3D(Vec3 origin, Vec3 direction, bool additive = false)
    {
        if (_running != null && CurrentPrompt?.Kind is not (PromptKind.Select or PromptKind.Face)) return null;
        var hit = Solid3D.Raycast(Document, origin, direction);
        if (CurrentPrompt?.Kind == PromptKind.Face)
        {
            if (hit is { } h) PickFace(h.Id, h.Point, h.Normal);
            return hit;
        }
        PickObject(hit?.Id, additive);
        return hit;
    }

    /// <summary>Selects an object picked by a viewer (null = empty space). Same rules as a 2D click: replaces the
    /// selection unless additive (Shift toggles), and answers a running selection prompt.</summary>
    public void PickObject(Guid? id, bool additive = false)
    {
        if (_running != null && CurrentPrompt?.Kind != PromptKind.Select) return;
        if (!additive && _running == null) Selection.Clear();
        if (id is { } h && !Selection.Remove(h)) Selection.Add(h);
        if (_running != null && CurrentPrompt is { Single: true } && Selection.Count > 0) { Feed(new Input.EndInput()); return; }
        Raise();
    }

    /// <summary>Answers a face prompt (UCS Face) with a face picked by a viewer.</summary>
    public void PickFace(Guid solid, Vec3 point, Vec3 normal)
    {
        if (CurrentPrompt?.Kind != PromptKind.Face || normal.LengthSquared < 1e-18) return;
        Print($"{CurrentPrompt.Display} face of {Document.Solids.FirstOrDefault(s => s.Id == solid)?.Name ?? "solid"}");
        Feed(new Input.FaceInput(solid, point, normal.Normalized()));
    }

    /// <summary>Switches the 2D view between model space, a work plane and a layout (null = model).</summary>
    public void SetSpace(string? key)
    {
        if (key != null && !Document.Planes.ContainsKey(key) && !Document.Layouts.ContainsKey(key)) return;
        if (Document.ActiveSpace == key) return;
        Cancel(silent: true);
        Selection.Clear();
        Document.ActiveSpace = key;
        Document.Touch();
        Print($"Space: {SpaceName(key)}");
        RequestView("zoom-extents");
        Raise();
    }

    public string SpaceName(string? key) =>
        key is null ? "Model" : Document.Planes.TryGetValue(key, out var p) ? p.Name : Document.Layouts.TryGetValue(key, out var l) ? l.Name : key;

    /// <summary>Selected solids (the gizmo acts on these).</summary>
    public IReadOnlyList<Guid> SelectedSolidIds => [.. Document.Solids.Where(s => Selection.Contains(s.Id)).Select(s => s.Id)];

    /// <summary>Gizmo move: translates the selected solids (one undo step).</summary>
    public void Move3D(Vec3 delta)
    {
        if (_running != null || delta.LengthSquared < 1e-24) return;
        int n = Solid3D.Transform(Document, SelectedSolidIds, Mat4.Translation3(delta), "Move 3D");
        if (n > 0) Print($"Moved {n} solid(s) by {delta.X:0.###}, {delta.Y:0.###}, {delta.Z:0.###}");
        Raise();
    }

    /// <summary>Gizmo rotate: turns the selected solids about an axis through <paramref name="pivot"/>
    /// (default: the centre of their bounds).</summary>
    public void Rotate3D(Vec3 axis, double radians, Vec3? pivot = null)
    {
        if (_running != null || Math.Abs(radians) < 1e-12 || axis.LengthSquared < 1e-24) return;
        var ids = SelectedSolidIds;
        var c = pivot ?? Solid3D.Bounds(Document, ids).Center;
        int n = Solid3D.Transform(Document, ids, Mat4.RotateAround(c, axis.Normalized(), radians), "Rotate 3D");
        if (n > 0) Print($"Rotated {n} solid(s) {Units.ToDegrees(radians):0.##}° about ({axis.X:0.##}, {axis.Y:0.##}, {axis.Z:0.##})");
        Raise();
    }

    /// <summary>Gizmo scale: uniform scale of the selected solids about <paramref name="pivot"/> (default: their centre).</summary>
    public void Scale3D(double factor, Vec3? pivot = null)
    {
        if (_running != null || factor <= 0 || Math.Abs(factor - 1) < 1e-12) return;
        var ids = SelectedSolidIds;
        var c = pivot ?? Solid3D.Bounds(Document, ids).Center;
        var m = Mat4.Translation3(c) * Mat4.Scale(factor) * Mat4.Translation3(-1 * c);
        int n = Solid3D.Transform(Document, ids, m, "Scale 3D");
        if (n > 0) Print($"Scaled {n} solid(s) ×{factor:0.###}");
        Raise();
    }

    private (StudioDocument?, int, string?) _refKey;
    private List<(DrawingEntity Geometry, string Color)> _ref = [];

    /// <summary>Reference geometry of the active space, cached per document version.</summary>
    public IReadOnlyList<(DrawingEntity Geometry, string Color)> ReferenceGeometry()
    {
        var key = (Document, Document.Version, Document.ActiveSpace);
        if (key != _refKey) { _ref = Spaces.Reference(Document); _refKey = key; }
        return _ref;
    }

    // ===================================================================== grips

    /// <summary>When idle with a selection, a click on a grip starts a stretch: the next click places it.</summary>
    private bool TryStartGrip(Vec2 world, double tol)
    {
        if (_running != null || Selection.Count is 0 or > UI.DisplayList.MaxGripObjects) return false;
        foreach (var e in Document.Entities)
        {
            if (!Selection.Contains(e.Id) || Document.LayerOf(e.Layer).Locked) continue;
            var grips = Grips.Of(e.Geometry);
            for (int i = 0; i < grips.Count; i++)
                if (Vec2.Distance(grips[i], world) <= tol)
                {
                    var entity = e; int index = i; var at = grips[i];
                    Launch(new CommandInfo("GRIP", [], "Edit", "Grip edit", c => GripEdit(c, entity, index, at)), null, remember: false);
                    return true;
                }
        }
        return false;
    }

    private IEnumerable<Prompt> GripEdit(CommandContext c, StudioEntity e, int index, Vec2 grip)
    {
        HotGrip = (e.Id, index);
        try
        {
            yield return Prompt.Point("** STRETCH ** Specify stretch point", grip, p => Grips.Move(e.Geometry, index, p) is { } g ? [g] : [], allowEnd: true);
            if (c.IsEnd) yield break;
            if (Grips.Move(e.Geometry, index, c.Point) is not { } moved) { Print("  That would collapse the object."); yield break; }
            Document.Edit("Grip edit", d =>
            {
                int k = d.Entities.FindIndex(x => x.Id == e.Id);
                if (k >= 0) d.Entities[k] = d.Entities[k] with { Geometry = moved };
            });
        }
        finally { HotGrip = null; }
    }

    /// <summary>Runs a whole script ("LINE 0,0 100,0 ;") line by line — used by samples, tests and macros.</summary>
    public void RunScript(string script)
    {
        foreach (var raw in script.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var tokens = Tokenize(line);
            Start(tokens[0], tokens.Skip(1).Select(t => t == ";" ? "" : t));
            // finish open commands (e.g. LINE waits for Enter)
            int guard = 0;
            while (_running != null && guard++ < 8) Feed(new Input.EndInput());
        }
    }

    /// <summary>A click in model space (already snapped by the caller via <see cref="Snap"/>).</summary>
    public void Click(Vec2 world, double pickTolerance, bool additive = false)
    {
        if (pickTolerance > 0) PickTolerance = pickTolerance;
        if (_running == null && !additive && TryStartGrip(world, pickTolerance)) return;
        if (_running != null && CurrentPrompt is { } p)
        {
            switch (p.Kind)
            {
                case PromptKind.Point:
                case PromptKind.Distance:
                    var pt = ApplyOrtho(world, p.Base);
                    Print($"{p.Display} {pt.X:0.###},{pt.Y:0.###}");
                    Feed(new Input.PointInput(pt));
                    return;
                case PromptKind.Select:
                    TogglePick(world, pickTolerance);
                    if (p.Single && Selection.Count > 0) { Feed(new Input.EndInput()); return; }
                    Raise();
                    return;
            }
            return;
        }
        if (!additive) Selection.Clear();
        TogglePick(world, pickTolerance);
        Raise();
    }

    /// <summary>Window (left→right, fully inside) or crossing (right→left, touching) selection.</summary>
    public void SelectWindow(Vec2 a, Vec2 b, bool additive = false)
    {
        if (_running != null && CurrentPrompt?.Kind != PromptKind.Select) return;
        if (!additive && _running == null) Selection.Clear();
        bool crossing = b.X < a.X;
        var min = Vec2.Zero with { X = Math.Min(a.X, b.X), Y = Math.Min(a.Y, b.Y) };
        var max = new Vec2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        bool In(Vec2 p) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;
        foreach (var e in Document.VisibleEntities)
        {
            if (Document.LayerOf(e.Layer).Locked) continue;
            var pts = Geometry2D.Polyline(e.Geometry, 0.5);
            if (crossing ? pts.Any(In) || Geometry2D.Segments(e.Geometry).Any(s => CrossesBox(s.A, s.B, min, max)) : pts.All(In)) Selection.Add(e.Id);
        }
        foreach (var s in Document.ActiveSpace is null ? Document.VisibleSolids : [])
        {
            var bb = s.Mesh.Bounds;
            var lo = bb.Min.XY; var hi = bb.Max.XY;
            bool inside = In(lo) && In(hi);
            bool touch = !(hi.X < min.X || lo.X > max.X || hi.Y < min.Y || lo.Y > max.Y);
            if (crossing ? touch : inside) Selection.Add(s.Id);
        }
        if (_running == null && Selection.Count > 0) Print($"{Selection.Count} object(s) selected.");
        Raise();
    }

    private static bool CrossesBox(Vec2 a, Vec2 b, Vec2 min, Vec2 max)
    {
        Vec2[] c = [min, new(max.X, min.Y), max, new(min.X, max.Y)];
        for (int i = 0; i < 4; i++) if (Geometry2D.SegmentIntersection(a, b, c[i], c[(i + 1) % 4], out _)) return true;
        return false;
    }

    public void Enter()
    {
        if (_running == null) { Submit(""); return; }
        Print($"{CurrentPrompt?.Display} ↵");
        Feed(new Input.EndInput());
    }

    public void Escape()
    {
        if (_running != null) { Cancel(); return; }
        Selection.Clear();
        Raise();
    }

    public void Cancel(bool silent = false)
    {
        if (_running == null) return;
        _running.Dispose();
        _running = null; _ctx = null; _active = null; CurrentPrompt = null;
        _queued.Clear();
        if (!silent) Print("*Cancel*");
        Raise();
    }

    public void SelectAll()
    {
        foreach (var e in Document.VisibleEntities) Selection.Add(e.Id);
        foreach (var s in Document.VisibleSolids) Selection.Add(s.Id);
        Raise();
    }

    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        var sel = Selection.ToHashSet();
        Document.Edit("Erase", d => { d.Entities.RemoveAll(e => sel.Contains(e.Id)); d.Solids.RemoveAll(s => sel.Contains(s.Id)); });
        Print($"{sel.Count} object(s) erased.");
        Selection.Clear();
        Raise();
    }

    // ===================================================================== core loop

    private void Feed(Input input)
    {
        if (_ctx == null || _running == null) return;
        if (input is Input.PointInput pi) LastPoint = pi.Value;
        _ctx.Input = input;
        Advance();
    }

    private void Advance()
    {
        try
        {
            while (true)
            {
                if (_running == null) break;
                if (_stay) { _stay = false; goto Dispatch; }
                if (!_running.MoveNext())
                {
                    // like AutoCAD: the selection is consumed by the command (kept as "Previous")
                    if (Selection.Count > 0 && _active?.Name is not ("SELECTALL" or "GRIP")) { _previous.Clear(); _previous.UnionWith(Selection); Selection.Clear(); }
                    _running.Dispose();
                    _running = null; _ctx = null; _active = null; CurrentPrompt = null;
                    _queued.Clear();
                    break;
                }
                CurrentPrompt = _running.Current;
            Dispatch:
                if (CurrentPrompt is null) break;
                // Selection prompts are satisfied immediately by a pick-first selection.
                if (CurrentPrompt is { Kind: PromptKind.Select } && Selection.Count > 0 && _queued.Count == 0)
                {
                    _ctx!.Input = new Input.EndInput();
                    continue;
                }
                if (_queued.Count == 0) break;
                var tok = _queued.First!.Value; _queued.RemoveFirst();
                if (CurrentPrompt is { Kind: PromptKind.Select })
                {
                    // scripts select with ALL, L/LAST (last created), P (previous); ";" (Enter) ends the selection.
                    bool ended = tok.Length == 0;
                    if (tok.Equals("ALL", StringComparison.OrdinalIgnoreCase)) { foreach (var e in Document.VisibleEntities) Selection.Add(e.Id); foreach (var so in Document.VisibleSolids) Selection.Add(so.Id); }
                    else if (tok.Equals("L", StringComparison.OrdinalIgnoreCase) || tok.Equals("LAST", StringComparison.OrdinalIgnoreCase)) { if (LastCreated is { } lc) Selection.Add(lc); }
                    else if (tok.Equals("P", StringComparison.OrdinalIgnoreCase)) { foreach (var id in _previous) Selection.Add(id); }
                    else if (!ended) { _queued.AddFirst(tok); ended = true; }   // not a selection token: hand it to the next prompt
                    Print($"{CurrentPrompt.Display} {(tok.Length == 0 ? "↵" : tok)}");
                    if (!ended && _queued.Count > 0) { _stay = true; continue; }
                    if (!ended) break;
                    _ctx!.Input = new Input.EndInput();
                    continue;
                }
                Print($"{CurrentPrompt.Display} {tok}");
                var input = tok.Length == 0 ? new Input.EndInput() : Parse(tok, CurrentPrompt);
                if (input is null) { Print($"  Invalid input \"{tok}\" — {Hint(CurrentPrompt)}"); _queued.Clear(); break; }
                if (input is Input.PointInput pi) LastPoint = pi.Value;
                _ctx!.Input = input;
            }
        }
        catch (Exception ex)
        {
            Print($"  Error: {ex.Message}");
            _running?.Dispose();
            _running = null; _ctx = null; _active = null; CurrentPrompt = null;
            _queued.Clear();
        }
        Raise();
    }

    // ===================================================================== parsing

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        foreach (Match m in TokenRegex().Matches(line))
            tokens.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Value);
        return tokens;
    }

    [GeneratedRegex("\"([^\"]*)\"|\\S+")]
    private static partial Regex TokenRegex();

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static bool Num(string s, out double v) => double.TryParse(s, NumberStyles.Float, Inv, out v);

    private Input? Parse(string text, Prompt p)
    {
        // keywords first (match full word or leading capital letters)
        if (p.Keywords.Count > 0 && !char.IsDigit(text[0]) && text[0] is not '-' and not '@' and not '.')
        {
            var kw = p.Keywords.FirstOrDefault(k => k.Equals(text, StringComparison.OrdinalIgnoreCase))
                ?? p.Keywords.FirstOrDefault(k => k.StartsWith(text, StringComparison.OrdinalIgnoreCase));
            if (kw != null) return new Input.KeywordInput(kw);
        }
        switch (p.Kind)
        {
            case PromptKind.Text: return new Input.TextInput(text);
            case PromptKind.Keyword: return null;
            case PromptKind.Integer: return int.TryParse(text, NumberStyles.Integer, Inv, out var iv) ? new Input.NumberInput(iv) : null;
            case PromptKind.Select: return null;
            case PromptKind.Distance:
                if (Num(text, out var d)) return new Input.NumberInput(d);
                return ParsePoint(text, p.Base) is { } dp ? new Input.PointInput(dp) : null;
            case PromptKind.Point:
                if (ParsePoint(text, p.Base) is { } pt) return new Input.PointInput(pt);
                // direct distance entry: a number along the cursor direction from the base point
                if (Num(text, out var dist) && p.Base is { } b)
                {
                    var dir = ApplyOrtho(Cursor, b) - b;
                    if (dir.LengthSquared < 1e-18) dir = Vec2.UnitX;
                    return new Input.PointInput(b + dir.Normalized() * dist);
                }
                return null;
        }
        return null;
    }

    /// <summary>Parses "x,y", "@dx,dy" or "@dist&lt;angle" (degrees).</summary>
    public Vec2? ParsePoint(string text, Vec2? basePoint)
    {
        bool rel = text.StartsWith('@');
        var t = rel ? text[1..] : text;
        var origin = rel ? (basePoint ?? LastPoint ?? Vec2.Zero) : Vec2.Zero;
        int lt = t.IndexOf('<');
        if (lt > 0 && Num(t[..lt], out var len) && Num(t[(lt + 1)..], out var ang))
            return origin + Vec2.FromAngle(Units.Deg(ang), len);
        var parts = t.Split(',');
        if (parts.Length is 2 or 3 && Num(parts[0], out var x) && Num(parts[1], out var y)) return origin + new Vec2(x, y);
        return null;
    }

    private static string Hint(Prompt p) => p.Kind switch
    {
        PromptKind.Point => "enter x,y  @dx,dy  @dist<angle or click",
        PromptKind.Distance => "enter a number or pick two points",
        PromptKind.Integer => "enter a whole number",
        PromptKind.Keyword => $"choose {string.Join("/", p.Keywords)}",
        PromptKind.Select => "click objects, drag a window, Enter to finish",
        _ => ""
    };

    public Vec2 ApplyOrtho(Vec2 p, Vec2? basePoint)
    {
        if (!Settings.Ortho || basePoint is not { } b) return p;
        var d = p - b;
        return Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vec2(p.X, b.Y) : new Vec2(b.X, p.Y);
    }

    // ===================================================================== picking & snapping

    /// <summary>Nearest selectable object within tolerance.</summary>
    public Guid? Pick(Vec2 world, double tolerance)
    {
        Guid? best = null; double bd = tolerance;
        foreach (var e in Document.VisibleEntities)
        {
            if (Document.LayerOf(e.Layer).Locked) continue;
            var d = Geometry2D.Distance(e.Geometry, world);
            if (d <= bd) { bd = d; best = e.Id; }
        }
        if (Document.ActiveSpace is null)
            foreach (var s in Document.VisibleSolids)
            {
                var d = Geometry2D.Distance(s.PlanOutline, world);
                if (d <= bd) { bd = d; best = s.Id; }
            }
        return best;
    }

    private void TogglePick(Vec2 world, double tol)
    {
        if (Pick(world, tol) is { } id && !Selection.Remove(id)) Selection.Add(id);
    }

    /// <summary>Object snap / grid snap for the cursor. <paramref name="tolerance"/> is in world units (≈ 10 px).</summary>
    public SnapResult Snap(Vec2 cursor, double tolerance)
    {
        Cursor = cursor;
        if (CurrentPrompt is { NoSnap: true }) { LastSnap = new SnapResult(cursor, SnapKind.None); return LastSnap.Value; }
        SnapResult result = new(cursor, SnapKind.None);
        if (Settings.ObjectSnap)
        {
            double bd = tolerance;
            var near = new List<StudioEntity>();
            foreach (var e in Document.VisibleEntities)
            {
                if (Geometry2D.Distance(e.Geometry, cursor) > tolerance * 4 && e.Geometry is not CircleEntity) continue;
                near.Add(e);
                foreach (var (p, k) in Geometry2D.SnapPoints(e.Geometry))
                {
                    double d = Vec2.Distance(p, cursor) * (k is SnapKind.Midpoint or SnapKind.Quadrant ? 1.15 : 1);
                    if (d <= bd) { bd = d; result = new SnapResult(p, k); }
                }
            }
            foreach (var s in Document.ActiveSpace is null ? Document.VisibleSolids : [])
                foreach (var (a, b) in s.PlanOutline)
                    foreach (var p in new[] { a, b })
                    {
                        double d = Vec2.Distance(p, cursor);
                        if (d <= bd) { bd = d; result = new SnapResult(p, SnapKind.Endpoint); }
                    }
            // reference geometry (face outline on a work plane) snaps to its ends and midpoints
            if (Document.ActivePlane is not null)
                foreach (var (g, _) in ReferenceGeometry())
                    foreach (var (p, k) in Geometry2D.SnapPoints(g))
                    {
                        double d = Vec2.Distance(p, cursor) * (k == SnapKind.Midpoint ? 1.15 : 1);
                        if (d <= bd) { bd = d; result = new SnapResult(p, k); }
                    }
            if (result.Kind == SnapKind.None && near.Count is > 1 and < 40)
            {
                var segs = near.SelectMany(e => Geometry2D.Segments(e.Geometry).Where(s => Geometry2D.DistanceToSegment(cursor, s.A, s.B) < tolerance * 2)).Take(200).ToList();
                for (int i = 0; i < segs.Count; i++)
                    for (int j = i + 1; j < segs.Count; j++)
                        if (Geometry2D.SegmentIntersection(segs[i].A, segs[i].B, segs[j].A, segs[j].B, out var ip))
                        {
                            double d = Vec2.Distance(ip, cursor);
                            if (d <= bd) { bd = d; result = new SnapResult(ip, SnapKind.Intersection); }
                        }
            }
        }
        if (result.Kind == SnapKind.None && Settings.GridSnap)
        {
            double g = Settings.GridSize;
            result = new SnapResult(new Vec2(Math.Round(cursor.X / g) * g, Math.Round(cursor.Y / g) * g), SnapKind.Grid);
        }
        if (result.Kind is SnapKind.None or SnapKind.Grid && CurrentPrompt?.Base is { } bp && Settings.Ortho)
            result = result with { Point = ApplyOrtho(result.Point, bp) };
        LastSnap = result;
        return result;
    }

    /// <summary>Rubber-band geometry for the active prompt at the (snapped) cursor.</summary>
    public IReadOnlyList<DrawingEntity> Preview(Vec2 cursor)
    {
        var p = CurrentPrompt;
        if (p is null) return [];
        var pt = ApplyOrtho(cursor, p.Base);
        try
        {
            if (p.Preview != null) return [.. p.Preview(pt)];
            if (p.Base is { } b && p.Kind is PromptKind.Point or PromptKind.Distance) return [new LineEntity(b, pt)];
        }
        catch { /* previews must never break the UI */ }
        return [];
    }
}

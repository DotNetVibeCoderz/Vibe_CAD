using System.Diagnostics;
using System.Globalization;
using Gravicode.CAD;
using Gravicode.CAD.AI;
using Gravicode.CAD.CAM;
using Gravicode.CAD.CLI;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;
using Gravicode.CAD.Topology;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = System.Text.Encoding.UTF8;
CadIO.RegisterAll();
NativeEngine.Enable();

var a = new Args(args);
try
{
    return a.Command switch
    {
        "convert" => Convert(a),
        "info" => Info(a),
        "validate" => Validate(a),
        "render" => Render(a),
        "drawing" => Drawing(a),
        "build" => Build(a),
        "shape" => Shape(a),
        "slice" => Slice(a),
        "generate" or "ai" => await Generate(a),
        "formats" => Formats(),
        "about" or "--version" or "-v" => About(),
        _ => Help()
    };
}
catch (Exception ex)
{
    Ui.Error(ex.Message);
    if (a.Flag("verbose")) Console.Error.WriteLine(ex);
    return 1;
}

static int Help()
{
    Ui.Banner();
    Console.WriteLine("""
  Usage: gcad <command> [arguments] [options]

  Commands
    convert  <in> <out> [--units mm|cm|m|in] [--ascii]   Convert between CAD formats
    info     <file>                                       Model statistics and mass properties
    validate <file>                                       Topology check (watertight, manifold, genus)
    render   <file> <out.png> [--view iso|front|top|right] [--width 1600] [--height 1000] [--light] [--transparent]
    drawing  <file> <out.pdf|svg> [--title "Part"]       Engineering drawing (4 views + title block)
    build    <part.gcad.json> <out> [--set name=value]... Rebuild a parametric part
    shape    <box|cylinder|sphere|cone|torus|tube|gear|nut> [numbers...] -o <out>
    slice    <file> <out.gcode> [--layer 0.2] [--infill 0.2] [--perimeters 2]
    generate "<prompt>" [-o dir] [--provider claude|azure|deepseek|hf|offline] [--key-file path] [--model id]
    formats                                               Supported import/export formats
    about                                                 Version and credits

  Examples
    gcad convert model.step model.stl
    gcad render bracket.step bracket.png --view iso
    gcad generate "Create a gearbox housing with four mounting holes" -o out
    gcad shape gear 2 24 10 8 -o gear.step
""");
    return 0;
}

static int About()
{
    Ui.Banner();
    Console.WriteLine($"  Version        {GravicodeCad.Version}");
    Console.WriteLine($"  Mesh kernel    {MeshKernel.Current.Name}");
    Console.WriteLine($"  Native engine  {NativeEngine.Version} ({NativeEngine.Threads} threads)");
    Console.WriteLine($"  .NET           {Environment.Version}");
    Console.WriteLine($"  {GravicodeCad.Credits}");
    Console.WriteLine($"  {GravicodeCad.CreditsId}");
    return 0;
}

static int Formats()
{
    Ui.Title("Import");
    foreach (var i in CadFormats.Importers.OrderBy(i => i.FormatName)) Console.WriteLine($"  {i.FormatName,-14} .{string.Join(" .", i.Extensions)}");
    Ui.Title("Export");
    foreach (var e in CadFormats.Exporters.OrderBy(e => e.FormatName)) Console.WriteLine($"  {e.FormatName,-14} .{string.Join(" .", e.Extensions)}");
    Ui.Dim("  DWG: convert to DXF first (proprietary format).");
    return 0;
}

static CadModel LoadTimed(string path)
{
    var sw = Stopwatch.StartNew();
    var m = CadModel.Load(path);
    Ui.Dim($"  loaded {Path.GetFileName(path)} in {sw.ElapsedMilliseconds} ms — {m.Bodies.Count} bodies, {m.TriangleCount:N0} triangles");
    foreach (var w in m.Warnings) Ui.Warn(w);
    return m;
}

static int Convert(Args a)
{
    var (src, dst) = (a.Pos(0, "input"), a.Pos(1, "output"));
    var m = LoadTimed(src);
    if (a.Opt("units") is { } u) m.ConvertUnits(ParseUnit(u));
    m.Save(dst, new ExportOptions { Binary = !a.Flag("ascii"), Title = m.Name });
    Ui.Ok($"wrote {dst} ({new FileInfo(dst).Length:N0} bytes)");
    return 0;
}

static int Info(Args a)
{
    var m = LoadTimed(a.Pos(0, "file"));
    var bb = m.Bounds;
    Ui.Title(m.Name);
    Console.WriteLine($"  Units      {m.Units.Symbol()}");
    Console.WriteLine($"  Bounds     {bb.Min} → {bb.Max}");
    Console.WriteLine($"  Size       {bb.Size.X:0.###} × {bb.Size.Y:0.###} × {bb.Size.Z:0.###} {m.Units.Symbol()}");
    Console.WriteLine($"  Drawing    {m.Drawing.Count} entities, {m.Wires.Count} wires");
    foreach (var b in m.Bodies)
    {
        var p = b.Mesh.Properties;
        Console.WriteLine($"  • {b.Name,-24} {b.Mesh.TriangleCount,9:N0} tris  V={p.Volume,14:0.###}  A={p.Area,12:0.###}  C={p.Centroid}  {b.Color.ToHex()}");
    }
    foreach (var (k, v) in m.Metadata.Take(20)) Ui.Dim($"  {k} = {v}");
    return 0;
}

static int Validate(Args a)
{
    var m = LoadTimed(a.Pos(0, "file"));
    int bad = 0;
    foreach (var b in m.Bodies)
    {
        var r = new HalfEdgeMesh(b.Mesh).Validate();
        bool ok = r.Watertight && r.ConsistentOrientation && r.Volume > 0;
        if (!ok) bad++;
        (ok ? (Action<string>)Ui.Ok : Ui.Warn)($"{b.Name}: {r}");
    }
    return bad == 0 ? 0 : 2;
}

static int Render(Args a)
{
    var m = LoadTimed(a.Pos(0, "file"));
    var outPath = a.Pos(1, "output png");
    var o = a.Flag("light") ? RenderOptions.Light : new RenderOptions();
    var cam = new OrbitCamera();
    (cam.Yaw, cam.Pitch) = (a.Opt("view") ?? "iso") switch
    {
        "front" => (0.0, 0.0), "top" => (0.0, 89.9), "right" => (90.0, 0.0), "back" => (180.0, 0.0), "left" => (-90.0, 0.0),
        _ => (-35.0, 28.0)
    };
    cam.Fit(m.Bounds);
    o = o with { Width = a.Int("width", 1600), Height = a.Int("height", 1000), Camera = cam, TransparentBackground = a.Flag("transparent"), DrawEdges = !a.Flag("no-edges") };
    var sw = Stopwatch.StartNew();
    SoftwareRenderer.RenderToPng(m, outPath, o);
    Ui.Ok($"rendered {outPath} ({o.Width}×{o.Height}) in {sw.ElapsedMilliseconds} ms");
    return 0;
}

static int Drawing(Args a)
{
    var m = LoadTimed(a.Pos(0, "file"));
    var outPath = a.Pos(1, "output");
    m.Save(outPath, new ExportOptions { Title = a.Opt("title") ?? m.Name });
    Ui.Ok($"drawing written to {outPath}");
    return 0;
}

static int Build(Args a)
{
    var def = PartDefinition.FromJson(File.ReadAllText(a.Pos(0, "part json")));
    var overrides = a.All("set").Select(s => s.Split('=', 2)).ToDictionary(p => p[0], p => double.Parse(p[1], CultureInfo.InvariantCulture));
    var r = def.Rebuild(overrides);
    foreach (var l in r.Log) (l.Status == "ok" ? (Action<string>)Ui.Dim : Ui.Warn)($"  {l.Id,-20} {l.Status,-10} {l.Milliseconds,8:0.0} ms {l.Message}");
    var outPath = a.Pos(1, "output");
    r.Model.Save(outPath, new ExportOptions { Title = def.Name });
    Ui.Ok($"{def.Name} rebuilt in {r.Elapsed.TotalMilliseconds:0} ms → {outPath}");
    return r.Success ? 0 : 2;
}

static int Shape(Args a)
{
    var kind = a.Pos(0, "shape").ToLowerInvariant();
    double N(int i, double d) => a.Positional.Count > i + 1 && double.TryParse(a.Positional[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
    Mesh mesh = kind switch
    {
        "box" => Shapes.Box(N(0, 20), N(1, 20), N(2, 20), centered: false),
        "cylinder" => Shapes.Cylinder(N(0, 10), N(1, 20)),
        "sphere" => Shapes.Sphere(N(0, 10)),
        "cone" => Shapes.Cone(N(0, 10), N(1, 0), N(2, 20)),
        "torus" => Shapes.Torus(N(0, 20), N(1, 5)),
        "tube" => Shapes.Tube(N(0, 10), N(1, 8), N(2, 30)),
        "gear" => Shapes.SpurGear(N(0, 2), (int)N(1, 24), N(2, 10), N(3, 8)),
        "nut" => Shapes.HexNut(N(0, 13), N(1, 6.5), N(2, 8.5)),
        _ => throw new ArgumentException($"unknown shape '{kind}'")
    };
    var outPath = a.Opt("o") ?? a.Opt("out") ?? $"{kind}.step";
    new CadModel(kind, mesh, CadColor.Steel).Save(outPath);
    Ui.Ok($"{kind}: {mesh.TriangleCount:N0} triangles, volume {mesh.Volume:0.###} → {outPath}");
    return 0;
}

static int Slice(Args a)
{
    var m = LoadTimed(a.Pos(0, "file"));
    var settings = new PrintSettings { LayerHeight = a.Dbl("layer", 0.2), InfillDensity = a.Dbl("infill", 0.2), Perimeters = a.Int("perimeters", 2) };
    var sw = Stopwatch.StartNew();
    var path = Slicer.PrintPath(m.ToMesh(), settings);
    var gcode = GCodeWriter.Write([path], GCodeFlavor.Marlin, m.Name, settings);
    var outPath = a.Pos(1, "output gcode");
    File.WriteAllText(outPath, gcode);
    var (cut, _, time) = path.Estimate(settings.TravelSpeed);
    double filament = path.Moves.Sum(x => x.Extrude);
    Ui.Ok($"sliced in {sw.ElapsedMilliseconds} ms → {outPath}: {path.Moves.Count:N0} moves, path {cut / 1000:0.##} m, filament {filament / 1000:0.##} m, ≈{time:hh\\:mm}");
    return 0;
}

static async Task<int> Generate(Args a)
{
    var prompt = a.Pos(0, "prompt");
    var provider = (a.Opt("provider") ?? "auto").ToLowerInvariant();
    ICadLanguageModel? model = provider switch
    {
        "offline" => null,
        _ when a.Opt("key-file") is { } kf => ProviderFactory.FromKeyFile(kf, provider == "auto" ? "claude" : provider, a.Opt("model")),
        "auto" => ProviderFactory.FromEnvironment(),
        _ => ProviderFactory.FromEnvironment(provider)
    };
    if (provider != "offline" && model is null) Ui.Warn("no LLM credentials found — using the offline interpreter (set ANTHROPIC_API_KEY or use --key-file)");
    Ui.Dim($"  provider: {model?.Name ?? "offline rule-based"}");
    var agent = new TextToCadAgent(model);
    var r = await agent.GenerateAsync(prompt);
    var dir = a.Opt("o") ?? a.Opt("out") ?? "gcad-output";
    var files = r.ExportAll(dir);
    Ui.Title($"{r.Definition.Name}  ({r.Provider}, {r.Attempts} attempt(s), {r.Elapsed.TotalSeconds:0.0} s)");
    if (r.Explanation is { } ex) Console.WriteLine("  " + ex.Replace("\n", "\n  "));
    var bb = r.Model.Bounds;
    Console.WriteLine($"  size {bb.Size.X:0.#} × {bb.Size.Y:0.#} × {bb.Size.Z:0.#} mm, volume {r.Model.ToMesh().Volume:N0} mm³, {r.Definition.Features.Count} features");
    var png = Path.Combine(dir, Path.GetFileNameWithoutExtension(files[0]).Replace(".gcad", "") + ".png");
    SoftwareRenderer.RenderToPng(r.Model, png, new RenderOptions { Width = 1280, Height = 800 });
    foreach (var f in files.Append(png)) Ui.Ok(f);
    return r.Success ? 0 : 2;
}

static LengthUnit ParseUnit(string u) => u.ToLowerInvariant() switch
{
    "mm" => LengthUnit.Millimeter, "cm" => LengthUnit.Centimeter, "m" => LengthUnit.Meter, "in" or "inch" => LengthUnit.Inch, "ft" => LengthUnit.Foot,
    _ => throw new ArgumentException($"unknown unit '{u}'")
};

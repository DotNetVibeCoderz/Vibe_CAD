using System.Diagnostics;
using Gravicode.CAD;
using Gravicode.CAD.Gallery;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Native;
using Gravicode.CAD.Rendering;

// Regenerates assets/icon.png, docs/images/*.png and gallery thumbnails.
// Usage: dotnet run --project tools/Gravicode.CAD.DocGen [-- <repoRoot>]
var root = args.Length > 0 ? args[0] : FindRoot();
NativeEngine.Enable();
var images = Directory.CreateDirectory(Path.Combine(root, "docs", "images"));
var thumbs = Directory.CreateDirectory(Path.Combine(images.FullName, "samples"));
var webRoot = Directory.CreateDirectory(Path.Combine(root, "gallery", "Gravicode.CAD.Gallery.Web", "wwwroot"));
var webThumbs = Directory.CreateDirectory(Path.Combine(webRoot.FullName, "thumbs"));
Console.WriteLine($"Gravicode.CAD DocGen → {root}");

// ---------- NuGet / app icon ----------
MakeIcon(Path.Combine(root, "assets", "icon.png"), 512);
MakeIcon(Path.Combine(root, "assets", "icon-128.png"), 128);
WriteIco(Path.Combine(root, "assets", "icon.ico"), [256, 64, 48, 32, 16]);
File.Copy(Path.Combine(root, "assets", "icon-128.png"), Path.Combine(webRoot.FullName, "icon.png"), true);

// ---------- sample thumbnails ----------
foreach (var s in SampleCatalog.All)
{
    var sw = Stopwatch.StartNew();
    var m = s.Build();
    var cam = new OrbitCamera { Yaw = -35, Pitch = 28 }.Fit(m.Bounds, 1.05);
    if (s.Id is "truss") { cam.Yaw = -8; cam.Pitch = 12; cam.Fit(m.Bounds, 0.62); }
    if (s.Id is "cantilever") { cam.Yaw = -18; cam.Pitch = 52; cam.Fit(m.Bounds, 0.66); }
    if (s.Id == "sketch-solver" || s.Id == "pin-grid") { cam.Pitch = 40; }
    SoftwareRenderer.RenderToPng(m, Path.Combine(thumbs.FullName, s.Id + ".png"), new RenderOptions { Width = 960, Height = 600, Camera = cam });
    File.Copy(Path.Combine(thumbs.FullName, s.Id + ".png"), Path.Combine(webThumbs.FullName, s.Id + ".png"), true);
    Console.WriteLine($"  {s.Id,-18} {m.TriangleCount,8:N0} tris  {sw.ElapsedMilliseconds,6} ms");
}

// ---------- hero image ----------
var hero = new CadModel("Hero");
void Add(string id, Vec3 offset)
{
    foreach (var b in SampleCatalog.Find(id)!.Build().Bodies)
        hero.AddBody(b.Name, b.Mesh.Translated(offset), b.Color).Metallic = b.Metallic;
}
Add("ai-housing", new Vec3(-20, 40, 0));
Add("gear-pair", new Vec3(150, -40, 0));
Add("flange-json", new Vec3(-150, -60, 0));
Add("csg-classic", new Vec3(150, 110, 20));
SoftwareRenderer.RenderToPng(hero, Path.Combine(images.FullName, "hero.png"),
    new RenderOptions { Width = 1600, Height = 800, Camera = new OrbitCamera { Yaw = -28, Pitch = 30, FieldOfView = 30 }.Fit(hero.Bounds, 0.64) });
Console.WriteLine("  hero.png");
Console.WriteLine("Done.");
return;

static void WriteIco(string path, int[] sizes)
{
    // ICO container with PNG-compressed entries (supported since Windows Vista)
    var pngs = sizes.Select(sz => IconImage(sz).EncodePng()).ToArray();
    using var w = new BinaryWriter(File.Create(path));
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
        w.Write(pngs[i].Length); w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) w.Write(p);
    Console.WriteLine($"  {Path.GetFileName(path)} ({string.Join("/", sizes)})");
}

static void MakeIcon(string path, int size)
{
    IconImage(size).SavePng(path);
    Console.WriteLine($"  {Path.GetFileName(path)} ({size}px)");
}

static RgbaImage IconImage(int size)
{
    // subject: the classic CSG part in Gravicode orange, rendered with a transparent background
    var core = Csg.Intersect(Shapes.Cube(40), Shapes.Sphere(27, 96, 48));
    var bore = Shapes.Cylinder(12, 60, 64, centered: true);
    core = core.Subtract(bore).Subtract(bore.Rotated(Vec3.UnitX, 90)).Subtract(bore.Rotated(Vec3.UnitY, 90));
    var m = new CadModel("icon", core, CadColor.FromHex("#FF8A2A"));
    m.Bodies[0].Metallic = 0.35; m.Bodies[0].Roughness = 0.3;
    int ss = 3;
    var subject = SoftwareRenderer.Render(m, new RenderOptions
    {
        Width = size, Height = size, Supersample = ss, TransparentBackground = true, Grid = false, GroundShadow = false,
        EdgeColor = CadColor.FromHex("#5A2200"), Camera = new OrbitCamera { Yaw = -40, Pitch = 30, FieldOfView = 28 }.Fit(m.Bounds, 0.86), Exposure = 1.15
    });
    var img = new RgbaImage(size, size);
    double r = size * 0.22; // corner radius
    var top = CadColor.FromHex("#1C2A44"); var bottom = CadColor.FromHex("#0A0F1A"); var glow = CadColor.FromHex("#2F6FDE");
    for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // rounded-square mask with 1px anti-aliasing
            double dx = Math.Max(Math.Max(r - x - 0.5, x + 0.5 - (size - r)), 0), dy = Math.Max(Math.Max(r - y - 0.5, y + 0.5 - (size - r)), 0);
            double dist = Math.Sqrt(dx * dx + dy * dy) - r;
            double mask = Math.Clamp(0.5 - dist, 0, 1);
            if (mask <= 0) { img.Set(x, y, new CadColor(0, 0, 0, 0)); continue; }
            var bg = CadColor.Lerp(top, bottom, (double)y / size);
            // soft blue glow behind the subject + faint blueprint grid
            double gx = (x - size * 0.5) / size, gy = (y - size * 0.52) / size;
            double g = Math.Exp(-(gx * gx + gy * gy) * 9) * 0.55;
            bg = CadColor.Lerp(bg, glow, g);
            double cell = size / 8.0;
            bool line = (x % cell) < Math.Max(1, size / 256.0) || (y % cell) < Math.Max(1, size / 256.0);
            if (line) bg = CadColor.Lerp(bg, CadColor.FromHex("#3B5A8F"), 0.18);
            var fg = subject.Get(x, y);
            var c = CadColor.Lerp(bg, fg with { A = 1 }, fg.A);
            img.Set(x, y, c with { A = mask });
        }
    return img;
}

static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
    return d?.FullName ?? Directory.GetCurrentDirectory();
}

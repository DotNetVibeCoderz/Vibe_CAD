using Gravicode.CAD;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

// Gravicode.CAD quick tour — Made by Gravicode Studios, led by Kang Fadhil.
var outDir = args.Length > 0 ? args[0] : "out";
Directory.CreateDirectory(outDir);
Console.WriteLine($"{GravicodeCad.ProductName} {GravicodeCad.Version} — {GravicodeCad.Credits}");
Console.WriteLine($"Native engine: {(NativeEngine.Enable() ? NativeEngine.Version : "managed fallback")}");

// 1) Parametric mounting plate
var plate = Part.Create("MountingPlate")
    .Param("w", 120).Param("d", 80).Param("t", 10).Param("hole", 8)
    .Box("w", "d", "t").Color("#9AA7B4")
    .Holes("hole", (-48, -28), (48, -28), (-48, 28), (48, 28))
    .Cylinder(36, 30).At(0, 0, "t").Color("#9AA7B4")
    .Holes(20, (0, 0))
    .Build();

// 2) Spur gear
var gear = new CadModel("SpurGear", Shapes.SpurGear(module: 2.5, teeth: 24, thickness: 14, boreDiameter: 16), CadColor.Brass);
gear.Bodies[0].Metallic = 0.9; gear.Bodies[0].Roughness = 0.3;

// 3) CSG classic: cube ∩ sphere − 3 cylinders
var cube = Shapes.Cube(40);
var sphere = Shapes.Sphere(27, 96, 48);
var core = Csg.Intersect(cube, sphere);
foreach (var axis in new[] { Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ })
{
    var cyl = Shapes.Cylinder(12, 60, 64, centered: true);
    if (axis == Vec3.UnitX) cyl = cyl.Rotated(Vec3.UnitY, 90);
    if (axis == Vec3.UnitY) cyl = cyl.Rotated(Vec3.UnitX, 90);
    core = Csg.Subtract(core, cyl);
}
var csg = new CadModel("CsgClassic", core, CadColor.Blueprint);

foreach (var m in new[] { plate, gear, csg })
{
    var p = m.ToMesh().Properties;
    Console.WriteLine($"{m.Name,-14} triangles={m.TriangleCount,7}  volume={p.Volume,12:F1}  area={p.Area,10:F1}");
    SoftwareRenderer.RenderToPng(m, Path.Combine(outDir, $"{m.Name}.png"), new RenderOptions { Width = 960, Height = 640 });
    m.Save(Path.Combine(outDir, $"{m.Name}.step"));
    m.Save(Path.Combine(outDir, $"{m.Name}.stl"));
    m.Save(Path.Combine(outDir, $"{m.Name}.glb"));
    m.Save(Path.Combine(outDir, $"{m.Name}.pdf"));
}

// Round-trip: load the STEP we just wrote and convert it
var model = StepImporter.Import(Path.Combine(outDir, "MountingPlate.step"));
model.ExportStl(Path.Combine(outDir, "MountingPlate_from_step.stl"));
Console.WriteLine($"Re-imported: {model}");
Console.WriteLine($"Outputs written to {Path.GetFullPath(outDir)}");

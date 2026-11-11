// Made with Gravicode.CAD — Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
using Gravicode.CAD;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

NativeEngine.Enable(); // Rust mesh kernels when available, C# otherwise

var model = Part.Create("Bracket")
    .Param("w", 120).Param("d", 80).Param("t", 10)
    .Box("w", "d", "t")
    .Holes(8.5, (-48, -28), (48, -28), (-48, 28), (48, 28))
    .Cylinder(36, 30).At(0, 0, "t")
    .Holes(20, (0, 0))
    .Build();

Directory.CreateDirectory("out");
model.Save("out/bracket.step");
model.ExportStl("out/bracket.stl");
model.Save("out/bracket.glb");
model.ExportPdf("out/bracket.pdf", "Bracket");
SoftwareRenderer.RenderToPng(model, "out/bracket.png");

var p = model.ToMesh().Properties;
Console.WriteLine($"{model.Name}: volume {p.Volume:N0} mm³, area {p.Area:N0} mm² → out/");

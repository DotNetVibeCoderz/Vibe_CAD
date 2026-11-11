# Getting started

> 🇮🇩 [Baca dalam Bahasa Indonesia](../id/GettingStarted.md)

Gravicode.CAD targets **.NET 10**. Install the packages you need — they are small and layered, so a web API that only converts files does not pull in the renderer or the AI client.

```bash
dotnet new console -n MyFirstPart && cd MyFirstPart
dotnet add package Gravicode.CAD.Parametric
dotnet add package Gravicode.CAD.IO
dotnet add package Gravicode.CAD.Rendering
dotnet add package Gravicode.CAD.Native      # optional: Rust kernels
```

## 1. Build a part

```csharp
using Gravicode.CAD;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

NativeEngine.Enable();                      // uses Rust when the native library loads

var model = Part.Create("Bracket")
    .Param("w", 120).Param("d", 80).Param("t", 10)
    .Box("w", "d", "t")                     // centred in XY, base on z = 0
    .Holes(8.5, (-48, -28), (48, -28), (-48, 28), (48, 28))
    .Cylinder(36, 30).At(0, 0, "t")         // boss on top
    .Holes(20, (0, 0))                      // through the boss and plate
    .Build();
```

Units are millimetres and **Z is up**. Every number can be an expression over parameters (`"w/2 - 10"`).

## 2. Export

```csharp
model.Save("bracket.step");                 // extension picks the format
model.ExportStl("bracket.stl");
model.Save("bracket.glb");
model.ExportPdf("bracket.pdf", "Bracket");  // A3 sheet: front, top, right, isometric
SoftwareRenderer.RenderToPng(model, "bracket.png");
```

## 3. Import

```csharp
var engine = CadModel.Load("engine.step");      // STEP, IGES, STL, OBJ, glTF, 3MF, DXF, SVG
Console.WriteLine(engine);                      // bodies, triangles, drawing entities
foreach (var w in engine.Warnings) Console.WriteLine(w);
engine.ConvertUnits(LengthUnit.Inch).Save("engine.3mf");
```

## 4. Inspect

```csharp
var mesh = model.ToMesh();
var p = mesh.Properties;                        // volume, area, centroid
var report = new Gravicode.CAD.Topology.HalfEdgeMesh(mesh).Validate();
Console.WriteLine(report);                      // watertight, genus, non-manifold edges…
```

## 5. Change a parameter

```csharp
var wider = Part.Create("Bracket")./* … same features … */Build(new Dictionary<string, double> { ["w"] = 160 });
```

or serialise the definition and rebuild it later (see [Geometry → Parametric](Geometry.md#parametric-feature-tree)).

## Next steps

- Browse the [Gallery](../../gallery) — 18 runnable samples with code.
- [CLI](CLI.md): convert and render without writing code.
- [Templates](Templates.md): WPF, WinForms, Blazor, Web API, Avalonia and MAUI starters.
- [AI](AI.md): generate parts from text.

---
Made by Gravicode Studios, led by Kang Fadhil.

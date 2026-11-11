# Memulai

> 🇬🇧 [Read in English](../en/GettingStarted.md)

Gravicode.CAD menargetkan **.NET 10**. Pasang paket yang dibutuhkan saja — paketnya kecil dan berlapis, sehingga web API yang hanya mengonversi file tidak ikut membawa renderer atau klien AI.

```bash
dotnet new console -n PartPertama && cd PartPertama
dotnet add package Gravicode.CAD.Parametric
dotnet add package Gravicode.CAD.IO
dotnet add package Gravicode.CAD.Rendering
dotnet add package Gravicode.CAD.Native      # opsional: kernel Rust
```

## 1. Membuat part

```csharp
using Gravicode.CAD;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

NativeEngine.Enable();                      // memakai Rust bila library native termuat

var model = Part.Create("Braket")
    .Param("w", 120).Param("d", 80).Param("t", 10)
    .Box("w", "d", "t")                     // di tengah XY, alas di z = 0
    .Holes(8.5, (-48, -28), (48, -28), (-48, 28), (48, 28))
    .Cylinder(36, 30).At(0, 0, "t")         // boss di atas
    .Holes(20, (0, 0))                      // tembus boss dan pelat
    .Build();
```

Satuan dalam milimeter dan **sumbu Z mengarah ke atas**. Setiap angka bisa berupa ekspresi parameter (`"w/2 - 10"`).

## 2. Ekspor

```csharp
model.Save("braket.step");                  // ekstensi menentukan format
model.ExportStl("braket.stl");
model.Save("braket.glb");
model.ExportPdf("braket.pdf", "Braket");    // lembar A3: tampak depan, atas, kanan, isometrik
SoftwareRenderer.RenderToPng(model, "braket.png");
```

## 3. Impor

```csharp
var mesin = CadModel.Load("engine.step");       // STEP, IGES, STL, OBJ, glTF, 3MF, DXF, SVG
Console.WriteLine(mesin);                       // jumlah body, segitiga, entitas gambar
foreach (var w in mesin.Warnings) Console.WriteLine(w);
mesin.ConvertUnits(LengthUnit.Inch).Save("engine.3mf");
```

## 4. Memeriksa

```csharp
var mesh = model.ToMesh();
var p = mesh.Properties;                        // volume, luas, centroid
var laporan = new Gravicode.CAD.Topology.HalfEdgeMesh(mesh).Validate();
Console.WriteLine(laporan);                     // watertight, genus, edge non-manifold…
```

## 5. Mengubah parameter

```csharp
var lebihLebar = Part.Create("Braket")./* … fitur sama … */Build(new Dictionary<string, double> { ["w"] = 160 });
```

atau simpan definisinya ke JSON lalu bangun ulang kapan saja (lihat [Geometri → Parametrik](Geometry.md#feature-tree-parametrik)).

## Langkah berikutnya

- Jelajahi [Galeri](../../gallery) — 18 contoh lengkap dengan kode.
- [CLI](CLI.md): konversi dan render tanpa menulis kode.
- [Template](Templates.md): starter WPF, WinForms, Blazor, Web API, Avalonia dan MAUI.
- [AI](AI.md): membuat part dari teks.

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.

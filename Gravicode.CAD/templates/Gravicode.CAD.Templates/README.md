# Gravicode.CAD.Templates

```bash
dotnet new install Gravicode.CAD.Templates
```

| Short name | What you get |
|---|---|
| `cad-console` | Parametric part → STEP / STL / glTF / PDF drawing + PNG preview |
| `cad-webapi` | Minimal API: convert uploads between CAD formats, parametric parts, text-to-CAD |
| `cad-blazor` | Blazor Web App with the three.js `<CadViewer>` and live parameter sliders |
| `cad-wpf` | WPF window with `CadViewport` (orbit/pan/zoom) and file open |
| `cad-winforms` | WinForms form with `CadViewport.Load(...)` |
| `cad-avalonia` | Avalonia desktop app rendering through ThreeNet (DirectX 12 / Metal / Vulkan) |
| `cad-maui` | .NET MAUI app with a parametric part preview |

```bash
dotnet new cad-console -n MyPart
cd MyPart && dotnet run
```

Made by Gravicode Studios, led by Kang Fadhil · Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil

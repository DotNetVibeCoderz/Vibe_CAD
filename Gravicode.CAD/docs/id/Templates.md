# Template proyek

> 🇬🇧 [Read in English](../en/Templates.md)

```bash
dotnet new install Gravicode.CAD.Templates
```

| Template | Perintah | Isi |
|---|---|---|
| Console | `dotnet new cad-console -n PartSaya` | braket parametrik → STEP, STL, GLB, gambar PDF, PNG |
| Web API | `dotnet new cad-webapi -n CadApi` | `GET /parts/plate.step?w=150`, `POST /convert/{ext}`, `POST /ai/generate`, `GET /formats` |
| Blazor | `dotnet new cad-blazor -n CadWeb` | `<CadViewer>` dengan slider langsung untuk flens |
| WPF | `dotnet new cad-wpf -n CadWpf` | `CadViewport` + dialog buka (`Model = CadModel.Load(...)`) |
| WinForms | `dotnet new cad-winforms -n CadForms` | `viewport.Load("engine.step")` |
| Avalonia | `dotnet new cad-avalonia -n CadDesk` | viewer GPU ThreeNet, lintas platform |
| MAUI | `dotnet new cad-maui -n CadMobile` | pratinjau roda gigi dengan slider jumlah gigi (Android, Windows) |

Setiap template mereferensikan paket NuGet Gravicode.CAD versi yang sama dan bisa dijalankan dengan `dotnet run`. Template `cad-maui` membutuhkan workload MAUI (`dotnet workload install maui`).

Copot: `dotnet new uninstall Gravicode.CAD.Templates`.

Sumber template ada di `templates/Gravicode.CAD.Templates/content` dan dibuat oleh `gen_templates.py`.

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.

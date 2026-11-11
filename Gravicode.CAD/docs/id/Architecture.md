# Arsitektur

> 🇬🇧 [Read in English](../en/Architecture.md)

```text
Aplikasi ── WPF · WinForms · Blazor · ASP.NET Core · Console · MAUI · Avalonia
      │
SDK Gravicode.CAD ── Parametric · Constraint · FEM · CAM · AI · IO · Rendering · ThreeJs · ThreeNet
      │
API CAD tingkat tinggi ── Geometry (kurva, profil, sweep, CSG) · Topology (half-edge, B-Rep-lite)
      │
Mesin native Rust ── gravicode-cad-engine (C ABI) di balik IMeshKernel
      │
Kernel CAD ── Core (matematika presisi ganda, Mesh, CadModel, registri format)
```

## Graf dependensi proyek

```text
Core ◄── Geometry ◄── Topology ◄── IO ◄── AI
  ▲          ▲            ▲         ▲      ▲
  │          ├─ Parametric ─────────┼──────┘
  │          ├─ Constraint          │
  │          ├─ FEM                 ├── ThreeJs (Blazor RCL)
  │          └─ CAM                 ├── Viewer.Wpf / Viewer.WinForms
  ├─ Native (P/Invoke → Rust)       │
  └──────────── Rendering ◄─────────┴── ThreeNet
CLI = AI + CAM + Rendering + Native
```

## Gagasan inti

**Satu "mata uang": `Mesh`.** Semua modul bertukar mesh segitiga presisi ganda yang tertutup dan berorientasi keluar. Boolean, slicer, visualisasi FEM, semua importer dan renderer memakai tipe yang sama, sehingga fitur bisa digabung tanpa konverter. `ShadedMesh` adalah varian float32 dengan normal sadar-lipatan untuk GPU.

**`CadModel` adalah dokumen.** Berisi `CadBody` bernama (mesh + warna + parameter PBR), `DrawingEntity` 2D (garis, busur, lingkaran, polyline, teks) dan kawat 3D `Polyline3`, ditambah satuan dan metadata. `CadModel.Load/Save` memilih format berdasarkan ekstensi lewat `CadFormats`; `Gravicode.CAD.IO` mendaftarkan dirinya otomatis melalui module initializer (dan secara lazy bila hanya Core yang direferensikan).

**Kernel bisa ditukar.** Loop berat — mass properties, normal, welding, slicing Z — melewati `MeshKernel.Current` (`IMeshKernel`). Implementasi C# selalu tersedia; `NativeEngine.Enable()` menukarnya dengan mesin Rust yang membaca memori `List<Vec3>` langsung (tanpa salin; `Vec3` adalah tiga double berurutan). Keduanya diuji setara.

**Parametrik = data.** `PartDefinition` adalah parameter + daftar fitur berurutan, sepenuhnya bisa diserialisasi ke JSON. Rebuild mengevaluasi ekspresi, membangun solid tiap fitur di frame lokalnya, menempatkannya, lalu menggabungkannya ke body target (add / subtract / intersect). JSON yang sama inilah yang diminta agen AI dari model bahasa, sehingga keluaran AI divalidasi oleh kernel sungguhan.

**Boolean berbasis BSP** (seperti csg.js) dalam presisi ganda, diikuti perbaikan T-junction agar hasil kedap dan kueri topologi (genus, feature edge, muka STEP) tetap bermakna.

**Eksak di tempat penting, ditessellasi bila praktis.** Kurva (NURBS, busur, heliks) eksak sampai ditessellasi dengan toleransi chord. Impor STEP mentessellasi muka bidang, silinder dan kerucut dari loop edge aslinya; permukaan lain memakai fan loop dengan peringatan. Kernel B-Rep penuh (fillet, offset, boolean permukaan NURBS ala OpenCascade) ada di [roadmap](../../PLAN.md).

## Jalur rendering

| Jalur | Di mana | Cara |
|---|---|---|
| `SoftwareRenderer` | di mana saja (server, CI, MAUI) | rasterizer CPU deferred → `RgbaImage` → PNG |
| `Gravicode.CAD.ThreeJs` | Blazor / browser | `<CadViewer>` → payload biner → three.js |
| `Gravicode.CAD.ThreeNet` | GPU desktop | `CadScene` → ThreeNet (wgpu: DX12/Metal/Vulkan) |
| `Viewer.Wpf/WinForms` | desktop Windows | `InteractiveView` + software renderer |

## Thread & performa

- Pohon CSG dibangun secara iteratif (tanpa batas kedalaman rekursi) dengan union berpasangan seimbang.
- Software renderer merasterisasi pita horizontal secara paralel dan memakai supersampling.
- Mass properties di Rust membagi mesh besar ke beberapa thread.
- Loop berat memakai span (`CollectionsMarshal.AsSpan`) dan `stackalloc`, bukan alokasi.

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.

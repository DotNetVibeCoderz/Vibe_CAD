# Gravicode CAD Platform

> Enterprise-grade CAD, Geometry, Parametric Modeling, Rendering, Simulation, Interoperability, and AI-CAD Framework for .NET

## Vision

Membangun library CAD paling lengkap di ekosistem .NET dengan kombinasi kekuatan OpenCascade, CadQuery, Build123D, Maker.js, CGAL, Three.js, dan ThreeNet.

## Arsitektur

```text
Applications
├── WPF
├── WinForms
├── Blazor
├── ASP.NET Core
├── Console
├── MAUI
└── Avalonia

        ↓

Gravicode.CAD SDK
        ↓
High-Level CAD API
        ↓
Rust Native Engine
        ↓
CAD Kernel
```

## Modul Utama

- Gravicode.CAD.Core
- Gravicode.CAD.Geometry
- Gravicode.CAD.Topology
- Gravicode.CAD.Parametric
- Gravicode.CAD.Constraint
- Gravicode.CAD.Rendering
- Gravicode.CAD.ThreeNet
- Gravicode.CAD.ThreeJs
- Gravicode.CAD.FEM
- Gravicode.CAD.CAM
- Gravicode.CAD.AI
- Gravicode.CAD.CLI

## Dukungan Format

### Import
- STEP/STP
- IGES/IGS
- DXF
- DWG
- STL
- OBJ
- GLTF/GLB
- SVG
- 3MF

### Export
- STEP
- IGES
- STL
- OBJ
- DXF
- GLTF
- SVG
- PDF

## Aplikasi Contoh

### WPF

```csharp
Model = CadModel.Load("engine.step");
```

### WinForms

```csharp
viewport.Load("engine.step");
```

### Blazor

```razor
<CadViewer Source="gear.step" />
```

### Console

```csharp
var model = StepImporter.Import("part.step");
model.ExportStl("part.stl");
```

## CLI Tool

```bash
dotnet tool install -g gravicode-cad
```

```bash
gcad convert model.step model.stl
```

## Template Project

```bash
dotnet new install Gravicode.CAD.Templates
```

Templates:

- cad-console
- cad-wpf
- cad-winforms
- cad-blazor
- cad-webapi
- cad-maui

## Rendering

### Web
- Three.js
- Blazor JSInterop

### Desktop
- ThreeNet
- WPF
- WinForms

## AI CAD

Prompt:

```text
Create a gearbox housing with four mounting holes
```

Output:
- CAD Model
- STEP
- STL
- Drawing

## Dokumentasi

- GettingStarted.md
- Architecture.md
- Geometry.md
- Rendering.md
- ThreeNet.md
- ThreeJs.md
- AI.md
- CLI.md
- Templates.md

## Screenshot yang Direkomendasikan

- docs/images/home.png
- docs/images/wpf-editor.png
- docs/images/blazor-viewer.png
- docs/images/cli-demo.png
- docs/images/ai-agent.png

## Positioning

GemBox untuk CAD + OpenCascade untuk .NET + Three.js untuk Web + ThreeNet untuk Desktop + AI CAD Platform.

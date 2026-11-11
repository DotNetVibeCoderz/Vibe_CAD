# Architecture

> 🇮🇩 [Baca dalam Bahasa Indonesia](../id/Architecture.md)

```text
Applications ── WPF · WinForms · Blazor · ASP.NET Core · Console · MAUI · Avalonia
      │
Gravicode.CAD SDK ── Parametric · Constraint · FEM · CAM · AI · IO · Rendering · ThreeJs · ThreeNet
      │
High-level CAD API ── Geometry (curves, profiles, sweeps, CSG) · Topology (half-edge, B-Rep-lite)
      │
Rust native engine ── gravicode-cad-engine (C ABI) behind IMeshKernel
      │
CAD kernel ── Core (double-precision math, Mesh, CadModel, format registry)
```

## Project dependency graph

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

## Core ideas

**One currency: `Mesh`.** Every module exchanges closed, outward-oriented, double-precision triangle meshes. Booleans, the slicer, FEM visualisation, every importer and every renderer speak the same type, so features compose without converters. `ShadedMesh` is the float32, crease-aware variant handed to GPUs.

**`CadModel` is the document.** It holds named `CadBody`s (mesh + colour + PBR parameters), 2D `DrawingEntity`s (lines, arcs, circles, polylines, text) and 3D `Polyline3` wires, plus units and metadata. `CadModel.Load/Save` dispatch by file extension through `CadFormats`; `Gravicode.CAD.IO` registers itself automatically through a module initializer (and lazily if only Core is referenced).

**The kernel is swappable.** Hot loops — mass properties, normals, welding, Z-slicing — go through `MeshKernel.Current` (`IMeshKernel`). The managed implementation is always present; `NativeEngine.Enable()` swaps in the Rust engine, which reads `List<Vec3>` memory directly (zero copy; `Vec3` is three contiguous doubles). Both are parity-tested.

**Parametric = data.** A `PartDefinition` is parameters + an ordered feature list, fully JSON-serialisable. Rebuilding evaluates expressions, builds each feature's solid in its local frame, places it, and combines it with its target body by add / subtract / intersect. The same JSON is what the AI agent asks a language model to write, so AI output is validated by the real kernel before anyone sees it.

**Booleans are BSP-based** (in the spirit of csg.js) in double precision, followed by T-junction repair so results are watertight and topology queries (genus, feature edges, STEP faces) stay meaningful.

**Exact where it matters, tessellated where it's practical.** Curves (NURBS, arcs, helices) are exact until tessellation, controlled by a chordal tolerance. STEP import tessellates planar, cylindrical and conical faces from their real edge loops; other surfaces fall back to loop fans with a warning. A full B-Rep kernel (OpenCascade-class fillets, offsets, NURBS surface booleans) is on the [roadmap](../../PLAN.md).

## Rendering paths

| Path | Where | How |
|---|---|---|
| `SoftwareRenderer` | anywhere (servers, CI, MAUI) | deferred CPU rasteriser → `RgbaImage` → PNG |
| `Gravicode.CAD.ThreeJs` | Blazor / browsers | `<CadViewer>` → binary payload → three.js |
| `Gravicode.CAD.ThreeNet` | desktop GPU | `CadScene` → ThreeNet (wgpu: DX12/Metal/Vulkan) |
| `Viewer.Wpf/WinForms` | Windows desktop | `InteractiveView` + software renderer |

## Threading & performance

- CSG trees are built iteratively (no recursion depth limits) with balanced pairwise unions.
- The software renderer rasterises horizontal bands in parallel and supersamples.
- Rust mass properties split large meshes across threads.
- Hot loops use spans (`CollectionsMarshal.AsSpan`) and `stackalloc` instead of allocations.

---
Made by Gravicode Studios, led by Kang Fadhil.

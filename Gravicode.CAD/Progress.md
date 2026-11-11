# Progress — Gravicode.CAD

_Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil_

Legend: ✅ done · 🚧 in progress · ⏳ planned (see [PLAN.md](PLAN.md))

## Libraries (`src/`)
| Module | Status | Notes |
|---|---|---|
| Gravicode.CAD.Core | ✅ | Vec2/Vec3/Mat4 (double), BoundingBox, Plane, units, colours, Mesh, ShadedMesh, IMeshKernel, CadModel, CadFormats |
| Gravicode.CAD.Geometry | ✅ | Curves (line/arc/Bézier/NURBS/helix), Polygon2 + offset, Profile2, ear clipping with holes, primitives, extrude/twist/revolve/loft/sweep, involute gears, BSP CSG + T-junction repair |
| Gravicode.CAD.Topology | ✅ | Half-edge mesh, watertight/manifold/orientation, Euler/genus, shells, boundary loops, feature edges (sliver-safe), B-Rep-lite |
| Gravicode.CAD.Parametric | ✅ | Expressions, ParameterSet, 14 feature types, JSON feature tree, `Part` builder, body-aware through holes |
| Gravicode.CAD.Constraint | ✅ | 16 constraints, Levenberg–Marquardt, DOF/rank, sketch → profiles/DXF |
| Gravicode.CAD.IO | ✅ | STEP r/w, IGES r/w (wireframe), STL, OBJ+MTL, glTF/GLB, 3MF, DXF, SVG, PDF drawings with HLR; DWG → guidance |
| Gravicode.CAD.Native + Rust engine | ✅ | mass properties (multi-threaded), normals, weld, Z-slice; parity tests; CI builds win-x64, linux-x64, osx-arm64 |
| Gravicode.CAD.Rendering | ✅ | Software renderer → PNG, OrbitCamera, InteractiveView |
| Gravicode.CAD.ThreeJs | ✅ | Blazor `<CadViewer>` (three.js 0.170, studio environment, toolbar, snapshots) |
| Gravicode.CAD.ThreeNet | ✅ | `CadScene` for ThreeNet 0.12 (light/dark, edges, grid) |
| Gravicode.CAD.Viewer.Wpf / WinForms | ✅ | `CadViewport` controls |
| Gravicode.CAD.FEM | ✅ | Truss 2D/3D, plane-stress CST + von Mises, heat conduction, Delaunay mesher, CSR + PCG |
| Gravicode.CAD.CAM | ✅ | Contour, pocket with islands, peck drill, FDM slicer, G-code (GRBL/Marlin/LinuxCNC) |
| Gravicode.CAD.AI | ✅ | Text-to-CAD with repair loop; Claude (Anthropic SDK, `claude-opus-5-5`), OpenAI-compatible, offline EN/ID — live-tested with Claude |
| Gravicode.CAD.CLI (`gcad`) | ✅ | convert, info, validate, render, drawing, build, shape, slice, generate, formats |

## Apps, packaging, docs
| Item | Status |
|---|---|
| Tests | ✅ 75 passing + 1 opt-in live LLM test |
| Console sample, DocGen tool | ✅ |
| Templates (console, webapi, blazor, wpf, winforms, avalonia, maui) | ✅ |
| Gallery — Blazor (18 samples, three.js) | ✅ |
| Gallery — Avalonia (ThreeNet GPU, software fallback) | ✅ |
| Docs EN/ID (9 topics), README EN/ID, screenshots, NuGet icon | ✅ |
| CI (GitHub Actions): native matrix → build/test → pack → NuGet on tag | ✅ |

## Log
- 2026-11-11 — All modules implemented; Rust engine wired; live Claude text-to-CAD verified; galleries screenshotted; moved into the Vibe_CAD monorepo with CI.

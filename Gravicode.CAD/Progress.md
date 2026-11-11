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
| Gravicode.CAD.ThreeNet | ✅ | `CadScene` for ThreeNet 0.12.1 (light/dark, edges, grid, clip planes fitted to the model) |
| Gravicode.CAD.Viewer.Wpf / WinForms | ✅ | `CadViewport` controls |
| Gravicode.CAD.FEM | ✅ | Truss 2D/3D, plane-stress CST + von Mises, heat conduction, Delaunay mesher, CSR + PCG |
| Gravicode.CAD.CAM | ✅ | Contour, pocket with islands, peck drill, FDM slicer, G-code (GRBL/Marlin/LinuxCNC) |
| Gravicode.CAD.AI | ✅ | Text-to-CAD with repair loop; Claude (Anthropic SDK, `claude-opus-5-5`), OpenAI-compatible, offline EN/ID — live-tested with Claude |
| Gravicode.CAD.CLI (`gcad`) | ✅ | convert, info, validate, render, drawing, build, shape, slice, generate, formats |

## Apps, packaging, docs
| Item | Status |
|---|---|
| Tests | ✅ 98 passing (75 library + 23 studio) + 1 opt-in live LLM test |
| Console sample, DocGen tool | ✅ |
| Templates (console, webapi, blazor, wpf, winforms, avalonia, maui) | ✅ |
| Gallery — Blazor (18 samples, three.js) | ✅ |
| Gallery — Avalonia (ThreeNet GPU, software fallback) | ✅ |
| Docs EN/ID (10 topics), README EN/ID, screenshots, NuGet icon | ✅ |
| CI (GitHub Actions): native matrix → build/test → pack → NuGet on tag | ✅ |
| **Gravicode CAD Studio** — Studio.Core (document + undo, command engine, ~50 commands, `.gcproj`, catalog) | ✅ 10 tests |
| Studio Web (Blazor Server, blueprint canvas, three.js 3D) | ✅ |
| Studio Desktop (Avalonia, `BlueprintCanvas`, ThreeNet GPU 3D + software fallback) | ✅ |
| Studio catalog: 8 templates + 18 samples across 17 industries (Blender-made game props) | ✅ |
| Studio release workflow: Windows setup + zip, Linux .deb + tar.gz, macOS .dmg + .app.zip, web zips | ✅ |
| Studio drafting: TRIM / EXTEND / FILLET / CHAMFER (lines, arcs, circles, polylines; polyline mode), grips | ✅ |
| Studio HATCH (pick point with islands, ANSI31 / ANSI37 / SOLID, auto scale), BLOCK / INSERT / EXPLODE | ✅ |
| Studio PLOT: A4–A0, Fit or 1:N, window, title block, PDF / SVG; exports explode dims/hatches/blocks (DXF keeps them) | ✅ |
| Studio 3D picking & gizmos, sketch-on-face, parametric history, paper-space layouts | ⏳ |

## Log
- 2026-11-12 — Studio drafting stage: TRIM/EXTEND/FILLET/CHAMFER, grips (corner-only on tessellated polylines), HATCH with islands and auto scale, blocks, PLOT to scaled PDF/SVG with title block. Fixed: `;` as Enter in the interactive command line, DXF export dropped dimensions, exact keyword matches now beat prefixes.
- 2026-11-11 — **1.0.1**: NuGet packages and `dotnet new` templates move to ThreeNet 0.12.1 and ship the `CadScene` clip-plane/background fixes and the software renderer's edge depth-bias fix. Studio 1.0.0 released with installers for Windows, Linux and macOS.
- 2026-11-11 — Gravicode CAD Studio (web + desktop) added with docs, screenshots and the `gravicode-cad-studio-v*` release workflow. Fixed in ThreeNet 0.12.1 while embedding it: stale GPU buffers on recycled ids, FXC → DXC on Windows (first frame 16.6 s → 1 s), UI-friendly render loop. `CadScene` now fits near/far planes to the model and uses linear background colours.
- 2026-11-11 — All modules implemented; Rust engine wired; live Claude text-to-CAD verified; galleries screenshotted; moved into the Vibe_CAD monorepo with CI.

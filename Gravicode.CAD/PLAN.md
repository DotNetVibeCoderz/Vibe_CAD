# Gravicode.CAD — Roadmap / Rencana Pengembangan

_Made by Gravicode Studios, led by Kang Fadhil · Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil_

Status of each item is tracked in [Progress.md](Progress.md).

## v1.0 — Foundation (current) ✅

- Double-precision core, meshes, `CadModel`, format registry
- Geometry kernel: curves, profiles, primitives, extrude/revolve/loft/sweep, BSP booleans with T-junction repair
- Topology: half-edge analysis, B-Rep-lite faces
- Parametric feature tree (JSON) + fluent builder; 2D constraint solver
- Interop: STEP (faceted write, advanced B-Rep read for planes/cylinders/cones), IGES (wireframe), STL, OBJ, glTF/GLB, 3MF, DXF, SVG, PDF drawings
- Rust native engine for hot mesh kernels
- Rendering: software renderer, three.js Blazor viewer, ThreeNet desktop adapter, WPF & WinForms viewports
- FEM (truss, plane stress, heat), CAM (contour, pocket, drill, FDM slicer, G-code)
- AI-CAD: text-to-part with repair loop (Claude / OpenAI-compatible / offline)
- `gcad` CLI, `dotnet new` templates, Blazor + Avalonia galleries, bilingual docs, CI → NuGet
- Gravicode CAD Studio (web + desktop CAD designer) with installers on GitHub releases

### Studio next
- [x] Grips, TRIM/EXTEND/FILLET/CHAMFER, hatch, blocks, PLOT (scaled PDF/SVG with title block)
- [ ] Paper-space layouts with multiple viewports; polyline arc segments (bulges) instead of tessellated arcs
- [x] Picking and gizmos in the 3D pane (move / rotate, both editions)
- [ ] Sketch-on-face; parametric history for solids; scale gizmo and plane handles
- [ ] Signed/notarised installers; auto-update

## v1.1 — Exact solids

- [ ] B-Rep data model with exact surfaces (plane, cylinder, cone, sphere, torus, NURBS) alongside meshes
- [ ] STEP export of exact faces (`ADVANCED_FACE`) for primitives and extrusions
- [ ] STEP import of spherical/toroidal/B-spline surfaces via UV-space tessellation
- [ ] Assembly structure: STEP `NEXT_ASSEMBLY_USAGE_OCCURRENCE` placements, glTF/3MF component trees
- [ ] Fillets & chamfers on straight edges (rolling-ball on planar faces)
- [ ] Shell (hollow) feature and draft angles

## v1.2 — Interop & drawing

- [ ] DWG read via an open implementation (LibreDWG-compatible reader in Rust)
- [ ] IGES 128/144 trimmed NURBS surfaces
- [ ] Associative dimensions (linear, radial, angular) and section views in drawings
- [ ] DXF R2018 export with LWPOLYLINE, layers and dimension entities
- [ ] Units & tolerances in drawings (ISO 2768 title block field)

## v1.3 — Simulation & manufacturing

- [ ] 3D tetrahedral mesher and linear elasticity (TET4/TET10)
- [ ] Modal analysis (natural frequencies) and buckling for trusses
- [ ] Adaptive clearing and rest machining; arc fitting (G2/G3) in G-code
- [ ] Slicer: supports, adaptive layers, gyroid infill
- [ ] Rust implementations of CSG and slicing for large models (parallel BSP)

## v1.4 — AI-CAD

- [ ] Image/sketch-to-CAD (multimodal prompts) and drawing → part
- [ ] Iterative design agent with tool calls (measure, section, FEM check) and design-intent memory
- [ ] Integration with GravicodeScience / HF.Net for local models and design-space optimisation (RLNet)
- [ ] Blender & Rodin MCP bridges for concept-to-CAD asset pipelines

## Platform

- [ ] Blazor WebAssembly build of the kernel (trimming-safe IO registry)
- [ ] Native engine packages for linux-arm64 and osx-x64
- [ ] Benchmarks suite and performance dashboards in CI

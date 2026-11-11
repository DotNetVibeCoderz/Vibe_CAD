# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Gravicode.CAD lives in the `Gravicode.CAD/` folder of the Vibe_CAD monorepo. All paths below are relative to that folder. The product brief is `solution-design.md`; roadmap in `PLAN.md`, status in `Progress.md` (keep both updated).

## Commands

```bash
dotnet build Gravicode.CAD.slnx                       # everything (Windows needed for Viewer.Wpf/WinForms + gallery desktop)
dotnet test tests/Gravicode.CAD.Tests                  # 75 tests
dotnet test tests/Gravicode.CAD.Tests --filter "FullyQualifiedName~IoTests.Step_reads_advanced_brep_with_cylinder"
./build-native.ps1                                     # cargo build + copy into src/Gravicode.CAD.Native/runtimes/<rid>/native
cd native/gravicode-cad-engine && cargo test --release
./pack.ps1                                             # all NuGet packages → artifacts/packages
dotnet run -c Release --project tools/Gravicode.CAD.DocGen   # regenerates assets/icon*, docs/images, gallery thumbs
dotnet run --project gallery/Gravicode.CAD.Gallery.Web       # needs ASPNETCORE_ENVIRONMENT=Development when run without launch profile
dotnet run --project gallery/Gravicode.CAD.Gallery.Desktop
dotnet test tests/Gravicode.CAD.Studio.Tests                 # studio command engine / catalog
dotnet run --project studio/Gravicode.CAD.Studio.Web         # http://localhost:5190 (launch profile sets Development)
dotnet run --project studio/Gravicode.CAD.Studio.Desktop     # GCAD_STUDIO_DEMO="gearbox|BOX 0,0 50,50 20" scripts it (screenshots)
dotnet run -c Release --project tools/Gravicode.CAD.DocGen -- . --studio   # studio thumbnails only
python templates/Gravicode.CAD.Templates/gen_templates.py    # regenerates template content (run from that folder)
```

Live LLM test is opt-in: set `GRAVICODE_LIVE_LLM=claude|azure|deepseek|hf` plus `ANTHROPIC_API_KEY` (or `GRAVICODE_KEY_FILE`). Never commit keys.

CI: `.github/workflows/gravicode-cad.yml` at the monorepo root. Studio installers: `.github/workflows/gravicode-cad-studio-release.yml` on tag `gravicode-cad-studio-vX.Y.Z` (attaches Windows/Linux/macOS installers to a GitHub Release). Pushing a tag `gravicode-cad-vX.Y.Z` builds native libs (win/linux/mac), tests, packs with that version and pushes to nuget.org using the `NUGET_API_KEY` secret.

## Architecture (big picture)

- **`Mesh` is the shared currency** (double precision, closed, outward CCW). Everything — CSG, slicer, FEM visualisation, importers, renderers — exchanges `Mesh`; `ShadedMesh` is the float32 crease-split version for GPUs.
- **`CadModel`** = bodies (mesh + colour/PBR) + 2D `DrawingEntity`s + 3D `Polyline3` wires + units/metadata. `Load/Save` dispatch by extension via `CadFormats`; `Gravicode.CAD.IO` self-registers with a module initializer (and Core reflects-loads it lazily).
- **Hot kernels are swappable**: `MeshKernel.Current` (`IMeshKernel`) is managed by default; `NativeEngine.Enable()` swaps in Rust (`native/gravicode-cad-engine`, C ABI, zero-copy over `List<Vec3>`). Keep both implementations in sync — `NativeTests` checks parity. Slice intersection points are computed with canonical edge ordering in both so chains close.
- **CSG** (`Geometry/Csg.cs`) is iterative BSP; every result passes `MeshRepair.FixTJunctions`, which topology/STEP/feature edges depend on. Keep boolean tools reasonably sized (very long tools lose precision — `HoleFeature` sizes through-holes from the body bounds for this reason).
- **Parametric** (`PartDefinition`) is JSON with polymorphic `type` discriminator (out-of-order allowed). Features build in local frame → `Placement` (Rz·Ry·Rx then translate) → add/subtract/intersect into a named body. `PatternFeature` takes its operation from its source. This same JSON is the AI agent's output contract (`AI/CadPrompts.cs` documents it — update the prompt when adding feature types).
- **Z-up everywhere in CAD space**; glTF and ThreeNet are Y-up (converted at the boundary: glTF root rotation, `CadScene` root node).
- **Triangulator** vertex order is outer then holes in profile order (STEP import relies on this mapping).
- Feature edges ignore zero-area slivers (`HalfEdgeMesh.FaceNormals` returns `Vec3.Zero` for them).

- **Studio** (`studio/`): `Studio.Core` holds everything UI-independent — `StudioDocument` (mutate only through `Edit(label, action)` so undo works), `CommandEngine` (commands are iterators yielding `Prompt`s; scripts use `;` for Enter and `L`/`P`/`ALL` selection tokens), `StudioCatalog` (templates/samples), `StudioRibbon`, `DisplayList`. Web and Desktop are thin shells over it and draw the same display list (`studioCanvas.js` / `BlueprintCanvas.cs` mirror each other). ThreeNet scene edits in the desktop must be applied from the `ThreeNetView.Frame` callback.

## Conventions

- Central package versions in `Directory.Packages.props`; projects under `src/` are packable and get icon/README from `Directory.Build.props`. `templates/` has its own props to stop inheritance.
- Gallery samples live in `gallery/Gravicode.CAD.Gallery.Samples/SampleCatalog.cs`; each sample's `Code` string must mirror its `Build` lambda.
- Docs are bilingual: every page in `docs/en` has a twin in `docs/id`. Product credit line: "Made by Gravicode Studios, led by Kang Fadhil" / "Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil" (constants in `GravicodeCad`).
- Claude calls use the official `Anthropic` NuGet SDK (default `claude-opus-5-5`, adaptive thinking, refusal fallbacks); unscoped keys need `anthropic-workspace-id` (supported via `ClaudeCadModel(workspaceId:)`).

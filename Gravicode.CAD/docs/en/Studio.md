# Gravicode CAD Studio

A command-line driven CAD designer built on Gravicode.CAD, in two editions that share one engine:

| Edition | Stack | Run |
|---|---|---|
| **Web** | Blazor Server + `<CadViewer>` (three.js) | `dotnet run --project studio/Gravicode.CAD.Studio.Web` → http://localhost:5190 |
| **Desktop** | Avalonia 12 + ThreeNet (GPU) | `dotnet run --project studio/Gravicode.CAD.Studio.Desktop` |

Installers for Windows, Linux and macOS are attached to every
[GitHub release](https://github.com/DotNetVibeCoderz/Vibe_CAD/releases) tagged `gravicode-cad-studio-v*`.

![Web edition](../images/studio-web-editor.png)

## The screen

```text
┌ Ribbon: Home · Solid · Inspect · View · AI · Output ───────────────────────────┐
│ Layers / solids  │  2D model space (blueprint)  │  3D view  │  Properties      │
│                  │  rulers · grid · snaps        │  orbit    │  mass, colour…   │
├ Command history ──────────────────────────────────────────────────────────────┤
│ ▸ Command: _                                  keyword chips  [Undo] [Close]    │
├ Status: coordinates · SNAP ORTHO OSNAP · GRID · layer · units · kernel ────────┤
```

- **Model space** is blueprint paper: white linework, rulers, a grid that adapts to the zoom level, the UCS
  icon and a crosshair cursor. Whatever is selected glows safety orange.
- **Split view** shows the plan and the 3D model side by side (View → Plan / Split / 3D).
- **Start screen**: a blank drawing, 8 templates, 18 samples filtered by industry, or open any file.

## Working like AutoCAD

Start typing anywhere — keys go to the command line. Commands accept the usual input:

| Input | Meaning |
|---|---|
| `10,20` | absolute point |
| `@50,0` | relative to the last point |
| `@100<45` | polar: distance 100 at 45° |
| `75` | direct distance along the cursor direction (works with ORTHO) |
| Enter / right-click | finish the current step or command |
| Esc | cancel |
| `U` / `C` / keyword chips | command options, e.g. **U**ndo, **C**lose, **D**iameter |

Inline arguments and scripts run without prompts: `CIRCLE 0,0 40`, `EXTRUDE L ; 25`
(`;` = Enter, `L` = last object, `P` = previous selection, `ALL` = everything).

Selection: click, or drag **left→right** for a window (fully inside) and **right→left** for crossing.
Shift adds. Pick objects first and then run MOVE/COPY/EXTRUDE — the selection is used directly.

Aids: **F3** object snap (endpoint, midpoint, centre, quadrant, intersection), **F8** ortho, **F9** grid snap,
wheel zoom, middle-drag or Space+drag pan, double-middle-click zoom extents.

## Commands

| Group | Commands (aliases) |
|---|---|
| Draw | LINE (L), PLINE (PL), RECTANG (REC), CIRCLE (C), ARC (A), POLYGON (POL), SPLINE (SPL), POINT (PO), TEXT (T) |
| Modify | MOVE (M), COPY (CO), ROTATE (RO), SCALE (SC), MIRROR (MI), OFFSET (O), ARRAY (AR, rectangular/polar), EXPLODE (X), ERASE (E) |
| Annotate | DIMLINEAR (DLI) |
| Solids | BOX, CYLINDER (CYL), CONE, SPHERE, TORUS (TOR), GEAR, EXTRUDE (EXT, nested shapes become holes), REVOLVE (REV), UNION (UNI), SUBTRACT (SU), INTERSECT (IN), ELEVATE (EL) |
| Inspect | DIST (DI), AREA (AA), MASSPROP (MP), LIST (LI) |
| Layers | LAYER (LA: New/Set/On/Off/Lock/Unlock/List), COLOR (COL) |
| View | ZOOM (Z), VIEW3D, PLAN, GRID, ORTHO, OSNAP, SELECTALL |
| AI | AI — describe a part; Claude generates it when `ANTHROPIC_API_KEY` is set (offline generator otherwise) |
| Utility | UNDO (Ctrl+Z), REDO (Ctrl+Y), HELP |

Booleans run on the Rust kernel when it is present (status bar shows the kernel).

## Files

- **Project**: `.gcproj` — gzip-compressed JSON with layers, drawing entities and solids.
- **Open / insert**: STEP, IGES, STL, OBJ, glTF/GLB, 3MF, DXF, SVG.
- **Export** (Output tab): STEP, STL, GLB, 3MF, DXF, SVG, PDF and a PNG render
  (a blueprint plot when the drawing has no solids).

## Templates and samples

| Templates | |
|---|---|
| Blank drawing (mm) · Architectural plan 1:50 · Mechanical part A3 · 3D print build plate · Game level blockout · Electronics enclosure · Robot work cell · Airframe layout | ![Templates](../images/studio/tpl-arch.png) |

Samples cover 17 industries: house (building & architecture), open-plan office, roller conveyor (industry),
Bluetooth speaker (products), spur gearbox and flange drawing (mechanical), wheel & brake (automotive),
sounding rocket (aerospace), 6-axis robot (robotics), phone stand plate (3D print), castle tower kit with
Blender-made props (games), rugged transport case (military), day-boat hull (marine), wind turbine (energy),
IoT enclosure (electronics), pill organiser (medical), dining set (furniture) and an FEM-analysed truss
footbridge (civil).

![Desktop samples](../images/studio-desktop-samples.png)

## Desktop edition

![Desktop edition](../images/studio-desktop-editor.png)

- GPU 3D through ThreeNet; `--software-3d` (or `GCAD_SOFTWARE_3D=1`) switches to the CPU renderer for remote
  desktops and VMs without a GPU, and it also kicks in automatically when the GPU fails.
- Double-clicking a `.gcproj` opens it (the installers register the file type).
- Closing with unsaved changes asks to save.

## Web edition

![Web start screen](../images/studio-web-start.png)

Each browser tab is its own session on the server. The release zips contain a self-contained server with
`start-studio.cmd` / `start-studio.sh`, which serves http://localhost:5190 and opens the browser.

## Code layout

| Project | Contents |
|---|---|
| `studio/Gravicode.CAD.Studio.Core` | document model with undo/redo, command engine, ~50 commands, `.gcproj` IO, catalog (templates + samples), ribbon definition, display lists |
| `studio/Gravicode.CAD.Studio.Web` | Blazor UI: ribbon, panels, `studioCanvas.js` blueprint canvas, command line |
| `studio/Gravicode.CAD.Studio.Desktop` | Avalonia UI: `BlueprintCanvas` control, ThreeNet 3D, file dialogs |
| `tests/Gravicode.CAD.Studio.Tests` | command scripts, booleans, undo, project round trip, exports, every catalog item |
| `studio/installer` | Inno Setup script, `.deb` and `.app`/`.dmg` packaging used by the release workflow |

Adding a command: register it in `StudioCommands` with `R("NAME", ["ALIAS"], "Group", "Description", Run)`;
`Run` is an iterator that `yield return`s prompts (`Prompt.Point`, `Prompt.Distance`, `Prompt.Select`, …) and
reads the answers from the context — both editions pick it up automatically.

# gcad — command-line tool

> 🇮🇩 [Baca dalam Bahasa Indonesia](../id/CLI.md)

```bash
dotnet tool install -g gravicode-cad
gcad about
```

| Command | Example | Does |
|---|---|---|
| `convert` | `gcad convert model.step model.stl [--units in] [--ascii]` | any supported format → any other |
| `info` | `gcad info engine.step` | units, bounds, per-body triangles / volume / area / centroid / colour |
| `validate` | `gcad validate part.stl` | watertight, orientation, shells, genus (exit code 2 when not solid) |
| `render` | `gcad render part.step part.png --view iso --width 1600 --light` | PNG preview (`iso`, `front`, `top`, `right`, `back`, `left`; `--transparent`, `--no-edges`) |
| `drawing` | `gcad drawing part.step part.pdf --title "Bracket"` | A3 engineering drawing (PDF or SVG) |
| `build` | `gcad build flange.gcad.json flange.step --set od=160` | rebuild a parametric JSON part with overrides |
| `shape` | `gcad shape gear 2 24 10 8 -o gear.step` | box, cylinder, sphere, cone, torus, tube, gear, nut |
| `slice` | `gcad slice part.stl part.gcode --layer 0.2 --infill 0.2` | FDM G-code (Marlin) with filament & time estimate |
| `generate` | `gcad generate "gearbox housing with four mounting holes" -o out` | text → CAD (`--provider claude\|azure\|deepseek\|hf\|offline`, `--key-file`, `--model`) |
| `formats` | `gcad formats` | lists importers / exporters |

`NO_COLOR=1` disables colours; `--verbose` prints stack traces.

```text
$ gcad shape gear 2 24 10 8 -o gear.step
  ✔ gear: 2,208 triangles, volume 17242.447 → gear.step
$ gcad validate gear.step
  ✔ gear: V=1104 E=3312 F=2208 | watertight=True oriented=True shells=1 χ=0 genus=1 | boundary=0 non-manifold=0
$ gcad slice gear.step gear.gcode --infill 0.15
  ✔ sliced in 323 ms → gear.gcode: 63,600 moves, path 69.22 m, filament 2.62 m, ≈00:31
```

The native Rust kernel is enabled automatically when the platform library is present (`gcad about` shows which kernel runs).

---
Made by Gravicode Studios, led by Kang Fadhil.

# gcad — tool baris perintah

> 🇬🇧 [Read in English](../en/CLI.md)

```bash
dotnet tool install -g gravicode-cad
gcad about
```

| Perintah | Contoh | Fungsi |
|---|---|---|
| `convert` | `gcad convert model.step model.stl [--units in] [--ascii]` | format apa pun → format lain |
| `info` | `gcad info engine.step` | satuan, batas, segitiga / volume / luas / centroid / warna per body |
| `validate` | `gcad validate part.stl` | watertight, orientasi, shell, genus (exit code 2 bila bukan solid) |
| `render` | `gcad render part.step part.png --view iso --width 1600 --light` | pratinjau PNG (`iso`, `front`, `top`, `right`, `back`, `left`; `--transparent`, `--no-edges`) |
| `drawing` | `gcad drawing part.step part.pdf --title "Braket"` | gambar teknik A3 (PDF atau SVG) |
| `build` | `gcad build flens.gcad.json flens.step --set od=160` | bangun ulang part JSON parametrik dengan override |
| `shape` | `gcad shape gear 2 24 10 8 -o gear.step` | box, cylinder, sphere, cone, torus, tube, gear, nut |
| `slice` | `gcad slice part.stl part.gcode --layer 0.2 --infill 0.2` | G-code FDM (Marlin) dengan estimasi filamen & waktu |
| `generate` | `gcad generate "housing gearbox dengan empat lubang mounting" -o out` | teks → CAD (`--provider claude\|azure\|deepseek\|hf\|offline`, `--key-file`, `--model`) |
| `formats` | `gcad formats` | daftar importer / exporter |

`NO_COLOR=1` mematikan warna; `--verbose` menampilkan stack trace.

```text
$ gcad shape gear 2 24 10 8 -o gear.step
  ✔ gear: 2,208 triangles, volume 17242.447 → gear.step
$ gcad validate gear.step
  ✔ gear: V=1104 E=3312 F=2208 | watertight=True oriented=True shells=1 χ=0 genus=1 | boundary=0 non-manifold=0
$ gcad slice gear.step gear.gcode --infill 0.15
  ✔ sliced in 323 ms → gear.gcode: 63,600 moves, path 69.22 m, filament 2.62 m, ≈00:31
```

Kernel native Rust aktif otomatis bila library platformnya ada (`gcad about` menampilkan kernel yang dipakai).

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.

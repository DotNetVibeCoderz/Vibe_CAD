# Geometri, boolean, parametrik & sketsa

> 🇬🇧 [Read in English](../en/Geometry.md)

## Matematika

`Vec2`, `Vec3` (double), `Mat4` (row-major, vektor kolom), `BoundingBox`, `Plane`, `CadColor`, `LengthUnit`.

```csharp
var m = Mat4.Translate(10, 0, 0) * Mat4.RotateAxis(Vec3.UnitZ, Units.Deg(30));
var p = m.TransformPoint(new Vec3(1, 2, 3));
```

## Kurva

| Tipe | Catatan |
|---|---|
| `Line2`, `Arc2` (`Arc2.ThroughPoints`) | 2D |
| `Bezier2` | derajat bebas, tessellasi adaptif |
| `NurbsCurve` | rasional, knot clamped default, `NurbsCurve.Circle` eksak |
| `Helix` | untuk ulir dan pegas |

Semua kurva punya `PointAt(t)` dan `Tessellate(toleransi)`.

## Profil

`Polygon2` (luas, centroid, contains, offset miter, simplify) dan `Profile2` (luar + lubang). Pabrik: `Rectangle`, `RoundedRectangle`, `Circle`, `RegularPolygon`, `Slot`, `Star`. `Profile2.FromLoops` menyusun loop sembarang berdasarkan siapa di dalam siapa. `Triangulator.Triangulate(profil)` melakukan ear-clipping dengan lubang.

## Solid (`Shapes`)

```csharp
Shapes.Box(w, d, h, centered: true);
Shapes.Cylinder(r, h); Shapes.Cone(r0, r1, h); Shapes.Sphere(r); Shapes.Torus(R, r); Shapes.Tube(ro, ri, h);
Shapes.Extrude(profil, h);
Shapes.ExtrudeTwisted(poligon, h, twistDegrees: 90, topScale: 0.7);
Shapes.Revolve(profilRZ, angleDegrees: 360);
Shapes.Loft(bawah, atas, h);
Shapes.Sweep(penampang, new Helix(20, 9, 7));
Shapes.SpurGear(module: 2, teeth: 24, thickness: 10, boreDiameter: 8);
Shapes.HexNut(acrossFlats: 13, thickness: 6.5, holeDiameter: 8.4);
```

## Boolean

```csharp
var part = Csg.Subtract(Shapes.Box(60, 40, 10), Shapes.Cylinder(6, 30, centered: true));
var u = a.Union(b);  var i = a.Intersect(b);
var banyak = Csg.UnionAll(meshes);
```

Input harus tertutup dan berorientasi keluar (semua `Shapes` memenuhi). Hasil otomatis diperbaiki dari T-junction.

## Topologi

```csharp
var he = new HalfEdgeMesh(mesh);
he.IsClosedManifold; he.Genus; he.Shells(); he.BoundaryLoops(); he.FeatureEdges(30);
var brep = BrepSolid.FromMesh(mesh);           // muka planar & lengkung beserta loop
var atas = brep.SelectFace(Vec3.UnitZ);
```

## Feature tree parametrik

```json
{
  "name": "Flens",
  "parameters": { "od": 140, "id": "od / 3", "t": 14 },
  "features": [
    { "type": "cylinder", "diameter": "od", "height": "t" },
    { "type": "hole", "diameter": "id", "z": "t" },
    { "type": "pattern", "kind": "circular", "count": 8,
      "source": { "type": "hole", "diameter": 12, "x": 56, "z": "t" } }
  ]
}
```

```csharp
var def = PartDefinition.FromJson(json);
var hasil = def.Rebuild(new Dictionary<string, double> { ["od"] = 160 });
foreach (var log in hasil.Log) Console.WriteLine($"{log.Id} {log.Status} {log.Milliseconds} ms");
```

Tipe fitur: `box`, `cylinder`, `cone`, `sphere`, `torus`, `tube`, `extrude`, `revolve`, `loft`, `sweep`, `gear`, `hole` (dengan counterbore), `pattern` (linear / grid / circular), `mirror`. Setiap fitur punya `x y z rx ry rz`, `operation` (add / subtract / intersect), `body` dan `color`. Lubang dibor **ke bawah** dari `z`; tanpa `z` dimulai dari puncak part, dan tanpa `depth` lubangnya tembus.

Ekspresi mendukung `+ - * / % ^`, kurung, `pi`, `e`, `sin cos tan` (radian), `sind cosd tand` (derajat), `sqrt abs min max floor ceil round pow rad deg clamp log exp`.

## Sketsa dengan constraint

```csharp
var s = new Sketch();
var l = s.Polyline((0, 0), (95, 3), (92, 28), (-2, 30));   // gambar kasar
s.FixAt(l[0].A, 0, 0).Horizontal(l[0]).Vertical(l[1]).Horizontal(l[2]).Vertical(l[3]);
s.Length(l[0], 100).Length(l[1], 30);
var lubang = s.Circle(25, 15, 5); s.Diameter(lubang, 10);
var r = s.Solve();                 // Solved, DOF = …
var solid = Shapes.Extrude(s.ToProfiles()[0], 8);
```

Constraint: coincident, horizontal, vertical, parallel, perpendicular, distance/length, jarak horizontal & vertikal, angle, radius/diameter, equal length, fixed, titik-pada-garis, titik-pada-lingkaran, midpoint, tangent (garis–lingkaran), symmetric.

---
Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil.

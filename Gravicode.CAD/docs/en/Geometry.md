# Geometry, booleans, parametric & sketches

> 🇮🇩 [Baca dalam Bahasa Indonesia](../id/Geometry.md)

## Math

`Vec2`, `Vec3` (double), `Mat4` (row-major, column vectors), `BoundingBox`, `Plane`, `CadColor`, `LengthUnit`.

```csharp
var m = Mat4.Translate(10, 0, 0) * Mat4.RotateAxis(Vec3.UnitZ, Units.Deg(30));
var p = m.TransformPoint(new Vec3(1, 2, 3));
```

## Curves

| Type | Notes |
|---|---|
| `Line2`, `Arc2` (`Arc2.ThroughPoints`) | 2D |
| `Bezier2` | any degree, adaptive tessellation |
| `NurbsCurve` | rational, clamped knots by default, `NurbsCurve.Circle` is exact |
| `Helix` | for threads and springs |

All curves expose `PointAt(t)` and `Tessellate(tolerance)`.

## Profiles

`Polygon2` (area, centroid, contains, miter offset, simplify) and `Profile2` (outer + holes). Factories: `Rectangle`, `RoundedRectangle`, `Circle`, `RegularPolygon`, `Slot`, `Star`. `Profile2.FromLoops` nests arbitrary loops by containment. `Triangulator.Triangulate(profile)` ear-clips with holes.

## Solids (`Shapes`)

```csharp
Shapes.Box(w, d, h, centered: true);
Shapes.Cylinder(r, h); Shapes.Cone(r0, r1, h); Shapes.Sphere(r); Shapes.Torus(R, r); Shapes.Tube(ro, ri, h);
Shapes.Extrude(profile, h);
Shapes.ExtrudeTwisted(polygon, h, twistDegrees: 90, topScale: 0.7);
Shapes.Revolve(profileRZ, angleDegrees: 360);
Shapes.Loft(bottom, top, h);
Shapes.Sweep(section, new Helix(20, 9, 7));
Shapes.SpurGear(module: 2, teeth: 24, thickness: 10, boreDiameter: 8);
Shapes.HexNut(acrossFlats: 13, thickness: 6.5, holeDiameter: 8.4);
```

## Booleans

```csharp
var part = Csg.Subtract(Shapes.Box(60, 40, 10), Shapes.Cylinder(6, 30, centered: true));
var u = a.Union(b);  var i = a.Intersect(b);
var many = Csg.UnionAll(meshes);
```

Inputs must be closed and outward-oriented (all `Shapes` are). Results are T-junction-repaired.

## Topology

```csharp
var he = new HalfEdgeMesh(mesh);
he.IsClosedManifold; he.Genus; he.Shells(); he.BoundaryLoops(); he.FeatureEdges(30);
var brep = BrepSolid.FromMesh(mesh);           // planar & curved faces with loops
var top = brep.SelectFace(Vec3.UnitZ);
```

## Parametric feature tree

```json
{
  "name": "Flange",
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
var result = def.Rebuild(new Dictionary<string, double> { ["od"] = 160 });
foreach (var log in result.Log) Console.WriteLine($"{log.Id} {log.Status} {log.Milliseconds} ms");
```

Feature types: `box`, `cylinder`, `cone`, `sphere`, `torus`, `tube`, `extrude`, `revolve`, `loft`, `sweep`, `gear`, `hole` (with counterbore), `pattern` (linear / grid / circular), `mirror`. Every feature has `x y z rx ry rz`, `operation` (add / subtract / intersect), `body` and `color`. Holes drill **down** from `z`; without `z` they start at the top of the part, and without `depth` they go through.

Expressions support `+ - * / % ^`, parentheses, `pi`, `e`, `sin cos tan` (radians), `sind cosd tand` (degrees), `sqrt abs min max floor ceil round pow rad deg clamp log exp`.

## Constraint sketches

```csharp
var s = new Sketch();
var l = s.Polyline((0, 0), (95, 3), (92, 28), (-2, 30));   // rough
s.FixAt(l[0].A, 0, 0).Horizontal(l[0]).Vertical(l[1]).Horizontal(l[2]).Vertical(l[3]);
s.Length(l[0], 100).Length(l[1], 30);
var hole = s.Circle(25, 15, 5); s.Diameter(hole, 10);
var r = s.Solve();                 // Solved, DOF = …
var solid = Shapes.Extrude(s.ToProfiles()[0], 8);
```

Constraints: coincident, horizontal, vertical, parallel, perpendicular, distance/length, horizontal & vertical distance, angle, radius/diameter, equal length, fixed, point-on-line, point-on-circle, midpoint, tangent (line–circle), symmetric.

---
Made by Gravicode Studios, led by Kang Fadhil.

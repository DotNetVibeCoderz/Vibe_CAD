using Gravicode.CAD.AI;
using Gravicode.CAD.CAM;
using Gravicode.CAD.Constraint;
using Gravicode.CAD.FEM;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Parametric;

namespace Gravicode.CAD.Gallery;

/// <summary>A runnable gallery sample: metadata, the C# shown to the user, and the code that builds it.</summary>
public sealed record Sample(string Id, string Title, string Category, string Summary, string SummaryId, string Code, Func<CadModel> Build)
{
    /// <summary>Key facts displayed next to the viewer (filled after building).</summary>
    public static IReadOnlyList<(string Label, string Value)> Facts(CadModel m)
    {
        var bb = m.Bounds;
        var mesh = m.ToMesh();
        var list = new List<(string, string)>
        {
            ("Bodies", m.Bodies.Count.ToString()),
            ("Triangles", m.TriangleCount.ToString("N0")),
            ("Size", $"{bb.Size.X:0.#} × {bb.Size.Y:0.#} × {bb.Size.Z:0.#} {m.Units.Symbol()}"),
        };
        if (!mesh.IsEmpty) list.Add(("Volume", $"{mesh.Volume:N0} {m.Units.Symbol()}³"));
        foreach (var (k, v) in m.Metadata.Where(kv => !kv.Key.StartsWith("param.", StringComparison.Ordinal)).Take(3)) list.Add((k, v));
        return list;
    }
}

/// <summary>All gallery samples, grouped by category.</summary>
public static class SampleCatalog
{
    public static readonly string[] Categories =
        ["Geometry", "Booleans", "Parametric", "Sketch", "Mechanical", "Simulation", "Manufacturing", "AI-CAD"];

    public static IReadOnlyList<Sample> All { get; } =
    [
        new("primitives", "Primitive solids", "Geometry",
            "Box, cylinder, sphere, cone, torus and tube — closed, outward-oriented meshes in double precision.",
            "Box, silinder, bola, kerucut, torus dan pipa — mesh tertutup presisi ganda.",
            """
            var model = new CadModel("Primitives");
            model.AddBody("Box", Shapes.Box(30, 30, 30, centered: false).Translated(-120, -15, 0), CadColor.Steel);
            model.AddBody("Cylinder", Shapes.Cylinder(15, 30).Translated(-60, 0, 0), CadColor.Blueprint);
            model.AddBody("Sphere", Shapes.Sphere(16).Translated(0, 0, 16), CadColor.Orange);
            model.AddBody("Cone", Shapes.Cone(16, 0, 32).Translated(60, 0, 0), CadColor.Brass);
            model.AddBody("Torus", Shapes.Torus(14, 5).Translated(120, 0, 5), CadColor.Copper);
            model.AddBody("Tube", Shapes.Tube(15, 11, 30).Translated(180, 0, 0), CadColor.Aluminum);
            """,
            () =>
            {
                var model = new CadModel("Primitives");
                model.AddBody("Box", Shapes.Box(30, 30, 30, centered: false).Translated(-120, -15, 0), CadColor.Steel);
                model.AddBody("Cylinder", Shapes.Cylinder(15, 30).Translated(-60, 0, 0), CadColor.Blueprint);
                model.AddBody("Sphere", Shapes.Sphere(16).Translated(0, 0, 16), CadColor.Orange);
                model.AddBody("Cone", Shapes.Cone(16, 0, 32).Translated(60, 0, 0), CadColor.Brass);
                model.AddBody("Torus", Shapes.Torus(14, 5).Translated(120, 0, 5), CadColor.Copper);
                model.AddBody("Tube", Shapes.Tube(15, 11, 30).Translated(180, 0, 0), CadColor.Aluminum);
                return model;
            }),

        new("csg-classic", "Classic CSG", "Booleans",
            "The textbook constructive-solid-geometry part: (cube ∩ sphere) − three cylinders, with exact BSP booleans.",
            "Contoh CSG klasik: (kubus ∩ bola) − tiga silinder, boolean BSP presisi.",
            """
            var core = Csg.Intersect(Shapes.Cube(40), Shapes.Sphere(27, 96, 48));
            var bore = Shapes.Cylinder(12, 60, 64, centered: true);
            core = core.Subtract(bore)
                       .Subtract(bore.Rotated(Vec3.UnitX, 90))
                       .Subtract(bore.Rotated(Vec3.UnitY, 90));
            var model = new CadModel("CsgClassic", core, CadColor.Blueprint);
            """,
            () =>
            {
                var core = Csg.Intersect(Shapes.Cube(40), Shapes.Sphere(27, 96, 48));
                var bore = Shapes.Cylinder(12, 60, 64, centered: true);
                core = core.Subtract(bore).Subtract(bore.Rotated(Vec3.UnitX, 90)).Subtract(bore.Rotated(Vec3.UnitY, 90));
                return new CadModel("CsgClassic", core, CadColor.Blueprint);
            }),

        new("parametric-plate", "Parametric mounting plate", "Parametric",
            "Fluent, CadQuery-style builder over a JSON feature tree. Change a parameter and rebuild.",
            "Builder bergaya CadQuery di atas feature tree JSON. Ubah parameter lalu rebuild.",
            """
            var model = Part.Create("MountingPlate")
                .Param("w", 120).Param("d", 80).Param("t", 10)
                .Box("w", "d", "t").Color("#9AA7B4")
                .Holes(8.5, (-48, -28), (48, -28), (-48, 28), (48, 28))
                .Cylinder(40, 28).At(0, 0, "t")
                .Holes(22, (0, 0))
                .Build(new Dictionary<string, double> { ["w"] = 140 });
            """,
            () => Part.Create("MountingPlate")
                .Param("w", 120).Param("d", 80).Param("t", 10)
                .Box("w", "d", "t").Color("#9AA7B4")
                .Holes(8.5, (-48, -28), (48, -28), (-48, 28), (48, 28))
                .Cylinder(40, 28).At(0, 0, "t")
                .Holes(22, (0, 0))
                .Build(new Dictionary<string, double> { ["w"] = 140 })),

        new("flange-json", "Flange from JSON", "Parametric",
            "A feature tree written as JSON (the same format the AI agent produces) with a circular bolt pattern.",
            "Feature tree dalam JSON (format yang sama dengan keluaran agen AI) dengan pola baut melingkar.",
            FlangeJson,
            () => PartDefinition.FromJson(FlangeJson).Build()),

        new("revolve-vase", "Revolved bottle", "Geometry",
            "Revolve a profile drawn in the XZ half-plane around Z — lathe-style geometry.",
            "Revolve profil pada bidang XZ mengelilingi sumbu Z — geometri ala mesin bubut.",
            """
            var profile = new List<Vec2> { new(0, 0), new(30, 0), new(34, 10), new(34, 70), new(28, 90),
                new(14, 105), new(12, 125), new(14, 128), new(14, 132), new(0, 132) };
            var model = new CadModel("Bottle", Shapes.Revolve(profile, 360, 96), CadColor.FromHex("#3FB4C9"));
            """,
            () =>
            {
                var profile = new List<Vec2> { new(0, 0), new(30, 0), new(34, 10), new(34, 70), new(28, 90), new(14, 105), new(12, 125), new(14, 128), new(14, 132), new(0, 132) };
                var m = new CadModel("Bottle", Shapes.Revolve(profile, 360, 96), CadColor.FromHex("#3FB4C9"));
                m.Bodies[0].Metallic = 0.1; m.Bodies[0].Roughness = 0.15;
                return m;
            }),

        new("twisted-vase", "Twisted extrusion", "Geometry",
            "Extrude a star profile with 120° of twist and a 0.6 top scale — generative design in one call.",
            "Extrude profil bintang dengan puntiran 120° dan skala atas 0,6.",
            """
            var star = Polygon2.Star(7, 40, 28);
            var mesh = Shapes.ExtrudeTwisted(star, height: 120, twistDegrees: 120, topScale: 0.6, steps: 60);
            var model = new CadModel("TwistedVase", mesh, CadColor.Orange);
            """,
            () => new CadModel("TwistedVase", Shapes.ExtrudeTwisted(Polygon2.Star(7, 40, 28), 120, 120, 0.6, 60), CadColor.Orange)),

        new("spring", "Helical spring", "Geometry",
            "Sweep a circle along a helix with rotation-minimising frames.",
            "Sweep lingkaran sepanjang heliks dengan frame minim-rotasi.",
            """
            var helix = new Helix(Radius: 20, Pitch: 9, Turns: 7);
            var mesh = Shapes.Sweep(Polygon2.Circle(2.5, 24), helix, tolerance: 0.05);
            var model = new CadModel("Spring", mesh, CadColor.Steel);
            """,
            () =>
            {
                var m = new CadModel("Spring", Shapes.Sweep(Polygon2.Circle(2.5, 24), new Helix(20, 9, 7), 0.05), CadColor.Steel);
                m.Bodies[0].Metallic = 0.95; m.Bodies[0].Roughness = 0.25;
                return m;
            }),

        new("loft", "Square-to-round loft", "Geometry",
            "Loft between a rounded square and a circle — a classic duct transition.",
            "Loft dari persegi membulat ke lingkaran — transisi saluran udara.",
            """
            var mesh = Shapes.Loft(Polygon2.RoundedRectangle(80, 80, 8), Polygon2.Circle(28, 64), height: 90);
            var model = new CadModel("Transition", mesh, CadColor.Aluminum);
            """,
            () => new CadModel("Transition", Shapes.Loft(Polygon2.RoundedRectangle(80, 80, 8), Polygon2.Circle(28, 64), 90), CadColor.Aluminum)),

        new("sketch-solver", "Constrained sketch", "Sketch",
            "Draw roughly, add constraints (horizontal, vertical, lengths, radius, tangency) and let the solver snap the sketch. Then extrude.",
            "Gambar kasar, tambahkan constraint lalu solver merapikan sketsa. Kemudian extrude.",
            """
            var s = new Sketch();
            var l = s.Polyline((0, 0), (95, 3), (92, 28), (60, 33), (55, 62), (2, 58));
            s.FixAt(l[0].A, 0, 0);
            s.Horizontal(l[0]).Vertical(l[1]).Horizontal(l[2]).Vertical(l[3]).Horizontal(l[4]).Vertical(l[5]);
            s.Length(l[0], 100).Length(l[1], 30).Length(l[3], 35).Length(l[4], 55);
            var hole = s.Circle(25, 25, 6);  s.Diameter(hole, 16);
            s.HorizontalDistance(l[0].A, hole.Center, 25).VerticalDistance(l[0].A, hole.Center, 30);
            var result = s.Solve();               // Solved, DOF = 0
            var profile = s.ToProfiles()[0];
            var model = new CadModel("SketchPlate", Shapes.Extrude(profile, 12));
            """,
            () =>
            {
                var s = new Sketch();
                var l = s.Polyline((0, 0), (95, 3), (92, 28), (60, 33), (55, 62), (2, 58));
                s.FixAt(l[0].A, 0, 0);
                s.Horizontal(l[0]).Vertical(l[1]).Horizontal(l[2]).Vertical(l[3]).Horizontal(l[4]).Vertical(l[5]);
                s.Length(l[0], 100).Length(l[1], 30).Length(l[3], 35).Length(l[4], 55);
                var hole = s.Circle(25, 25, 6); s.Diameter(hole, 16);
                s.HorizontalDistance(l[0].A, hole.Center, 25).VerticalDistance(l[0].A, hole.Center, 30);
                var r = s.Solve();
                var model = new CadModel("SketchPlate", Shapes.Extrude(s.ToProfiles()[0], 12), CadColor.Blueprint);
                model.Drawing.AddRange(s.ToDrawing());
                model.Metadata["solver"] = r.ToString();
                return model;
            }),

        new("gear-pair", "Meshing spur gears", "Mechanical",
            "Two involute spur gears at the correct centre distance m·(z₁+z₂)/2, phased so the teeth mesh.",
            "Dua roda gigi involute pada jarak sumbu m·(z₁+z₂)/2, fasenya diatur agar gigi saling bertaut.",
            """
            double m = 2.5; int z1 = 24, z2 = 16;
            double center = m * (z1 + z2) / 2;
            var g1 = Shapes.SpurGear(m, z1, 12, 12);
            var g2 = Shapes.SpurGear(m, z2, 12, 10)
                .Rotated(Vec3.UnitZ, 180.0 / z2)          // half a tooth: tooth into gap
                .Translated(center, 0, 0);
            var model = new CadModel("GearPair");
            model.AddBody("Driver", g1, CadColor.Brass);
            model.AddBody("Driven", g2, CadColor.Steel);
            """,
            () =>
            {
                double m = 2.5; int z1 = 24, z2 = 16;
                double center = m * (z1 + z2) / 2;
                var model = new CadModel("GearPair");
                var a = model.AddBody("Driver", Shapes.SpurGear(m, z1, 12, 12), CadColor.Brass); a.Metallic = 0.9;
                var b = model.AddBody("Driven", Shapes.SpurGear(m, z2, 12, 10).Rotated(Vec3.UnitZ, 180.0 / z2).Translated(center, 0, 0), CadColor.Steel); b.Metallic = 0.9;
                model.Metadata["centre distance"] = $"{center} mm";
                model.Metadata["ratio"] = $"{(double)z1 / z2:0.###}";
                return model;
            }),

        new("bolt-nut", "Bolt and nut", "Mechanical",
            "Hex head bolt with a threaded-look shank (stacked rings) and a matching hex nut.",
            "Baut kepala hex dengan batang berulir (cincin bertumpuk) dan mur hex.",
            """
            var head = Shapes.Extrude(new Profile2(Polygon2.RegularPolygon(6, 13 / Math.Sqrt(3))), 6.5);
            var shank = Shapes.Cylinder(4, 40, 48).Translated(0, 0, 6.5);
            var thread = Shapes.Sweep(Polygon2.RegularPolygon(3, 0.7), new Helix(4, 1.25, 20), 0.02)
                              .Translated(0, 0, 18);
            var model = new CadModel("BoltAndNut");
            model.AddBody("Bolt", Csg.UnionAll([head, shank]), CadColor.Steel);
            model.AddBody("Thread", thread, CadColor.Steel);
            model.AddBody("Nut", Shapes.HexNut(13, 6.5, 8.4).Translated(0, 0, 30), CadColor.Brass);
            """,
            () =>
            {
                var head = Shapes.Extrude(new Profile2(Polygon2.RegularPolygon(6, 13 / Math.Sqrt(3))), 6.5);
                var shank = Shapes.Cylinder(4, 40, 48).Translated(0, 0, 6.5);
                var thread = Shapes.Sweep(Polygon2.RegularPolygon(3, 0.7), new Helix(4, 1.25, 20), 0.02).Translated(0, 0, 18);
                var model = new CadModel("BoltAndNut");
                var b = model.AddBody("Bolt", Csg.UnionAll([head, shank]), CadColor.Steel); b.Metallic = 0.9;
                var t = model.AddBody("Thread", thread, CadColor.Steel); t.Metallic = 0.9;
                var n = model.AddBody("Nut", Shapes.HexNut(13, 6.5, 8.4).Translated(0, 0, 30), CadColor.Brass); n.Metallic = 0.9;
                return model;
            }),

        new("pin-grid", "Grid pattern", "Parametric",
            "A pin board made with a grid pattern feature — 6 × 4 pins on a rounded plate.",
            "Papan pin dengan fitur pola grid — 6 × 4 pin di atas pelat.",
            """
            var def = new PartDefinition { Name = "PinBoard" };
            def.Features.Add(new ExtrudeFeature { Sketch = new SketchSpec { Shape = "roundedRect", Width = 150, Height = 100, CornerRadius = 10 }, Height = 6 });
            def.Features.Add(new PatternFeature { Kind = "grid", Count = 6, CountY = 4, Spacing = 24, SpacingY = 24,
                Source = new CylinderFeature { Diameter = 8, Height = 14, X = -60, Y = -36, Z = 6 } });
            var model = def.Build();
            """,
            () =>
            {
                var def = new PartDefinition { Name = "PinBoard" };
                def.Features.Add(new ExtrudeFeature { Sketch = new SketchSpec { Shape = "roundedRect", Width = 150, Height = 100, CornerRadius = 10 }, Height = 6, Color = "#2F6FDE" });
                def.Features.Add(new PatternFeature { Kind = "grid", Count = 6, CountY = 4, Spacing = 24, SpacingY = 24, Source = new CylinderFeature { Diameter = 8, Height = 14, X = -60, Y = -36, Z = 6 } });
                return def.Build();
            }),

        new("truss", "Truss bridge analysis", "Simulation",
            "Warren truss under deck loads: linear static FEM, members coloured by axial stress (blue compression, red tension).",
            "Analisis rangka Warren: FEM statik linear, batang diwarnai tegangan aksial (biru tekan, merah tarik).",
            """
            var t = new Truss();
            var bottom = Enumerable.Range(0, 7).Select(i => t.AddNode(i * 2000, 0, 0)).ToArray();
            var top = Enumerable.Range(0, 6).Select(i => t.AddNode(1000 + i * 2000, 0, 1600)).ToArray();
            for (int i = 0; i < 6; i++) { t.AddMember(bottom[i], bottom[i + 1], 3000);
                t.AddMember(bottom[i], top[i], 2000); t.AddMember(top[i], bottom[i + 1], 2000); }
            for (int i = 0; i < 5; i++) t.AddMember(top[i], top[i + 1], 3000);
            t.Support(bottom[0]).Support(bottom[6], x: false);
            for (int i = 0; i < 13; i++) t.Support(i, x: false, y: true, z: false);   // planar truss in XZ
            foreach (var n in bottom[1..^1]) t.Load(n, new Vec3(0, 0, -60_000));
            var result = t.Solve();
            var model = t.ToModel(result, displacementScale: 40);
            """,
            () =>
            {
                var t = new Truss();
                var bottom = Enumerable.Range(0, 7).Select(i => t.AddNode(i * 2000, 0, 0)).ToArray();
                var top = Enumerable.Range(0, 6).Select(i => t.AddNode(1000 + i * 2000, 0, 1600)).ToArray();
                for (int i = 0; i < 6; i++) { t.AddMember(bottom[i], bottom[i + 1], 3000); t.AddMember(bottom[i], top[i], 2000); t.AddMember(top[i], bottom[i + 1], 2000); }
                for (int i = 0; i < 5; i++) t.AddMember(top[i], top[i + 1], 3000);
                t.Support(bottom[0], y: true).Support(bottom[6], x: false);
                for (int i = 0; i < 13; i++) t.Support(i, x: false, y: true, z: false);
                foreach (var n in bottom[1..^1]) t.Load(n, new Vec3(0, 0, -60_000));
                var r = t.Solve();
                var m = t.ToModel(r, 0, 40);
                m.Name = "TrussBridge";
                m.Metadata["max stress"] = $"{r.MaxStress:0.0} MPa";
                m.Metadata["max deflection"] = $"{r.MaxDisplacement:0.00} mm";
                return m;
            }),

        new("cantilever", "Cantilever stress plot", "Simulation",
            "Plane-stress CST analysis of a bracket with a hole: von Mises stress bands, deformation ×20.",
            "Analisis plane-stress braket berlubang: pita tegangan von Mises, deformasi ×20.",
            """
            var region = new Profile2(Polygon2.Rectangle(200, 50, centered: false),
                                      [Polygon2.Circle(12, 48, new Vec2(70, 25))]);
            var mesh = TriMesh2D.FromProfile(region, size: 4);
            var fem = new PlaneStressModel(mesh, Material.Aluminum, thickness: 10)
                .FixWhere(p => p.X < 1e-6)
                .LoadWhere(p => p.X > 200 - 1e-6, new Vec2(0, -4000));
            var result = fem.Solve();
            var model = fem.ToModel(result, displacementScale: 20);
            """,
            () =>
            {
                var region = new Profile2(Polygon2.Rectangle(200, 50, centered: false), [Polygon2.Circle(12, 48, new Vec2(70, 25))]);
                var mesh = TriMesh2D.FromProfile(region, 4);
                var fem = new PlaneStressModel(mesh, Material.Aluminum, 10).FixWhere(p => p.X < 1e-6).LoadWhere(p => p.X > 200 - 1e-6, new Vec2(0, -4000));
                var r = fem.Solve();
                var m = fem.ToModel(r, 20);
                m.Name = "Cantilever";
                m.Metadata["elements"] = mesh.Elements.Count.ToString("N0");
                return m;
            }),

        new("cnc-pocket", "CNC pocket & contour", "Manufacturing",
            "Zig-zag pocket around an island plus an outside profile contour; preview as wires, export G-code.",
            "Pocket zig-zag mengitari pulau dan kontur luar; pratinjau toolpath, ekspor G-code.",
            """
            var tool = Tool.EndMill(6);
            var pocket = Milling.Pocket(new Profile2(Polygon2.RoundedRectangle(100, 60, 10), [Polygon2.Circle(12, 48)]),
                                        tool, depth: 8, stepDown: 4);
            var contour = Milling.Contour(Polygon2.RoundedRectangle(130, 90, 14), tool, 20, 5);
            string gcode = GCodeWriter.Write([pocket, contour], GCodeFlavor.Grbl);
            """,
            () =>
            {
                var tool = Tool.EndMill(6);
                var pocket = Milling.Pocket(new Profile2(Polygon2.RoundedRectangle(100, 60, 10), [Polygon2.Circle(12, 48)]), tool, 8, 4);
                var contour = Milling.Contour(Polygon2.RoundedRectangle(130, 90, 14), tool, 20, 5);
                var stock = Csg.Subtract(Shapes.Box(130, 90, 20).Translated(0, 0, -10),
                    Shapes.Extrude(new Profile2(Polygon2.RoundedRectangle(100, 60, 10), [Polygon2.Circle(12, 48)]), 9).Translated(0, 0, -8));
                var m = new CadModel("CncPocket");
                m.AddBody("Stock", stock, CadColor.Aluminum).Roughness = 0.5;
                foreach (var w in pocket.ToPolylines().Concat(contour.ToPolylines()).Where(w => w.Layer == "CUT")) m.Wires.Add(w);
                var (cut, rapid, time) = pocket.Estimate();
                m.Metadata["pocket path"] = $"{cut / 1000:0.00} m";
                m.Metadata["cycle time"] = $"{time:mm\\:ss}";
                return m;
            }),

        new("print-slice", "3D-print slicing", "Manufacturing",
            "Slice a part into 0.8 mm layers (shown) and generate FDM G-code with perimeters and infill.",
            "Iris part menjadi lapisan 0,8 mm (ditampilkan) dan buat G-code FDM dengan perimeter dan infill.",
            """
            var part = Csg.Subtract(Shapes.Cylinder(30, 40, 96), Shapes.Sphere(26, 64, 32).Translated(0, 0, 40));
            var layers = Slicer.Slice(part, layerHeight: 0.8);
            var path = Slicer.PrintPath(part, new PrintSettings { InfillDensity = 0.2 });
            string gcode = GCodeWriter.Write([path], GCodeFlavor.Marlin);
            """,
            () =>
            {
                var part = Csg.Subtract(Shapes.Cylinder(30, 40, 96), Shapes.Sphere(26, 64, 32).Translated(0, 0, 40));
                var m = new CadModel("PrintSlice");
                foreach (var layer in Slicer.Slice(part, 0.8))
                    foreach (var loop in layer.Loops)
                        m.Wires.Add(new Polyline3(loop.Points.Select(p => p.ToVec3(layer.Z)), true) { Color = CadColor.Lerp(CadColor.Blueprint, CadColor.Orange, layer.Z / 40) });
                m.AddBody("Ghost", part.Translated(80, 0, 0), CadColor.FromHex("#FF7A1A"));
                m.Metadata["layers"] = Slicer.Slice(part, 0.8).Count.ToString();
                return m;
            }),

        new("ai-housing", "Text-to-CAD housing", "AI-CAD",
            "\"Create a gearbox housing with four mounting holes\" → validated parametric part → STEP/STL/PDF. Works offline; plug in Claude for open-ended requests.",
            "\"Buat housing gearbox dengan empat lubang mounting\" → part parametrik tervalidasi → STEP/STL/PDF.",
            """
            // Offline interpreter (no key needed). For an LLM:
            //   var agent = new TextToCadAgent(new ClaudeCadModel(apiKey));   // claude-opus-5-5
            var agent = new TextToCadAgent();
            var result = await agent.GenerateAsync("Create a gearbox housing with four mounting holes");
            result.ExportAll("out");   // .gcad.json, .step, .stl, .glb, .pdf, .svg
            var model = result.Model;
            """,
            () => new TextToCadAgent().GenerateAsync("Create a gearbox housing with four mounting holes").GetAwaiter().GetResult().Model),

        new("enclosure", "Electronics enclosure", "Parametric",
            "Shelled box with corner screw bosses and a separate lid body — ready for 3D printing.",
            "Kotak berongga dengan boss sekrup di sudut dan tutup terpisah — siap cetak 3D.",
            EnclosureJson,
            () => PartDefinition.FromJson(EnclosureJson).Build()),
    ];

    public static Sample? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    public const string FlangeJson = """
    {
      "name": "Flange",
      "parameters": { "od": 140, "id": 48, "t": 14, "bolts": 8, "bc": 112 },
      "features": [
        { "type": "cylinder", "diameter": "od", "height": "t", "color": "#C8734A" },
        { "type": "cylinder", "diameter": "id + 24", "height": 22, "z": "t" },
        { "type": "hole", "diameter": "id", "z": "t + 22" },
        { "type": "pattern", "kind": "circular", "count": "bolts",
          "source": { "type": "hole", "diameter": 12, "x": "bc / 2", "z": "t" } }
      ]
    }
    """;

    public const string EnclosureJson = """
    {
      "name": "Enclosure",
      "parameters": { "w": 110, "d": 70, "h": 40, "wall": 2.4, "boss": 8, "lid": 3, "gap": 30 },
      "features": [
        { "type": "extrude", "sketch": { "shape": "roundedRect", "width": "w", "height": "d", "cornerRadius": 6 }, "height": "h", "color": "#2F6FDE" },
        { "type": "extrude", "operation": "subtract", "z": "wall",
          "sketch": { "shape": "roundedRect", "width": "w - 2*wall", "height": "d - 2*wall", "cornerRadius": 4 }, "height": "h" },
        { "type": "pattern", "kind": "grid", "count": 2, "countY": 2, "spacing": "w - 2*wall - boss", "spacingY": "d - 2*wall - boss",
          "source": { "type": "cylinder", "diameter": "boss", "height": "h - wall - 2", "x": "-(w/2 - wall - boss/2)", "y": "-(d/2 - wall - boss/2)", "z": "wall" } },
        { "type": "pattern", "kind": "grid", "count": 2, "countY": 2, "spacing": "w - 2*wall - boss", "spacingY": "d - 2*wall - boss",
          "source": { "type": "hole", "diameter": 2.5, "depth": 18, "x": "-(w/2 - wall - boss/2)", "y": "-(d/2 - wall - boss/2)", "z": "h - 2" } },
        { "type": "extrude", "body": "Lid", "sketch": { "shape": "roundedRect", "width": "w", "height": "d", "cornerRadius": 6 }, "height": "lid", "y": "d + gap", "color": "#C9D1D9" },
        { "type": "pattern", "body": "Lid", "kind": "grid", "count": 2, "countY": 2, "spacing": "w - 2*wall - boss", "spacingY": "d - 2*wall - boss",
          "source": { "type": "hole", "body": "Lid", "diameter": 3.2, "x": "-(w/2 - wall - boss/2)", "y": "d + gap - (d/2 - wall - boss/2)", "z": "lid" } }
      ]
    }
    """;
}

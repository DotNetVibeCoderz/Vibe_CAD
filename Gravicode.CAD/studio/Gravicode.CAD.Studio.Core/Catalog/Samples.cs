using Gravicode.CAD.FEM;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Parametric;
using static Gravicode.CAD.Studio.Catalog.Draft;

namespace Gravicode.CAD.Studio.Catalog;

public static partial class StudioCatalog
{
    // ===================================================================== Building & Architecture

    private static StudioDocument House()
    {
        var d = New("Two-bedroom house", "Building & Architecture", "12 × 8 m single-storey house, 200 mm walls",
            ("Walls", "#E6EDF3"), ("Doors", "#F39C4A"), ("Windows", "#7FD1FF"), ("Text", "#F5D76E"), ("Roof", "#C8734A"));
        const double W = 12000, D = 8000, T = 200, H = 2800;
        // plan
        Rect(d, "Walls", 0, 0, W, D); Rect(d, "Walls", T, T, W - 2 * T, D - 2 * T);
        Rect(d, "Walls", 7000, T, 100, D - 2 * T);                 // living | bedrooms
        Rect(d, "Walls", 7100, 3950, W - 7100 - T, 100);            // bedroom split
        // doors (opening + leaf + swing)
        void Door(double x, double y, double w, bool vertical, double swing)
        {
            if (vertical) { Line(d, "Doors", x, y, x + w * Math.Cos(Units.Deg(swing)), y + w * Math.Sin(Units.Deg(swing))); Arc(d, "Doors", x, y, w, Math.Min(90, swing), Math.Max(90, swing)); }
            else { Line(d, "Doors", x, y, x, y + w); Arc(d, "Doors", x, y, w, 0, 90); }
        }
        Door(2000, T, 900, false, 0);       // entrance
        Door(7100, 5200, 800, false, 0);    // bedroom 1
        Door(7100, 1200, 800, false, 0);    // bedroom 2
        // windows (triple line in wall)
        void Window(double x0, double y0, double x1, double y1) { Line(d, "Windows", x0, y0, x1, y1); Line(d, "Windows", x0, y0 + (x1 > x0 ? T / 2 : 0), x1 + (x1 > x0 ? 0 : T / 2), y1 + (x1 > x0 ? T / 2 : 0)); Line(d, "Windows", x0, y0 + (x1 > x0 ? T : 0), x1 + (x1 > x0 ? 0 : T), y1 + (x1 > x0 ? T : 0)); }
        Window(3500, D - T, 5500, D - T); Window(8200, D - T, 10800, D - T); Window(8200, 0, 10800, 0); Window(4000, 0, 5800, 0);
        Text(d, "Text", 2500, 4200, "LIVING / KITCHEN", 220); Text(d, "Text", 2500, 3800, "≈ 47 m²", 180);
        Text(d, "Text", 8300, 6100, "BEDROOM 1", 220); Text(d, "Text", 8300, 2100, "BEDROOM 2", 220);
        Dim(d, 0, 0, W, 0, -900, 220); Dim(d, W, 0, W, D, -900, 220); Dim(d, 0, D, 7000, D, 700, 220);
        Sheet(d, -2500, -3500, 17500, 13500, "Two-bedroom house — ground floor", "1:50", 25);
        // 3D: shell = outer − inner, then partitions, openings, slab and gable roof
        var walls = Csg.Subtract(Box(0, 0, 0, W, D, H), Box(T, T, -10, W - 2 * T, D - 2 * T, H + 20));
        walls = Csg.UnionAll([walls, Box(7000, T, 0, 100, D - 2 * T, H), Box(7100, 3950, 0, W - 7100 - T, 100, H)]);
        var openings = Mesh.Merge([
            Box(2000, -50, 0, 900, T + 100, 2100), Box(7000 - 50, 5200, 0, 200, 800, 2100), Box(7000 - 50, 1200, 0, 200, 800, 2100),
            Box(3500, D - T - 50, 900, 2000, T + 100, 1300), Box(8200, D - T - 50, 900, 2600, T + 100, 1300),
            Box(8200, -50, 900, 2600, T + 100, 1300), Box(4000, -50, 900, 1800, T + 100, 1300)]);
        walls = Csg.Subtract(walls, openings);
        d.AddSolid("Walls", walls, "#E8E2D6", "Walls", 0, 0.85);
        d.AddSolid("Floor slab", Box(-100, -100, -200, W + 200, D + 200, 200), "#9AA0A6", "Solids", 0, 0.9);
        var roofProfile = new Polygon2([new(-400, 0), new(W + 400, 0), new(W / 2, 2600)]);
        var roof = Shapes.Extrude(new Profile2(roofProfile), D + 800).Transformed(Mat4.Translate(0, D + 400, H) * Mat4.RotateX(Math.PI / 2));
        d.AddSolid("Gable roof", roof, "#B5523B", "Roof", 0, 0.7);
        Done(d); return d;
    }

    // ===================================================================== Office

    private static StudioDocument Office()
    {
        var d = New("Open-plan office", "Office", "20 × 12 m open office with 12 desks and a meeting room",
            ("Walls", "#E6EDF3"), ("Furniture", "#9BE3B0"), ("Text", "#F5D76E"));
        Rect(d, "Walls", 0, 0, 20000, 12000); Rect(d, "Walls", 14000, 6000, 6000, 6000);
        Text(d, "Text", 15200, 9200, "MEETING", 300); Text(d, "Text", 1000, 11200, "OPEN PLAN — 12 WORKSTATIONS", 300);
        var desks = new List<Mesh>(); var chairs = new List<Mesh>();
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 4; col++)
            {
                double x = 1500 + col * 3000, y = 1500 + row * 3200;
                Rect(d, "Furniture", x, y, 1600, 800); Circle(d, "Furniture", x + 800, y - 350, 280);
                desks.Add(Box(x, y, 720, 1600, 800, 30)); desks.Add(Box(x, y, 0, 40, 800, 720)); desks.Add(Box(x + 1560, y, 0, 40, 800, 720));
                desks.Add(Box(x + 40, y + 760, 300, 1520, 25, 420)); // modesty panel
                chairs.Add(Cyl(x + 800, y - 350, 420, 260, 60, 24)); chairs.Add(Cyl(x + 800, y - 350, 0, 30, 420, 12)); chairs.Add(Box(x + 560, y - 640, 480, 480, 40, 450));
            }
        Rect(d, "Furniture", 15500, 8000, 3000, 1400);
        desks.Add(Box(15500, 8000, 720, 3000, 1400, 40)); desks.Add(Box(16700, 8500, 0, 600, 400, 720));
        d.AddSolid("Desks", Mesh.Merge(desks), "#D9C8A9", "Furniture", 0, 0.6);
        d.AddSolid("Chairs", Mesh.Merge(chairs), "#2F3B52", "Furniture", 0.1, 0.5);
        d.AddSolid("Meeting room glass", Csg.Subtract(Box(14000, 6000, 0, 6000, 6000, 2700), Box(14060, 6060, -10, 5880, 5880, 2720)), "#9CC9E8", "Walls", 0.2, 0.1);
        d.AddSolid("Floor", Box(0, 0, -100, 20000, 12000, 100), "#A3A8B0", "Solids", 0, 0.9);
        Dim(d, 0, 0, 20000, 0, -800, 250);
        Done(d); return d;
    }

    // ===================================================================== Industry

    private static StudioDocument Conveyor()
    {
        var d = New("Roller conveyor", "Industry", "3 m gravity/driven roller conveyor, 600 mm wide", ("Frame", "#E6EDF3"), ("Text", "#F5D76E"));
        const double L = 3000, Wd = 600, Hh = 800;
        var frame = new List<Mesh> { Box(0, -Wd / 2 - 80, Hh - 150, L, 80, 150), Box(0, Wd / 2, Hh - 150, L, 80, 150) };
        foreach (var x in new[] { 100.0, L - 180 }) foreach (var y in new[] { -Wd / 2 - 80, Wd / 2 }) frame.Add(Box(x, y, 0, 80, 80, Hh - 150));
        frame.Add(Box(100, -Wd / 2, 200, 80, Wd, 60)); frame.Add(Box(L - 180, -Wd / 2, 200, 80, Wd, 60));
        d.AddSolid("Frame", Mesh.Merge(frame), "#3F7DD8", "Solids", 0.6, 0.35);
        var rollers = Enumerable.Range(0, 20).Select(i => CylY(75 + i * 150, -Wd / 2, Hh - 40, 40, Wd, 24)).ToList();
        d.AddSolid("Rollers", Mesh.Merge(rollers), "#C9D1D9", "Solids", 0.9, 0.25);
        d.AddSolid("Drive motor", Mesh.Merge([Box(L / 2 - 150, Wd / 2 + 80, Hh - 380, 300, 220, 200), CylY(L / 2, Wd / 2 + 300, Hh - 280, 90, 160, 32)]), "#F39C4A", "Solids", 0.4, 0.4);
        // elevation drawing below the model
        double oy = -1600;
        Rect(d, "Frame", 0, oy + Hh - 150, L, 150);
        foreach (var x in new[] { 100.0, L - 180 }) Rect(d, "Frame", x, oy, 80, Hh - 150);
        for (int i = 0; i < 20; i++) Circle(d, "Frame", 75 + i * 150, oy + Hh - 40, 40);
        Dim(d, 0, oy, L, oy, -200, 60); Dim(d, 0, oy, 0, oy + Hh, 250, 60);
        Text(d, "Text", 0, oy - 450, "SIDE ELEVATION", 80);
        Done(d); return d;
    }

    // ===================================================================== Products

    private static StudioDocument Speaker()
    {
        var d = New("Bluetooth speaker", "Products", "Portable speaker housing 180 × 80 × 110 mm", ("Outline", "#E6EDF3"), ("Text", "#F5D76E"));
        var outer = Shapes.Extrude(new Profile2(Polygon2.RoundedRectangle(180, 80, 28)), 110);
        var inner = Shapes.Extrude(new Profile2(Polygon2.RoundedRectangle(172, 72, 24)), 104).Translated(0, 0, 3);
        var shell = Csg.Subtract(outer, inner);
        var holes = new List<Mesh>();
        for (int i = 0; i < 13; i++) for (int j = 0; j < 7; j++)
                if (Math.Pow((i - 6) / 6.5, 2) + Math.Pow((j - 3) / 3.5, 2) < 1) holes.Add(CylY(-66 + i * 11, -50, 20 + j * 11, 3, 20, 12));
        shell = Csg.Subtract(shell, Mesh.Merge(holes));
        d.AddSolid("Housing", shell, "#2B2F36", "Solids", 0.1, 0.55);
        d.AddSolid("Buttons", Mesh.Merge([Cyl(-30, 0, 110, 7, 3, 24), Cyl(0, 0, 110, 9, 3, 24), Cyl(30, 0, 110, 7, 3, 24)]), "#FF7A1A", "Solids", 0.2, 0.4);
        d.AddSolid("Feet", Mesh.Merge([Cyl(-60, -20, -2, 6, 2, 16), Cyl(60, -20, -2, 6, 2, 16), Cyl(-60, 20, -2, 6, 2, 16), Cyl(60, 20, -2, 6, 2, 16)]), "#4A5568", "Solids", 0, 0.9);
        // plan + front drawing
        d.Add(new PolylineEntity(Polygon2.RoundedRectangle(180, 80, 28).Points.Select(p => p + new Vec2(0, -150)), true), "Outline");
        Text(d, "Text", -90, -215, "TOP VIEW — housing 180 × 80", 6);
        Dim(d, -90, -150, 90, -150, -55, 5);
        Done(d); return d;
    }

    // ===================================================================== Mechanical

    private static StudioDocument Gearbox()
    {
        var d = New("Spur gearbox", "Mechanical", "Single-stage spur reduction, module 2.5, ratio 1.5:1", ("Outline", "#E6EDF3"), ("Center", "#F25F5C"), ("Text", "#F5D76E"));
        double m = 2.5; int z1 = 24, z2 = 36; double a = m * (z1 + z2) / 2;
        d.AddSolid("Pinion z24", Shapes.SpurGear(m, z1, 14, 12).Translated(0, 0, 30), "#D4A84B", "Solids", 0.9, 0.3);
        d.AddSolid("Wheel z36", Shapes.SpurGear(m, z2, 14, 16).Rotated(Vec3.UnitZ, 180.0 / z2).Translated(a, 0, 30), "#9AA7B4", "Solids", 0.9, 0.3);
        d.AddSolid("Shafts", Mesh.Merge([Cyl(0, 0, 0, 6, 90, 32), Cyl(a, 0, 0, 8, 90, 32)]), "#C9D1D9", "Solids", 0.95, 0.2);
        var plate = Csg.Subtract(Box(-50, -55, -12, a + 100, 110, 12), Mesh.Merge([Cyl(-35, -40, -20, 4.5, 40, 16), Cyl(a + 35, -40, -20, 4.5, 40, 16), Cyl(-35, 40, -20, 4.5, 40, 16), Cyl(a + 35, 40, -20, 4.5, 40, 16)]));
        d.AddSolid("Base plate", plate, "#4F5D73", "Solids", 0.5, 0.5);
        d.AddSolid("Bearing blocks", Mesh.Merge([Csg.Subtract(Box(-18, -18, 0, 36, 36, 22), Cyl(0, 0, -5, 6.2, 40, 32)), Csg.Subtract(Box(a - 20, -20, 0, 40, 40, 22), Cyl(a, 0, -5, 8.2, 40, 32))]), "#3F7DD8", "Solids", 0.6, 0.4);
        // drawing: pitch circles + centre distance
        Circle(d, "Center", 0, 0, m * z1 / 2); Circle(d, "Center", a, 0, m * z2 / 2);
        Line(d, "Center", -40, 0, a + 60, 0);
        Dim(d, 0, 0, a, 0, -70, 4);
        Text(d, "Text", -40, -100, $"CENTRE DISTANCE a = m(z1+z2)/2 = {a} mm   RATIO {(double)z2 / z1:0.##}:1", 4);
        Done(d); return d;
    }

    private static StudioDocument FlangeDrawing()
    {
        var d = New("Flange", "Mechanical", "Weld-neck style flange OD 140, 8 × Ø12 on PCD 112", ("Outline", "#E6EDF3"), ("Center", "#F25F5C"), ("Hidden", "#8A94A3"), ("Text", "#F5D76E"));
        const string json = """
        { "name": "Flange", "parameters": { "od": 140, "id": 48, "t": 14, "pcd": 112 },
          "features": [
            { "type": "cylinder", "diameter": "od", "height": "t", "color": "#C8734A" },
            { "type": "cylinder", "diameter": "id + 24", "height": 22, "z": "t" },
            { "type": "hole", "diameter": "id", "z": "t + 22" },
            { "type": "pattern", "kind": "circular", "count": 8, "source": { "type": "hole", "diameter": 12, "x": "pcd / 2", "z": "t" } } ] }
        """;
        foreach (var b in PartDefinition.FromJson(json).Build().Bodies) d.AddSolid("Flange", b.Mesh, "#C8734A", "Solids", 0.85, 0.3);
        // front view at x = 220
        double cx = 220, cy = 0;
        Circle(d, "Outline", cx, cy, 70); Circle(d, "Outline", cx, cy, 36); Circle(d, "Outline", cx, cy, 24);
        Circle(d, "Center", cx, cy, 56);
        for (int i = 0; i < 8; i++) { var p = new Vec2(cx, cy) + Vec2.FromAngle(i * Math.PI / 4, 56); Circle(d, "Outline", p.X, p.Y, 6); }
        Line(d, "Center", cx - 80, cy, cx + 80, cy); Line(d, "Center", cx, cy - 80, cx, cy + 80);
        // section view at x = 380
        double sx = 380;
        Poly(d, "Outline", true, (sx, -70), (sx + 14, -70), (sx + 14, -36), (sx + 36, -36), (sx + 36, -24), (sx, -24));
        Poly(d, "Outline", true, (sx, 24), (sx + 36, 24), (sx + 36, 36), (sx + 14, 36), (sx + 14, 70), (sx, 70));
        Line(d, "Center", sx - 10, 0, sx + 50, 0);
        Dim(d, cx - 70, cy, cx + 70, cy, -95, 4); Dim(d, sx, -70, sx, 70, -25, 4); Dim(d, sx, 70, sx + 36, 70, 18, 4);
        Text(d, "Text", cx + 30, cy + 62, "8× Ø12 EQ. SP. ON PCD 112", 3.5);
        Text(d, "Text", sx - 5, -95, "SECTION A-A", 4);
        Sheet(d, 100, -150, 420, 297, "Flange", "1:2");
        Done(d); return d;
    }

    // ===================================================================== Automotive

    private static StudioDocument Wheel()
    {
        var d = New("Wheel & brake", "Automotive", "17-inch style wheel with ventilated-look drilled disc", ("Outline", "#E6EDF3"), ("Text", "#F5D76E"));
        // rim: revolved profile (radius, z) — wheel lies flat, axis Z
        var rim = Shapes.Revolve([new(180, 0), new(215, 0), new(215, 12), new(200, 20), new(200, 200), new(215, 208), new(215, 220), new(180, 220), new(185, 200), new(185, 20)], 360, 96);
        var hub = Csg.Subtract(Cyl(0, 0, 120, 60, 40, 48), Mesh.Merge([Cyl(0, 0, 100, 30, 80, 32), .. Enumerable.Range(0, 5).Select(i => { var p = Vec2.FromAngle(i * 2 * Math.PI / 5, 45); return Cyl(p.X, p.Y, 100, 6, 80, 16); })]));
        var spokes = Enumerable.Range(0, 5).Select(i => Box(50, -14, 135, 140, 28, 22).Rotated(Vec3.UnitZ, i * 72)).ToList();
        d.AddSolid("Rim", Csg.UnionAll([rim, hub, .. spokes]), "#C9D1D9", "Solids", 0.95, 0.2);
        d.AddSolid("Tyre", Shapes.Torus(250, 60, 96, 32).Translated(0, 0, 110), "#1E2228", "Solids", 0, 0.8);
        var disc = Csg.Subtract(Shapes.Tube(160, 70, 24, 96).Translated(0, 0, 60), Mesh.Merge(Enumerable.Range(0, 24).Select(i => { var p = Vec2.FromAngle(i * 2 * Math.PI / 24, 120 + (i % 2) * 18); return Cyl(p.X, p.Y, 50, 4, 50, 12); })));
        d.AddSolid("Brake disc", disc, "#8A8F98", "Solids", 0.9, 0.45);
        d.AddSolid("Caliper", Box(100, -40, 50, 80, 80, 44).Rotated(Vec3.UnitZ, 30), "#E53E3E", "Solids", 0.3, 0.4);
        Circle(d, "Outline", 0, 0, 215); Circle(d, "Outline", 0, 0, 310);
        Dim(d, -310, 0, 310, 0, -360, 12);
        Text(d, "Text", -150, -420, "WHEEL ASSEMBLY — TYRE OD 620", 14);
        Done(d); return d;
    }

    // ===================================================================== Aerospace

    private static StudioDocument Rocket()
    {
        var d = New("Sounding rocket", "Aerospace", "Ø120 mm sounding rocket airframe, 4 fins", ("Airframe", "#E6EDF3"), ("Reference", "#F25F5C"), ("Text", "#F5D76E"));
        var profile = new List<Vec2> { new(0, 0), new(60, 0), new(60, 40) };
        profile.Add(new(60, 1500));
        for (int i = 1; i <= 12; i++) { double t = i / 12.0; profile.Add(new(60 * Math.Cos(t * Math.PI / 2), 1500 + 420 * Math.Sin(t * Math.PI / 2))); }
        profile.Add(new(0, 1920));
        d.AddSolid("Airframe", Shapes.Revolve(profile, 360, 64), "#EDF2F7", "Solids", 0.2, 0.35);
        d.AddSolid("Nose band", Shapes.Tube(61, 59, 30, 64).Translated(0, 0, 1460), "#E53E3E", "Solids", 0.3, 0.4);
        var fin = Shapes.Extrude(new Profile2(new Polygon2([new(55, 0), new(220, 60), new(220, 160), new(55, 300)])), 5).Transformed(Mat4.RotateX(Math.PI / 2) * Mat4.Translate(0, 0, -2.5));
        d.AddSolid("Fins", Mesh.Merge(Enumerable.Range(0, 4).Select(i => fin.Rotated(Vec3.UnitZ, i * 90))), "#2B6CB0", "Solids", 0.4, 0.4);
        d.AddSolid("Nozzle", Shapes.Cone(45, 30, 60, 48).Translated(0, 0, -60), "#4A5568", "Solids", 0.8, 0.4);
        // NACA 0012 fin airfoil drawing (chord 200) at x = 400
        double c = 200, ox = 400, oy = 900;
        var upper = Enumerable.Range(0, 41).Select(i => { double x = 1 - Math.Cos(i * Math.PI / 80); double yt = 5 * 0.12 * (0.2969 * Math.Sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1036 * x * x * x * x); return new Vec2(ox + x * c, oy + yt * c); }).ToList();
        var lower = upper.Select(p => new Vec2(p.X, 2 * oy - p.Y)).Reverse().Skip(1).ToList();
        d.Add(new PolylineEntity([.. upper, .. lower], true), "Airframe");
        Line(d, "Reference", ox - 10, oy, ox + c + 10, oy);
        Dim(d, ox, oy, ox + c, oy, -40, 8);
        Text(d, "Text", ox, oy + 50, "FIN SECTION — NACA 0012", 10);
        // side outline
        Poly(d, "Airframe", false, [.. profile.Select(p => (p.X + 700, p.Y))]);
        Done(d); return d;
    }

    // ===================================================================== Robotics

    private static StudioDocument Robot()
    {
        var d = New("6-axis robot", "Robotics", "Industrial articulated arm, 1.2 m reach", ("Fence", "#F6E05E"), ("Envelope", "#F25F5C"), ("Text", "#F5D76E"));
        const string orange = "#F28C28", graphite = "#2D3748";
        d.AddSolid("Base", Cyl(0, 0, 0, 160, 120, 64), graphite, "Solids", 0.4, 0.5);
        d.AddSolid("Turntable", Cyl(0, 0, 120, 130, 110, 64), orange, "Solids", 0.3, 0.4);
        var shoulder = Mat4.Translate(0, 0, 300);
        d.AddSolid("J2 joint", CylY(0, -110, 300, 85, 220, 48), graphite, "Solids", 0.4, 0.5);
        var upper = Box(-55, -60, 0, 110, 120, 560).Transformed(shoulder * Mat4.RotateY(Units.Deg(25)));
        d.AddSolid("Upper arm", upper, orange, "Solids", 0.3, 0.4);
        var elbow = shoulder.TransformPoint(Vec3.Zero) + new Vec3(Math.Sin(Units.Deg(25)) * 560, 0, Math.Cos(Units.Deg(25)) * 560);
        d.AddSolid("J3 joint", CylY(elbow.X, -90, elbow.Z, 65, 180, 48), graphite, "Solids", 0.4, 0.5);
        var fore = Box(0, -45, -45, 520, 90, 90).Transformed(Mat4.Translation3(elbow) * Mat4.RotateY(Units.Deg(15)));
        d.AddSolid("Forearm", fore, orange, "Solids", 0.3, 0.4);
        var wrist = elbow + new Vec3(Math.Cos(Units.Deg(15)) * 520, 0, -Math.Sin(Units.Deg(15)) * 520);
        d.AddSolid("Wrist", Shapes.Sphere(55, 32, 16).Translated(wrist), graphite, "Solids", 0.4, 0.5);
        var grip = Mat4.Translation3(wrist) * Mat4.RotateY(Units.Deg(15 + 90));
        d.AddSolid("Gripper", Mesh.Merge([Box(-40, -40, 50, 80, 80, 40).Transformed(grip), Box(-35, -12, 90, 15, 24, 90).Transformed(grip), Box(20, -12, 90, 15, 24, 90).Transformed(grip)]), "#A0AEC0", "Solids", 0.9, 0.3);
        d.AddSolid("Pallet", Box(700, -300, 0, 600, 600, 140), "#B7895B", "Solids", 0, 0.8);
        Rect(d, "Fence", -1500, -1500, 3000, 3000);
        Circle(d, "Envelope", 0, 0, 1200); Circle(d, "Envelope", 0, 0, 350);
        Text(d, "Text", -1450, 1550, "ROBOT CELL — REACH 1.2 m — SAFETY FENCE 1.8 m", 70);
        Done(d); return d;
    }

    // ===================================================================== 3D Print

    private static StudioDocument PhoneStand()
    {
        var d = New("Phone stand plate", "3D Print", "PLA phone stand + 6 cable clips on a 220 × 220 bed", ("Parts", "#FF9F43"), ("Bed", "#5F7FA8"), ("Text", "#F5D76E"));
        Rect(d, "Bed", 0, 0, 220, 220); Rect(d, "Bed", 5, 5, 210, 210);
        Text(d, "Bed", 4, -10, "BUILD PLATE 220 × 220 — PLA 0.2 mm", 5);
        // side profile drawn in XY, extruded 70 mm, then stood up on the bed
        var side = new Polygon2([new(0, 0), new(95, 0), new(95, 8), new(30, 8), new(78, 92), new(70, 96), new(18, 8), new(12, 8), new(12, 26), new(4, 26), new(4, 8), new(0, 8)]);
        var stand = Shapes.Extrude(new Profile2(side), 70).Transformed(Mat4.Translate(20, 140, 0) * Mat4.RotateX(Math.PI / 2));
        stand = Csg.Subtract(stand, Box(55, 140 - 70 + 25, -1, 30, 20, 12));
        d.AddSolid("Phone stand", stand, "#FF7A1A", "Parts", 0, 0.6);
        d.Add(new PolylineEntity(side.Points.Select(p => p + new Vec2(20, 145)), true), "Parts");
        var clip = Csg.Subtract(Shapes.Tube(9, 6, 10, 32), Box(-3, 5, -1, 6, 6, 12));
        d.AddSolid("Cable clips", Mesh.Merge(Enumerable.Range(0, 6).Select(i => clip.Translated(140 + (i % 3) * 24, 30 + (i / 3) * 26, 0))), "#4FD1C5", "Parts", 0, 0.6);
        Done(d); return d;
    }

    // ===================================================================== Games

    private static StudioDocument Castle()
    {
        var d = New("Castle tower kit", "Games", "Modular low-poly castle pieces (1 unit = 1 cm)", ("Blockout", "#B794F4"), ("Props", "#F6AD55"), ("Text", "#F5D76E"));
        var tower = Csg.Subtract(Shapes.Cylinder(300, 1200, 12), Box(-90, -320, 0, 180, 120, 260));
        var merlons = Enumerable.Range(0, 12).Select(i => Box(270, -40, 1200, 70, 80, 90).Rotated(Vec3.UnitZ, i * 30 + 15));
        d.AddSolid("Tower", Mesh.Merge([tower, .. merlons]), "#9C8B76", "Blockout", 0, 0.9);
        d.AddSolid("Roof", Shapes.Cone(340, 0, 500, 12).Translated(0, 0, 1290), "#4A5A8C", "Blockout", 0, 0.7);
        d.AddSolid("Door", Box(-80, -315, 0, 160, 20, 250), "#6B4423", "Props", 0, 0.8);
        var wall = Box(300, -100, 0, 1200, 200, 700);
        var crenels = Enumerable.Range(0, 8).Select(i => Box(330 + i * 150, -100, 700, 80, 200, 90));
        d.AddSolid("Wall section", Mesh.Merge([wall, .. crenels]), "#9C8B76", "Blockout", 0, 0.9);
        d.AddSolid("Banner pole", Cyl(0, 0, 1790, 8, 300, 8), "#4A5568", "Props", 0.6, 0.4);
        d.AddSolid("Banner", Box(8, -2, 1960, 160, 4, 110), "#C53030", "Props", 0, 0.7);
        Circle(d, "Blockout", 0, 0, 300); Rect(d, "Blockout", 300, -100, 1200, 200);
        Text(d, "Text", -300, -500, "MODULAR KIT — TOWER Ø6 m, WALL 12 m", 60);
        // props modelled in Blender (via MCP) and embedded as GLB; glTF metres import as mm, the kit is in cm → ×0.1
        AddAsset(d, "treasure_chest", new Vec3(-750, -300, 0), 0.25);
        AddAsset(d, "barrel", new Vec3(-620, 150, 0), 0.25);
        AddAsset(d, "barrel", new Vec3(-470, 260, 0), 0.25);
        Done(d); return d;
    }

    // ===================================================================== Military

    private static StudioDocument RuggedCase()
    {
        var d = New("Rugged transport case", "Military", "Field equipment case 500 × 320 × 260 mm", ("Outline", "#E6EDF3"), ("Text", "#F5D76E"));
        const string olive = "#5B6236";
        var body = Csg.Subtract(Box(-250, -160, 0, 500, 320, 180), Box(-240, -150, 10, 480, 300, 180));
        var ribs = Enumerable.Range(0, 3).Select(i => Box(-255, -165, 30 + i * 50, 510, 330, 12));
        d.AddSolid("Body", Mesh.Merge([body, .. ribs]), olive, "Solids", 0.1, 0.75);
        d.AddSolid("Lid", Box(-252, -162, 182, 504, 324, 78), olive, "Solids", 0.1, 0.75);
        var latches = new[] { -150.0, 150 }.SelectMany(x => new[] { Box(x - 25, -175, 150, 50, 14, 60), Box(x - 25, 161, 150, 50, 14, 60) });
        d.AddSolid("Latches", Mesh.Merge(latches), "#2D3748", "Solids", 0.8, 0.35);
        d.AddSolid("Handle", Mesh.Merge([Box(-90, -15, 262, 180, 30, 22), Box(-100, -12, 258, 20, 24, 10), Box(80, -12, 258, 20, 24, 10)]), "#1A202C", "Solids", 0.2, 0.6);
        Rect(d, "Outline", -250, -500, 500, 320);
        Text(d, "Text", -200, -420, "EQUIPMENT CASE — WATERTIGHT IP67", 16);
        Text(d, "Text", -200, -455, "NSN 0000-00-000-0000 (example)", 10);
        Done(d); return d;
    }

    // ===================================================================== Marine

    private static StudioDocument Hull()
    {
        var d = New("Day-boat hull", "Marine", "5.8 m day-boat, hull lofted through 8 stations", ("Stations", "#7FD1FF"), ("Text", "#F5D76E"));
        const double L = 5800;
        var rings = new List<IReadOnlyList<Vec3>>();
        for (int s = 0; s < 8; s++)
        {
            double t = s / 7.0, x = t * L;
            double beam = 1100 * Math.Sqrt(Math.Max(0.05, Math.Sin(Math.PI * (0.15 + 0.85 * (1 - t)) ))) * (t > 0.85 ? (1 - t) / 0.15 * 0.8 + 0.2 : 1);
            double depth = 650 - 150 * t, sheer = 900 + 200 * t * t;
            var ring = new List<Vec3>();
            for (int k = 0; k <= 12; k++) { double a = Math.PI * k / 12; ring.Add(new Vec3(x, -Math.Cos(a) * beam, sheer - Math.Sin(a) * depth)); }
            rings.Add(ring);
            d.Add(new PolylineEntity(ring.Select(p => new Vec2(p.Y + 2500 + s * 0, p.Z - 2200 + s * 0)), true), "Stations");
        }
        d.AddSolid("Hull", Shapes.LoftRings(rings, capEnds: true), "#F7FAFC", "Solids", 0.1, 0.3);
        d.AddSolid("Cabin", Box(1500, -650, 1000, 1600, 1300, 600), "#2B6CB0", "Solids", 0.2, 0.3);
        d.AddSolid("Windshield", Box(3100, -600, 1000, 60, 1200, 500), "#9CC9E8", "Solids", 0.3, 0.1);
        Text(d, "Text", 1300, -1700, "BODY PLAN — STATIONS 0–7", 120);
        Done(d); return d;
    }

    // ===================================================================== Energy

    private static StudioDocument Turbine()
    {
        var d = New("Wind turbine", "Energy", "Utility-scale turbine concept, 60 m hub height (units: m)", ("Foundation", "#E6EDF3"), ("Text", "#F5D76E"));
        d.Units = LengthUnit.Meter;
        d.AddSolid("Tower", Shapes.Cone(2.2, 1.3, 60, 48), "#EDF2F7", "Solids", 0.3, 0.4);
        d.AddSolid("Nacelle", Box(-1.5, -1.6, 60, 9, 3.2, 3.4), "#E2E8F0", "Solids", 0.3, 0.4);
        var hub = new Vec3(-2.6, 0, 61.7);
        d.AddSolid("Hub", Shapes.Sphere(1.4, 32, 16).Translated(hub), "#CBD5E0", "Solids", 0.3, 0.4);
        var section = Polygon2.RegularPolygon(10, 1).Transformed(p => new Vec2(p.X * 1.4, p.Y * 0.35));
        var blade = Shapes.ExtrudeTwisted(section, 32, 18, 0.25, 24);
        // blades radiate in the YZ plane (rotor faces −X)
        var blades = Enumerable.Range(0, 3).Select(i => blade.Transformed(Mat4.Translation3(hub) * Mat4.RotateX(Units.Deg(i * 120)))).ToList();
        d.AddSolid("Blades", Mesh.Merge(blades), "#F7FAFC", "Solids", 0.2, 0.35);
        d.AddSolid("Foundation", Cyl(0, 0, -2, 9, 2, 48), "#A0AEC0", "Solids", 0, 0.9);
        Circle(d, "Foundation", 0, 0, 9); Circle(d, "Foundation", 0, 0, 2.2);
        Text(d, "Text", -9, -11, "FOUNDATION Ø18 m", 1.2);
        Done(d); return d;
    }

    // ===================================================================== Electronics

    private static StudioDocument PcbBox()
    {
        var d = New("IoT enclosure", "Electronics", "Enclosure 110 × 70 × 40 mm around a populated PCB", ("PCB", "#38A169"), ("Holes", "#E6EDF3"), ("Text", "#F5D76E"));
        const string json = """
        { "name": "Enclosure", "parameters": { "w": 110, "d": 70, "h": 40, "wall": 2.4, "boss": 8 },
          "features": [
            { "type": "extrude", "sketch": { "shape": "roundedRect", "width": "w", "height": "d", "cornerRadius": 6 }, "height": "h" },
            { "type": "extrude", "operation": "subtract", "z": "wall", "sketch": { "shape": "roundedRect", "width": "w - 2*wall", "height": "d - 2*wall", "cornerRadius": 4 }, "height": "h" },
            { "type": "pattern", "kind": "grid", "count": 2, "countY": 2, "spacing": "w - 2*wall - boss", "spacingY": "d - 2*wall - boss",
              "source": { "type": "cylinder", "diameter": "boss", "height": 10, "x": "-(w/2 - wall - boss/2)", "y": "-(d/2 - wall - boss/2)", "z": "wall" } },
            { "type": "extrude", "operation": "subtract", "x": "w/2", "y": 0, "z": 14, "ry": 90, "sketch": { "shape": "roundedRect", "width": 8, "height": 12, "cornerRadius": 2 }, "height": 10 } ] }
        """;
        foreach (var b in PartDefinition.FromJson(json).Build().Bodies) d.AddSolid("Enclosure", b.Mesh, "#2F6FDE", "Solids", 0.1, 0.45);
        d.AddSolid("Lid", Shapes.Extrude(new Profile2(Polygon2.RoundedRectangle(110, 70, 6)), 2.4).Translated(0, 0, 70), "#E2E8F0", "Solids", 0.1, 0.5);
        d.AddSolid("PCB", Box(-50, -30, 12.4, 100, 60, 1.6), "#2F855A", "PCB", 0.1, 0.6);
        var parts = new List<Mesh> { Box(-20, -10, 14, 22, 22, 3), Box(10, 5, 14, 14, 8, 4), Box(30, -20, 14, 16, 10, 8) };
        for (int i = 0; i < 4; i++) parts.Add(Cyl(-38 + i * 8, 18, 14, 3, 8, 16));
        d.AddSolid("Components", Mesh.Merge(parts), "#1A202C", "PCB", 0.3, 0.4);
        Rect(d, "PCB", -50, -130, 100, 60);
        foreach (var (x, y) in new[] { (-45.0, -125.0), (45, -125), (45, -75), (-45, -75) }) Circle(d, "Holes", x, y, 1.6);
        Text(d, "Text", -50, -145, "PCB 100 × 60 — 4 × M3", 4);
        Done(d); return d;
    }

    // ===================================================================== Medical

    private static StudioDocument PillOrganiser()
    {
        var d = New("Pill organiser", "Medical", "Seven-day organiser, 210 × 60 × 25 mm", ("Outline", "#E6EDF3"), ("Text", "#F5D76E"));
        var pockets = Mesh.Merge(Enumerable.Range(0, 7).Select(i => Shapes.Extrude(new Profile2(Polygon2.RoundedRectangle(25, 48, 6)), 30).Translated(-90 + i * 30, 0, 3)));
        d.AddSolid("Tray", Csg.Subtract(Box(-108, -30, 0, 216, 60, 25), pockets), "#E2E8F0", "Solids", 0, 0.4);
        d.AddSolid("Lids", Mesh.Merge(Enumerable.Range(0, 7).Select(i => Box(-104 + i * 30, -29, 25, 28, 58, 2.5))), "#63B3ED", "Solids", 0, 0.3);
        string[] days = ["M", "T", "W", "T", "F", "S", "S"];
        for (int i = 0; i < 7; i++) { Rect(d, "Outline", -102.5 + i * 30, -24 - 100, 25, 48); Text(d, "Text", -94 + i * 30, -105, days[i], 8); }
        Text(d, "Text", -108, -140, "TOP VIEW — 7 POCKETS 25 × 48", 5);
        Done(d); return d;
    }

    // ===================================================================== Furniture

    private static StudioDocument Dining()
    {
        var d = New("Dining set", "Furniture", "1600 × 900 table with four chairs", ("Furniture", "#9BE3B0"), ("Text", "#F5D76E"));
        var table = new List<Mesh> { Box(-800, -450, 720, 1600, 900, 35) };
        foreach (var x in new[] { -740.0, 680 }) foreach (var y in new[] { -390.0, 330 }) table.Add(Box(x, y, 0, 60, 60, 720));
        d.AddSolid("Table", Mesh.Merge(table), "#B7895B", "Furniture", 0, 0.55);
        Mesh Chair()
        {
            var parts = new List<Mesh> { Box(-220, -220, 440, 440, 440, 30), Box(-220, 190, 470, 440, 30, 420) };
            foreach (var x in new[] { -210.0, 180 }) foreach (var y in new[] { -210.0, 180 }) parts.Add(Box(x, y, 0, 30, 30, 440));
            return Mesh.Merge(parts);
        }
        var chairs = new[] { (-400.0, -700.0, 180.0), (400, -700, 180), (-400, 700, 0), (400, 700, 0) }
            .Select(t => Chair().Rotated(Vec3.UnitZ, t.Item3).Translated(t.Item1, t.Item2, 0));
        d.AddSolid("Chairs", Mesh.Merge(chairs), "#3B4A5A", "Furniture", 0, 0.6);
        Rect(d, "Furniture", -800, -450, 1600, 900);
        foreach (var (x, y) in new[] { (-400.0, -700.0), (400, -700), (-400, 700), (400, 700) }) Rect(d, "Furniture", x - 220, y - 220, 440, 440);
        Dim(d, -800, -450, 800, -450, -450, 40); Dim(d, 800, -450, 800, 450, -250, 40);
        Done(d); return d;
    }

    // ===================================================================== Civil

    private static StudioDocument Bridge()
    {
        var d = New("Truss footbridge", "Civil", "12 m Warren truss, 4 × 15 kN deck loads, steel", ("Analysis", "#F25F5C"), ("Text", "#F5D76E"));
        var t = new Truss();
        var bottom = Enumerable.Range(0, 7).Select(i => t.AddNode(i * 2000, 0, 0)).ToArray();
        var top = Enumerable.Range(0, 6).Select(i => t.AddNode(1000 + i * 2000, 0, 1600)).ToArray();
        for (int i = 0; i < 6; i++) { t.AddMember(bottom[i], bottom[i + 1], 3000); t.AddMember(bottom[i], top[i], 2000); t.AddMember(top[i], bottom[i + 1], 2000); }
        for (int i = 0; i < 5; i++) t.AddMember(top[i], top[i + 1], 3000);
        t.Support(bottom[0]).Support(bottom[6], x: false);
        for (int i = 0; i < 13; i++) t.Support(i, x: false, y: true, z: false);
        foreach (var n in bottom[1..^1]) t.Load(n, new Vec3(0, 0, -15_000));
        var r = t.Solve();
        var model = t.ToModel(r, 0, 0);
        // one solid per member keeps the stress colour of each bar
        foreach (var b in model.Bodies.Where(b => b.Name.StartsWith("member")))
            d.AddSolid(b.Name, b.Mesh, b.Color.ToHex(), "Analysis", 0.5, 0.45);
        d.AddSolid("Nodes", Mesh.Merge(model.Bodies.Where(b => b.Name.StartsWith("node")).Select(b => b.Mesh)), "#2D3748", "Analysis", 0.6, 0.4);
        d.AddSolid("Deck", Box(0, -900, -120, 12000, 1800, 100), "#A0AEC0", "Solids", 0, 0.8);
        Text(d, "Text", 0, -1500, $"MAX STRESS {r.MaxStress:0.0} MPa — MAX DEFLECTION {r.MaxDisplacement:0.00} mm", 160);
        Text(d, "Text", 0, -1800, "BLUE = COMPRESSION · RED = TENSION", 140);
        Done(d); return d;
    }

    // ===================================================================== assets

    /// <summary>Inserts an embedded GLB asset (made in Blender) when it is bundled with the build.</summary>
    private static void AddAsset(StudioDocument d, string name, Vec3 at, double scale)
    {
        var asm = typeof(StudioCatalog).Assembly;
        using var s = asm.GetManifestResourceStream($"assets/{name}.glb");
        if (s is null) return;
        Gravicode.CAD.IO.CadIO.RegisterAll();
        var model = CadModel.Load(s, "glb");
        // glTF is metres; the kit uses centimetre-scale units
        var bb = model.Bounds;
        foreach (var b in model.Bodies)
            d.AddSolid($"{name}/{b.Name}", b.Mesh.Translated(-bb.Center.X, -bb.Center.Y, -bb.Min.Z).Scaled(scale).Translated(at), b.Color.ToHex(), "Props", b.Metallic, b.Roughness);
    }
}

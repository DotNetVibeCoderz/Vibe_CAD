using System.Globalization;
using Gravicode.CAD.AI;
using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Studio.Commands;

/// <summary>The built-in command set (names and aliases follow AutoCAD conventions).</summary>
public static partial class StudioCommands
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void RegisterAll(CommandEngine e)
    {
        void R(string name, string[] aliases, string group, string desc, Func<CommandContext, IEnumerable<Prompt>> run) =>
            e.Register(new CommandInfo(name, aliases, group, desc, run));

        // ---------- draw ----------
        R("LINE", ["L"], "Draw", "Draws straight line segments", Line);
        R("PLINE", ["PL"], "Draw", "Draws a polyline (C closes it)", Pline);
        R("RECTANG", ["REC", "RECT"], "Draw", "Draws a rectangle from two corners", Rect);
        R("CIRCLE", ["C"], "Draw", "Draws a circle from centre and radius (D for diameter)", Circle);
        R("ARC", ["A"], "Draw", "Draws an arc through three points", Arc);
        R("POLYGON", ["POL"], "Draw", "Draws a regular polygon", PolygonCmd);
        R("SPLINE", ["SPL"], "Draw", "Draws a smooth curve through points", Spline);
        R("POINT", ["PO"], "Draw", "Places a point", PointCmd);
        R("TEXT", ["T", "DT", "MTEXT"], "Annotate", "Places single-line text", TextCmd);
        R("DIMLINEAR", ["DLI", "DIM", "DIMALIGNED", "DAL"], "Annotate", "Aligned dimension between two points", Dim);

        // ---------- modify ----------
        R("MOVE", ["M"], "Modify", "Moves objects", c => Transform(c, "Move", copy: false));
        R("COPY", ["CO", "CP"], "Modify", "Copies objects", c => Transform(c, "Copy", copy: true));
        R("ROTATE", ["RO"], "Modify", "Rotates objects about a base point", Rotate);
        R("SCALE", ["SC"], "Modify", "Scales objects about a base point", Scale);
        R("MIRROR", ["MI"], "Modify", "Mirrors objects across a line", Mirror);
        R("ERASE", ["E", "DEL"], "Modify", "Erases objects", Erase);
        R("OFFSET", ["O"], "Modify", "Creates parallel copies of lines, arcs, circles and polylines", Offset);
        R("ARRAY", ["AR", "ARRAYRECT", "ARRAYPOLAR"], "Modify", "Rectangular or polar array of objects", Array);
        R("EXPLODE", ["X"], "Modify", "Breaks polylines into lines, and blocks, hatches and dimensions into their parts", Explode);

        // ---------- solids ----------
        R("BOX", [], "Solid", "Creates a box solid", Box);
        R("CYLINDER", ["CYL"], "Solid", "Creates a cylinder solid", Cylinder);
        R("CONE", [], "Solid", "Creates a cone solid", Cone);
        R("SPHERE", [], "Solid", "Creates a sphere solid", Sphere);
        R("TORUS", ["TOR"], "Solid", "Creates a torus solid", Torus);
        R("GEAR", [], "Solid", "Creates an involute spur gear", Gear);
        R("EXTRUDE", ["EXT"], "Solid", "Extrudes closed shapes into solids (nested shapes become holes)", Extrude);
        R("REVOLVE", ["REV"], "Solid", "Revolves a closed shape around the drawing Y axis", Revolve);
        R("UNION", ["UNI"], "Solid", "Combines solids", c => Boolean(c, "Union"));
        R("SUBTRACT", ["SU"], "Solid", "Subtracts solids from a base solid", Subtract);
        R("INTERSECT", ["IN"], "Solid", "Keeps the common volume of solids", c => Boolean(c, "Intersect"));
        R("ELEVATE", ["EL"], "Solid", "Moves solids up or down along Z", Elevate);

        // ---------- inquiry ----------
        R("DIST", ["DI"], "Inquiry", "Measures distance and angle between two points", Dist);
        R("AREA", ["AA"], "Inquiry", "Area and perimeter of a closed object", Area);
        R("MASSPROP", ["MP"], "Inquiry", "Volume, surface area and centroid of solids", MassProp);
        R("LIST", ["LI"], "Inquiry", "Describes selected objects", ListCmd);

        // ---------- utilities ----------
        R("LAYER", ["LA"], "Utility", "LAYER NEW name [#color] | SET | ON | OFF | LOCK | UNLOCK name", LayerCmd);
        R("COLOR", ["COL"], "Utility", "Sets the current colour (#RRGGBB or BYLAYER) or recolours the selection", ColorCmd);
        R("UNDO", ["U"], "Utility", "Undoes the last change", c => Simple(c, () => c.Print(c.Doc.Undo() ? "Undone." : "Nothing to undo.")));
        R("REDO", [], "Utility", "Redoes the last undone change", c => Simple(c, () => c.Print(c.Doc.Redo() ? "Redone." : "Nothing to redo.")));
        R("ZOOM", ["Z"], "View", "Zoom extents", c => Simple(c, () => c.Engine.RequestView("zoom-extents")));
        R("VIEW3D", ["3D", "ORBIT", "3DO"], "View", "Shows the 3D model view", c => Simple(c, () => c.Engine.RequestView("view-3d")));
        R("PLAN", ["2D"], "View", "Shows the 2D drafting view", c => Simple(c, () => c.Engine.RequestView("view-2d")));
        R("GRID", [], "View", "Toggles grid snap", c => Simple(c, () => { c.Settings.GridSnap = !c.Settings.GridSnap; c.Print($"Grid snap {(c.Settings.GridSnap ? "on" : "off")}."); }));
        R("ORTHO", [], "View", "Toggles orthogonal mode", c => Simple(c, () => { c.Settings.Ortho = !c.Settings.Ortho; c.Print($"Ortho {(c.Settings.Ortho ? "on" : "off")}."); }));
        R("OSNAP", ["OS"], "View", "Toggles object snap", c => Simple(c, () => { c.Settings.ObjectSnap = !c.Settings.ObjectSnap; c.Print($"Object snap {(c.Settings.ObjectSnap ? "on" : "off")}."); }));
        R("SELECTALL", ["ALL"], "Utility", "Selects every visible object", c => Simple(c, c.Engine.SelectAll));
        R("AI", ["TEXT2CAD", "GENERATE"], "AI", "Creates a part from a description (Claude when ANTHROPIC_API_KEY is set, offline otherwise)", Ai);
        R("HELP", ["?"], "Utility", "Lists commands", Help);
        RegisterDrafting(e);
    }

    // ===================================================================== helpers

    private static IEnumerable<Prompt> Simple(CommandContext c, Action a) { a(); yield break; }

    private static StudioEntity AddEntity(CommandContext c, DrawingEntity g, string label)
    {
        StudioEntity? created = null;
        c.Doc.Edit(label, d => created = d.Add(g, color: c.EntityColor));
        c.Engine.LastCreated = created!.Id;
        return created;
    }

    private static StudioSolid AddSolid(CommandContext c, string name, Mesh mesh, string label)
    {
        StudioSolid? created = null;
        var color = c.EntityColor ?? SolidColors[c.Doc.Solids.Count % SolidColors.Length];
        c.Doc.Edit(label, d => created = d.AddSolid(name, mesh, color));
        c.Engine.LastCreated = created!.Id;
        c.Print($"  {name}: volume {mesh.Volume.ToString("N1", Inv)} {c.Doc.Units.Symbol()}³");
        return created;
    }

    private static readonly string[] SolidColors = ["#9AA7B4", "#4F8DF7", "#F39C4A", "#D4A84B", "#6CC7A3", "#C8734A", "#B794F4"];

    private static StudioSolid WithMesh(StudioSolid s, Mesh m) => new(s.Id, s.Name, s.Layer, m, s.Color, s.Metallic, s.Roughness);

    /// <summary>Applies a 2D transform to entities and the matching 3D transform to solids.</summary>
    private static void ApplyTransform(StudioDocument d, HashSet<Guid> ids, Func<Vec2, Vec2> map2, Mat4 m3, bool copy, CommandEngine engine)
    {
        var newIds = new List<Guid>();
        for (int i = 0, n = d.Entities.Count; i < n; i++)
        {
            var e = d.Entities[i];
            if (!ids.Contains(e.Id)) continue;
            var g = e.Geometry.Transformed(map2);
            if (copy) { var ne = e with { Id = Guid.NewGuid(), Geometry = g }; d.Entities.Add(ne); newIds.Add(ne.Id); }
            else d.Entities[i] = e with { Geometry = g };
        }
        for (int i = 0, n = d.Solids.Count; i < n; i++)
        {
            var s = d.Solids[i];
            if (!ids.Contains(s.Id)) continue;
            var mesh = s.Mesh.Transformed(m3);
            if (copy) { var ns = StudioSolid.Create(s.Name + " copy", s.Layer, mesh, s.Color, s.Metallic, s.Roughness); d.Solids.Add(ns); newIds.Add(ns.Id); }
            else d.Solids[i] = WithMesh(s, mesh);
        }
        if (newIds.Count > 0) engine.LastCreated = newIds[^1];
    }

    private static IEnumerable<DrawingEntity> Ghost(CommandContext c, Func<Vec2, Vec2> map)
    {
        foreach (var e in c.SelectedEntities.Take(400)) yield return e.Geometry.Transformed(map);
        foreach (var s in c.SelectedSolids.Take(40))
            foreach (var (a, b) in s.PlanOutline.Take(800)) yield return new LineEntity(map(a), map(b));
    }

    /// <summary>Asks for a selection unless one exists already (pick-first editing).</summary>
    private static IEnumerable<Prompt> RequireSelection(CommandContext c, string msg = "Select objects")
    {
        if (!c.HasSelection) yield return Prompt.Select(msg);
    }

    // ===================================================================== draw

    private static IEnumerable<Prompt> Line(CommandContext c)
    {
        yield return Prompt.Point("Specify first point");
        var pts = new List<Vec2> { c.Point };
        while (true)
        {
            yield return Prompt.Point("Specify next point", pts[^1], allowEnd: true, keywords: pts.Count >= 3 ? ["Close", "Undo"] : ["Undo"]);
            if (c.IsEnd) yield break;
            if (c.Keyword == "Close") { AddEntity(c, new LineEntity(pts[^1], pts[0]), "Line"); yield break; }
            if (c.Keyword == "Undo") { if (pts.Count > 1) { c.Doc.Undo(); pts.RemoveAt(pts.Count - 1); } continue; }
            AddEntity(c, new LineEntity(pts[^1], c.Point), "Line");
            pts.Add(c.Point);
        }
    }

    private static IEnumerable<Prompt> Pline(CommandContext c)
    {
        yield return Prompt.Point("Specify start point");
        var pts = new List<Vec2> { c.Point };
        while (true)
        {
            var snapshot = pts.ToList();
            yield return Prompt.Point("Specify next point", pts[^1], p => [new PolylineEntity([.. snapshot, p], false)], allowEnd: true, keywords: pts.Count >= 3 ? ["Close", "Undo"] : ["Undo"]);
            if (c.IsEnd) break;
            if (c.Keyword == "Undo") { if (pts.Count > 1) pts.RemoveAt(pts.Count - 1); continue; }
            if (c.Keyword == "Close") { AddEntity(c, new PolylineEntity(pts, true), "Polyline"); yield break; }
            pts.Add(c.Point);
        }
        if (pts.Count >= 2) AddEntity(c, new PolylineEntity(pts, false), "Polyline");
    }

    private static PolylineEntity RectOf(Vec2 a, Vec2 b) =>
        new([a, new Vec2(b.X, a.Y), b, new Vec2(a.X, b.Y)], true);

    private static IEnumerable<Prompt> Rect(CommandContext c)
    {
        yield return Prompt.Point("Specify first corner point");
        var a = c.Point;
        yield return Prompt.Point("Specify other corner point (or @width,height)", a, p => [RectOf(a, p)]);
        AddEntity(c, RectOf(a, c.Point), "Rectangle");
    }

    private static IEnumerable<Prompt> Circle(CommandContext c)
    {
        yield return Prompt.Point("Specify center point");
        var ctr = c.Point;
        yield return Prompt.Distance("Specify radius of circle", ctr, preview: p => [new CircleEntity(ctr, Math.Max(Vec2.Distance(ctr, p), 1e-6))]) with { Keywords = ["Diameter"] };
        double r;
        if (c.Keyword == "Diameter")
        {
            yield return Prompt.Distance("Specify diameter of circle", ctr, preview: p => [new CircleEntity(ctr, Math.Max(Vec2.Distance(ctr, p) / 2, 1e-6))]);
            r = DistOf(c, ctr) / 2;
        }
        else r = DistOf(c, ctr);
        if (r > 0) AddEntity(c, new CircleEntity(ctr, r), "Circle");
    }

    private static double DistOf(CommandContext c, Vec2 from) => c.Input switch
    {
        Input.NumberInput n => n.Value,
        Input.PointInput p => Vec2.Distance(from, p.Value),
        _ => 0
    };

    private static IEnumerable<Prompt> Arc(CommandContext c)
    {
        yield return Prompt.Point("Specify start point of arc");
        var a = c.Point;
        yield return Prompt.Point("Specify second point of arc", a);
        var b = c.Point;
        yield return Prompt.Point("Specify end point of arc", b, p => TryArc(a, b, p) is { } arc ? [arc] : [new LineEntity(b, p)]);
        if (TryArc(a, b, c.Point) is { } result) AddEntity(c, result, "Arc");
        else c.Print("  Points are collinear — no arc created.");
    }

    private static ArcEntity? TryArc(Vec2 a, Vec2 b, Vec2 p)
    {
        try { var arc = Arc2.ThroughPoints(a, b, p); return new ArcEntity(arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle); }
        catch { return null; }
    }

    private static IEnumerable<Prompt> PolygonCmd(CommandContext c)
    {
        yield return Prompt.Integer("Enter number of sides", 6);
        int n = c.IsEnd ? 6 : Math.Clamp((int)c.Number, 3, 1024);
        yield return Prompt.Point("Specify center of polygon");
        var ctr = c.Point;
        PolylineEntity Make(Vec2 p) { var r = Vec2.Distance(ctr, p); var a0 = (p - ctr).Angle; return new PolylineEntity(Enumerable.Range(0, n).Select(i => ctr + Vec2.FromAngle(a0 + 2 * Math.PI * i / n, r)), true); }
        yield return Prompt.Distance("Specify radius of circle (vertex)", ctr, preview: p => [Make(p)]);
        var pt = c.Input is Input.PointInput pp ? pp.Value : ctr + new Vec2(c.Number, 0);
        AddEntity(c, Make(pt), "Polygon");
    }

    private static IEnumerable<Prompt> Spline(CommandContext c)
    {
        var pts = new List<Vec2>();
        yield return Prompt.Point("Specify first point");
        pts.Add(c.Point);
        while (true)
        {
            var snap = pts.ToList();
            yield return Prompt.Point("Enter next point", pts[^1], p => [SplineThrough([.. snap, p])], allowEnd: true);
            if (c.IsEnd) break;
            pts.Add(c.Point);
        }
        if (pts.Count >= 2) AddEntity(c, SplineThrough(pts), "Spline");
    }

    /// <summary>Catmull-Rom curve through fit points, as a polyline.</summary>
    private static PolylineEntity SplineThrough(List<Vec2> p)
    {
        if (p.Count < 3) return new PolylineEntity(p, false);
        var r = new List<Vec2>();
        for (int i = 0; i + 1 < p.Count; i++)
        {
            Vec2 p0 = p[Math.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Math.Min(p.Count - 1, i + 2)];
            for (int s = 0; s < 16; s++)
            {
                double t = s / 16.0, t2 = t * t, t3 = t2 * t;
                r.Add(0.5 * (2 * p1 + (-1 * p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-1 * p0 + 3 * p1 - 3 * p2 + p3) * t3));
            }
        }
        r.Add(p[^1]);
        return new PolylineEntity(r, false);
    }

    private static IEnumerable<Prompt> PointCmd(CommandContext c)
    {
        yield return Prompt.Point("Specify a point");
        AddEntity(c, new PointEntity(c.Point), "Point");
    }

    private static IEnumerable<Prompt> TextCmd(CommandContext c)
    {
        yield return Prompt.Point("Specify start point of text");
        var at = c.Point;
        yield return Prompt.Distance("Specify height", at, 5);
        double h = c.IsEnd ? 5 : DistOf(c, at);
        yield return Prompt.Text("Enter text");
        if (c.Text.Length > 0) AddEntity(c, new TextEntity(at, c.Text, h > 0 ? h : 5), "Text");
    }

    private static IEnumerable<Prompt> Dim(CommandContext c)
    {
        yield return Prompt.Point("Specify first extension line origin");
        var a = c.Point;
        yield return Prompt.Point("Specify second extension line origin", a);
        var b = c.Point;
        if (a.ApproxEquals(b, 1e-9)) yield break;
        DimensionEntity Make(Vec2 p) => new(a, b, Vec2.Dot(p - a, (b - a).Normalized().Perp()), Math.Max(2.5, Vec2.Distance(a, b) / 30));
        yield return Prompt.Point("Specify dimension line location", b, p => [Make(p)]);
        var dim = Make(c.Point);
        c.Doc.Edit("Dimension", d => { var e = d.Add(dim, "Dimensions"); c.Engine.LastCreated = e.Id; });
        c.Print($"  Dimension text = {dim.Label}");
    }

    // ===================================================================== modify

    private static IEnumerable<Prompt> Transform(CommandContext c, string label, bool copy)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        if (!c.HasSelection) yield break;
        yield return Prompt.Point("Specify base point");
        var b = c.Point;
        int copies = 0;
        while (true)
        {
            yield return Prompt.Point("Specify second point", b, p => Ghost(c, q => q + (p - b)), allowEnd: copy && copies > 0);
            if (c.IsEnd) break;
            var d = c.Point - b;
            var ids = c.Engine.Selection.ToHashSet();
            c.Doc.Edit(label, doc => ApplyTransform(doc, ids, q => q + d, Mat4.Translate(d.X, d.Y, 0), copy, c.Engine));
            copies++;
            if (!copy) break;
        }
        c.Print($"  {c.Engine.Selection.Count} object(s) {(copy ? $"copied ×{copies}" : "moved")}.");
    }

    private static IEnumerable<Prompt> Rotate(CommandContext c)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        if (!c.HasSelection) yield break;
        yield return Prompt.Point("Specify base point");
        var b = c.Point;
        yield return Prompt.Distance("Specify rotation angle (degrees) or pick", b, preview: p => Ghost(c, q => b + (q - b).Rotate((p - b).Angle)));
        double ang = c.Input is Input.PointInput pt ? (pt.Value - b).Angle : Units.Deg(c.Number);
        var ids = c.Engine.Selection.ToHashSet();
        c.Doc.Edit("Rotate", d => ApplyTransform(d, ids, q => b + (q - b).Rotate(ang), Mat4.RotateAround(b.ToVec3(), Vec3.UnitZ, ang), false, c.Engine));
        c.Print($"  Rotated {Units.ToDegrees(ang):0.##}°.");
    }

    private static IEnumerable<Prompt> Scale(CommandContext c)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        if (!c.HasSelection) yield break;
        yield return Prompt.Point("Specify base point");
        var b = c.Point;
        yield return Prompt.Distance("Specify scale factor", b, 1);
        double f = c.Input is Input.PointInput pt ? Vec2.Distance(b, pt.Value) : c.IsEnd ? 1 : c.Number;
        if (f <= 0) { c.Print("  Scale factor must be positive."); yield break; }
        var ids = c.Engine.Selection.ToHashSet();
        var m = Mat4.Translation3(b.ToVec3()) * Mat4.Scale(f) * Mat4.Translation3(-b.ToVec3());
        c.Doc.Edit("Scale", d => ApplyTransform(d, ids, q => b + (q - b) * f, m, false, c.Engine));
    }

    private static IEnumerable<Prompt> Mirror(CommandContext c)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        if (!c.HasSelection) yield break;
        yield return Prompt.Point("Specify first point of mirror line");
        var a = c.Point;
        Func<Vec2, Vec2> Map(Vec2 b) => q => { var d = (b - a).Normalized(); var v = q - a; return a + d * (2 * Vec2.Dot(v, d)) - v; };
        yield return Prompt.Point("Specify second point of mirror line", a, p => p.ApproxEquals(a, 1e-9) ? [] : Ghost(c, Map(p)));
        var b = c.Point;
        yield return Prompt.Keyword("Erase source objects?", "Yes", "No");
        bool erase = c.Keyword == "Yes";
        var ids = c.Engine.Selection.ToHashSet();
        var dir = (b - a).Normalized();
        var m = Mat4.Mirror(a.ToVec3(), new Vec3(-dir.Y, dir.X, 0));
        c.Doc.Edit("Mirror", d => ApplyTransform(d, ids, Map(b), m, !erase, c.Engine));
    }

    private static IEnumerable<Prompt> Erase(CommandContext c)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        c.Engine.DeleteSelection();
    }

    private static IEnumerable<Prompt> Offset(CommandContext c)
    {
        yield return Prompt.Distance("Specify offset distance", null, 10);
        double dist = c.IsEnd ? 10 : c.Number;
        while (true)
        {
            c.Engine.Selection.Clear();
            yield return Prompt.Select("Select object to offset (Enter to exit)") with { Single = true };
            var src = c.SelectedEntities.FirstOrDefault();
            if (src is null) yield break;
            yield return Prompt.Point("Specify point on side to offset", null, p => Geometry2D.Offset(src.Geometry, dist, p) is { } g ? [g] : []);
            if (Geometry2D.Offset(src.Geometry, dist, c.Point) is { } result)
                c.Doc.Edit("Offset", d => { var e = d.Add(result, src.Layer, src.Color); c.Engine.LastCreated = e.Id; });
            else c.Print("  Cannot offset that object.");
        }
    }

    private static IEnumerable<Prompt> Array(CommandContext c)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        if (!c.HasSelection) yield break;
        yield return Prompt.Keyword("Enter array type", "Rectangular", "Polar");
        var ids = c.Engine.Selection.ToHashSet();
        if (c.Keyword == "Polar")
        {
            yield return Prompt.Point("Specify center point of array");
            var ctr = c.Point;
            yield return Prompt.Integer("Enter number of items", 6);
            int n = c.IsEnd ? 6 : Math.Max(2, (int)c.Number);
            yield return Prompt.Distance("Specify angle to fill", null, 360);
            double fill = c.IsEnd ? 360 : c.Number;
            double step = Math.Abs(fill - 360) < 1e-9 ? 360.0 / n : fill / (n - 1);
            c.Doc.Edit("Polar array", d =>
            {
                for (int i = 1; i < n; i++)
                {
                    double a = Units.Deg(step * i);
                    ApplyTransform(d, ids, q => ctr + (q - ctr).Rotate(a), Mat4.RotateAround(ctr.ToVec3(), Vec3.UnitZ, a), true, c.Engine);
                }
            });
            c.Print($"  Polar array: {n} items.");
        }
        else
        {
            yield return Prompt.Integer("Enter number of columns", 3);
            int cols = c.IsEnd ? 3 : Math.Max(1, (int)c.Number);
            yield return Prompt.Integer("Enter number of rows", 2);
            int rows = c.IsEnd ? 2 : Math.Max(1, (int)c.Number);
            yield return Prompt.Distance("Specify column spacing", null, 50);
            double dx = c.IsEnd ? 50 : c.Number;
            yield return Prompt.Distance("Specify row spacing", null, 50);
            double dy = c.IsEnd ? 50 : c.Number;
            c.Doc.Edit("Rectangular array", d =>
            {
                for (int r = 0; r < rows; r++)
                    for (int col = 0; col < cols; col++)
                    {
                        if (r == 0 && col == 0) continue;
                        var off = new Vec2(col * dx, r * dy);
                        ApplyTransform(d, ids, q => q + off, Mat4.Translate(off.X, off.Y, 0), true, c.Engine);
                    }
            });
            c.Print($"  Rectangular array: {rows} × {cols}.");
        }
    }

    private static IEnumerable<Prompt> Explode(CommandContext c)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        var targets = c.SelectedEntities.Where(e => e.Geometry is PolylineEntity or BlockRefEntity or HatchEntity or DimensionEntity).ToList();
        if (targets.Count == 0) { c.Print("  Nothing to explode."); yield break; }
        c.Doc.Edit("Explode", d =>
        {
            foreach (var e in targets)
            {
                int at = d.Entities.FindIndex(x => x.Id == e.Id);
                if (at < 0) continue;
                d.Entities.RemoveAt(at);
                IEnumerable<StudioEntity> parts = e.Geometry switch
                {
                    PolylineEntity pl => Pairs(pl.Tessellate()).Select(s => StudioEntity.Create(e.Layer, new LineEntity(s.A, s.B), e.Color)),
                    // a block explodes one level: its objects come back with their own layers and colours
                    BlockRefEntity br => br.Children().Select(ch => StudioEntity.Create(ch.Layer == "0" ? e.Layer : ch.Layer, ch.Geometry, ch.Color ?? e.Color)),
                    _ => Geometry2D.Primitives(e.Geometry).Select(pr => StudioEntity.Create(pr.Layer ?? e.Layer, pr.Geometry, pr.Color ?? e.Color))
                };
                foreach (var part in parts) { d.EnsureLayer(part.Layer); d.Entities.Insert(at++, part); }
            }
        });
        c.Engine.Selection.Clear();
        c.Print($"  {targets.Count} object(s) exploded.");
    }

    private static IEnumerable<(Vec2 A, Vec2 B)> Pairs(IReadOnlyList<Vec2> pts)
    {
        for (int i = 0; i + 1 < pts.Count; i++) yield return (pts[i], pts[i + 1]);
    }

    // ===================================================================== solids

    private static IEnumerable<Prompt> Box(CommandContext c)
    {
        yield return Prompt.Point("Specify first corner");
        var a = c.Point;
        yield return Prompt.Point("Specify other corner (or @length,width)", a, p => [RectOf(a, p)]);
        var b = c.Point;
        yield return Prompt.Distance("Specify height", b, 20);
        double h = c.IsEnd ? 20 : DistOf(c, b);
        double w = Math.Abs(b.X - a.X), dpt = Math.Abs(b.Y - a.Y);
        if (w < 1e-9 || dpt < 1e-9 || h <= 0) { c.Print("  Box needs a non-zero size."); yield break; }
        var mesh = Shapes.Box(w, dpt, h, false).Translated(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), 0);
        AddSolid(c, "Box", mesh, "Box");
    }

    private static IEnumerable<Prompt> Cylinder(CommandContext c)
    {
        yield return Prompt.Point("Specify center point of base");
        var ctr = c.Point;
        yield return Prompt.Distance("Specify base radius", ctr, preview: p => [new CircleEntity(ctr, Math.Max(Vec2.Distance(ctr, p), 1e-6))]);
        double r = DistOf(c, ctr);
        yield return Prompt.Distance("Specify height", ctr, 20);
        double h = c.IsEnd ? 20 : DistOf(c, ctr);
        if (r > 0 && h > 0) AddSolid(c, "Cylinder", Shapes.Cylinder(r, h).Translated(ctr.X, ctr.Y, 0), "Cylinder");
    }

    private static IEnumerable<Prompt> Cone(CommandContext c)
    {
        yield return Prompt.Point("Specify center point of base");
        var ctr = c.Point;
        yield return Prompt.Distance("Specify base radius", ctr, preview: p => [new CircleEntity(ctr, Math.Max(Vec2.Distance(ctr, p), 1e-6))]);
        double r = DistOf(c, ctr);
        yield return Prompt.Distance("Specify top radius", null, 0);
        double rt = c.IsEnd ? 0 : c.Number;
        yield return Prompt.Distance("Specify height", ctr, 30);
        double h = c.IsEnd ? 30 : DistOf(c, ctr);
        if (r > 0 && h > 0) AddSolid(c, "Cone", Shapes.Cone(r, rt, h).Translated(ctr.X, ctr.Y, 0), "Cone");
    }

    private static IEnumerable<Prompt> Sphere(CommandContext c)
    {
        yield return Prompt.Point("Specify center point");
        var ctr = c.Point;
        yield return Prompt.Distance("Specify radius", ctr, preview: p => [new CircleEntity(ctr, Math.Max(Vec2.Distance(ctr, p), 1e-6))]);
        double r = DistOf(c, ctr);
        if (r > 0) AddSolid(c, "Sphere", Shapes.Sphere(r).Translated(ctr.X, ctr.Y, r), "Sphere");
    }

    private static IEnumerable<Prompt> Torus(CommandContext c)
    {
        yield return Prompt.Point("Specify center point");
        var ctr = c.Point;
        yield return Prompt.Distance("Specify radius of torus", ctr, preview: p => [new CircleEntity(ctr, Math.Max(Vec2.Distance(ctr, p), 1e-6))]);
        double R = DistOf(c, ctr);
        yield return Prompt.Distance("Specify tube radius", null, R / 5);
        double r = c.IsEnd ? R / 5 : c.Number;
        if (R > 0 && r > 0 && r < R) AddSolid(c, "Torus", Shapes.Torus(R, r).Translated(ctr.X, ctr.Y, r), "Torus");
    }

    private static IEnumerable<Prompt> Gear(CommandContext c)
    {
        yield return Prompt.Point("Specify center point");
        var ctr = c.Point;
        yield return Prompt.Distance("Enter module", null, 2);
        double m = c.IsEnd ? 2 : c.Number;
        yield return Prompt.Integer("Enter number of teeth", 24);
        int z = c.IsEnd ? 24 : Math.Clamp((int)c.Number, 6, 400);
        yield return Prompt.Distance("Enter face width", null, 10);
        double w = c.IsEnd ? 10 : c.Number;
        yield return Prompt.Distance("Enter bore diameter", null, Math.Round(m * z / 5));
        double bore = c.IsEnd ? Math.Round(m * z / 5) : c.Number;
        AddSolid(c, $"Gear m{m} z{z}", Shapes.SpurGear(m, z, w, bore).Translated(ctr.X, ctr.Y, 0), "Gear");
    }

    private static IEnumerable<Prompt> Extrude(CommandContext c)
    {
        foreach (var p in RequireSelection(c, "Select closed objects to extrude")) yield return p;
        var loops = c.SelectedEntities.Select(e => Geometry2D.AsLoop(e.Geometry)).OfType<Polygon2>().ToList();
        if (loops.Count == 0) { c.Print("  Select closed polylines, rectangles, polygons or circles."); yield break; }
        yield return Prompt.Distance("Specify height of extrusion", null, 20);
        double h = c.IsEnd ? 20 : c.Number;
        if (Math.Abs(h) < 1e-9) yield break;
        var profiles = Profile2.FromLoops(loops);
        var ids = c.SelectedEntities.Select(e => e.Id).ToHashSet();
        var meshes = profiles.Select(p => { var m = Shapes.Extrude(p, Math.Abs(h)); return h < 0 ? m.Translated(0, 0, h) : m; }).ToList();
        c.Doc.Edit("Extrude", d =>
        {
            d.Entities.RemoveAll(e => ids.Contains(e.Id));
            foreach (var m in meshes) { var s = d.AddSolid("Extrusion", m, c.EntityColor ?? SolidColors[d.Solids.Count % SolidColors.Length]); c.Engine.LastCreated = s.Id; }
        });
        c.Engine.Selection.Clear();
        c.Print($"  {meshes.Count} solid(s) created ({profiles.Sum(p => p.Holes.Count)} hole(s)).");
    }

    private static IEnumerable<Prompt> Revolve(CommandContext c)
    {
        foreach (var p in RequireSelection(c, "Select a closed profile (X = radius, Y = height)")) yield return p;
        var src = c.SelectedEntities.FirstOrDefault(e => Geometry2D.AsLoop(e.Geometry) is not null);
        if (src is null) { c.Print("  Select a closed profile."); yield break; }
        yield return Prompt.Distance("Specify angle of revolution", null, 360);
        double ang = c.IsEnd ? 360 : c.Number;
        var loop = Geometry2D.AsLoop(src.Geometry)!;
        // the profile is revolved about the vertical line through its minimum X
        double axisX = loop.Points.Min(p => p.X);
        var profile = loop.Points.Select(p => new Vec2(p.X - axisX, p.Y)).ToList();
        double y0 = profile.Min(p => p.Y);
        var mesh = Shapes.Revolve([.. profile.Select(p => new Vec2(p.X, p.Y - y0))], ang).Translated(axisX, (loop.Points.Max(p => p.Y) + y0) / 2, 0);
        AddSolid(c, "Revolution", mesh, "Revolve");
    }

    private static IEnumerable<Prompt> Boolean(CommandContext c, string op)
    {
        foreach (var p in RequireSelection(c, "Select solids")) yield return p;
        var solids = c.SelectedSolids.ToList();
        if (solids.Count < 2) { c.Print("  Select at least two solids."); yield break; }
        var mesh = solids[0].Mesh;
        foreach (var s in solids.Skip(1)) mesh = op == "Union" ? Csg.Union(mesh, s.Mesh) : Csg.Intersect(mesh, s.Mesh);
        var keep = solids[0];
        c.Doc.Edit(op, d =>
        {
            var ids = solids.Select(s => s.Id).ToHashSet();
            d.Solids.RemoveAll(s => ids.Contains(s.Id));
            d.Solids.Add(WithMesh(keep, mesh));
        });
        c.Engine.Selection.Clear();
        c.Print($"  {op}: {solids.Count} solids → volume {mesh.Volume.ToString("N1", Inv)}");
    }

    private static IEnumerable<Prompt> Subtract(CommandContext c)
    {
        c.Engine.Selection.Clear();
        yield return Prompt.Select("Select solids to subtract from");
        var bases = c.SelectedSolids.ToList();
        if (bases.Count == 0) yield break;
        c.Engine.Selection.Clear();
        yield return Prompt.Select("Select solids to subtract");
        var tools = c.SelectedSolids.Where(t => bases.All(b => b.Id != t.Id)).ToList();
        if (tools.Count == 0) yield break;
        var toolMesh = tools.Count == 1 ? tools[0].Mesh : Csg.UnionAll(tools.Select(t => t.Mesh));
        var results = bases.Select(b => WithMesh(b, Csg.Subtract(b.Mesh, toolMesh))).ToList();
        c.Doc.Edit("Subtract", d =>
        {
            var ids = bases.Concat(tools).Select(s => s.Id).ToHashSet();
            d.Solids.RemoveAll(s => ids.Contains(s.Id));
            d.Solids.AddRange(results);
        });
        c.Engine.Selection.Clear();
        c.Print($"  Subtracted {tools.Count} solid(s).");
    }

    private static IEnumerable<Prompt> Elevate(CommandContext c)
    {
        foreach (var p in RequireSelection(c, "Select solids")) yield return p;
        yield return Prompt.Distance("Specify Z offset", null, 10);
        double dz = c.IsEnd ? 10 : c.Number;
        var ids = c.SelectedSolids.Select(s => s.Id).ToHashSet();
        c.Doc.Edit("Elevate", d => { for (int i = 0; i < d.Solids.Count; i++) if (ids.Contains(d.Solids[i].Id)) d.Solids[i] = WithMesh(d.Solids[i], d.Solids[i].Mesh.Translated(0, 0, dz)); });
    }

    // ===================================================================== inquiry

    private static IEnumerable<Prompt> Dist(CommandContext c)
    {
        yield return Prompt.Point("Specify first point");
        var a = c.Point;
        yield return Prompt.Point("Specify second point", a);
        var d = c.Point - a;
        c.Print($"  Distance = {d.Length.ToString("0.####", Inv)}, angle in XY = {Units.ToDegrees(d.Angle).ToString("0.##", Inv)}°, ΔX = {d.X.ToString("0.####", Inv)}, ΔY = {d.Y.ToString("0.####", Inv)}");
    }

    private static IEnumerable<Prompt> Area(CommandContext c)
    {
        foreach (var p in RequireSelection(c, "Select closed objects")) yield return p;
        foreach (var e in c.SelectedEntities)
            if (Geometry2D.AsLoop(e.Geometry, 0.01) is { } loop)
                c.Print($"  Area = {loop.Area.ToString("N3", Inv)}, perimeter = {loop.Perimeter.ToString("N3", Inv)}");
        foreach (var s in c.SelectedSolids) c.Print($"  {s.Name}: surface area = {s.Mesh.SurfaceArea.ToString("N3", Inv)}");
    }

    private static IEnumerable<Prompt> MassProp(CommandContext c)
    {
        foreach (var p in RequireSelection(c, "Select solids")) yield return p;
        foreach (var s in c.SelectedSolids)
        {
            var p = s.Mesh.Properties; var bb = s.Mesh.Bounds;
            c.Print($"  {s.Name}: volume {p.Volume.ToString("N3", Inv)}, area {p.Area.ToString("N3", Inv)}, centroid {p.Centroid}, bounds {bb.Size}");
            c.Print($"    mass (steel 7.85 g/cm³) ≈ {(p.Volume * 7.85e-3).ToString("N1", Inv)} g · (aluminium 2.70) ≈ {(p.Volume * 2.7e-3).ToString("N1", Inv)} g · (PLA 1.24) ≈ {(p.Volume * 1.24e-3).ToString("N1", Inv)} g");
        }
    }

    private static IEnumerable<Prompt> ListCmd(CommandContext c)
    {
        foreach (var p in RequireSelection(c)) yield return p;
        foreach (var e in c.SelectedEntities)
            c.Print($"  {e.Geometry.GetType().Name.Replace("Entity", "")} on layer \"{e.Layer}\" — {Describe(e.Geometry)}");
        foreach (var s in c.SelectedSolids)
            c.Print($"  Solid \"{s.Name}\" on layer \"{s.Layer}\" — {s.Mesh.TriangleCount:N0} triangles, {s.Color}");
    }

    public static string Describe(DrawingEntity g) => g switch
    {
        LineEntity l => $"from {l.A} to {l.B}, length {l.Length.ToString("0.###", Inv)}",
        CircleEntity c => $"center {c.Center}, radius {c.Radius.ToString("0.###", Inv)}",
        ArcEntity a => $"center {a.Center}, radius {a.Radius.ToString("0.###", Inv)}, sweep {Units.ToDegrees(a.Sweep).ToString("0.#", Inv)}°",
        PolylineEntity p => $"{p.Points.Count} vertices, {(p.Closed ? "closed" : "open")}",
        TextEntity t => $"\"{t.Text}\" height {t.Height.ToString("0.##", Inv)}",
        DimensionEntity d => $"value {d.Label}",
        _ => ""
    };

    // ===================================================================== utilities

    private static IEnumerable<Prompt> LayerCmd(CommandContext c)
    {
        yield return Prompt.Keyword("Enter an option", "New", "Set", "On", "Off", "Lock", "Unlock", "List");
        var opt = c.Keyword;
        if (opt is null or "List")
        {
            foreach (var l in c.Doc.Layers) c.Print($"  {(l.Name == c.Doc.CurrentLayer ? "*" : " ")} {l.Name,-20} {l.Color} {(l.Visible ? "on" : "off")}{(l.Locked ? " locked" : "")}");
            yield break;
        }
        yield return Prompt.Text("Enter layer name");
        var name = c.Text;
        if (string.IsNullOrWhiteSpace(name)) yield break;
        switch (opt)
        {
            case "New":
                yield return Prompt.Text("Enter colour (#RRGGBB) or Enter for default");
                var color = c.Text.StartsWith('#') ? c.Text : "#E6EDF3";
                c.Doc.Edit("New layer", d => { if (d.FindLayer(name) is null) d.Layers.Add(new Layer(name, color)); d.CurrentLayer = name; });
                break;
            case "Set":
                if (c.Doc.FindLayer(name) is null) { c.Print($"  Layer \"{name}\" not found."); yield break; }
                c.Doc.Edit("Set layer", d => d.CurrentLayer = c.Doc.FindLayer(name)!.Name);
                break;
            default:
                c.Doc.Edit("Layer state", d =>
                {
                    int i = d.Layers.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (i < 0) return;
                    d.Layers[i] = opt switch
                    {
                        "On" => d.Layers[i] with { Visible = true },
                        "Off" => d.Layers[i] with { Visible = false },
                        "Lock" => d.Layers[i] with { Locked = true },
                        _ => d.Layers[i] with { Locked = false }
                    };
                });
                break;
        }
        c.Print($"  Layer {name}: {opt.ToLowerInvariant()}.");
    }

    private static IEnumerable<Prompt> ColorCmd(CommandContext c)
    {
        yield return Prompt.Text("Enter colour (#RRGGBB or BYLAYER)");
        var col = c.Text.Trim();
        bool byLayer = col.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase);
        if (!byLayer && !col.StartsWith('#')) { c.Print("  Use #RRGGBB or BYLAYER."); yield break; }
        if (c.HasSelection)
        {
            var ids = c.Engine.Selection.ToHashSet();
            c.Doc.Edit("Colour", d =>
            {
                for (int i = 0; i < d.Entities.Count; i++) if (ids.Contains(d.Entities[i].Id)) d.Entities[i] = d.Entities[i] with { Color = byLayer ? null : col };
                for (int i = 0; i < d.Solids.Count; i++) if (ids.Contains(d.Solids[i].Id) && !byLayer)
                    { var s = d.Solids[i]; d.Solids[i] = new StudioSolid(s.Id, s.Name, s.Layer, s.Mesh, col, s.Metallic, s.Roughness); }
            });
        }
        else c.Settings.CurrentColor = byLayer ? "ByLayer" : col;
        c.Print($"  Colour {(c.HasSelection ? "applied" : "set")}: {col}.");
    }

    private static IEnumerable<Prompt> Ai(CommandContext c)
    {
        yield return Prompt.Text("Describe the part");
        var prompt = c.Text;
        if (string.IsNullOrWhiteSpace(prompt)) yield break;
        yield return Prompt.Point("Specify insertion point", allowEnd: true);
        var at = c.IsEnd ? Vec2.Zero : c.Point;
        TextToCadResult result;
        try { result = new TextToCadAgent(ProviderFactory.FromEnvironment()).GenerateAsync(prompt).GetAwaiter().GetResult(); }
        catch (Exception ex) { c.Print($"  AI failed: {ex.Message}"); yield break; }
        c.Doc.Edit("AI part", d =>
        {
            foreach (var b in result.Model.Bodies)
            {
                var s = d.AddSolid($"{result.Definition.Name}/{b.Name}", b.Mesh.Translated(at.X, at.Y, 0), b.Color.ToHex(), "AI", b.Metallic, b.Roughness);
                c.Engine.LastCreated = s.Id;
            }
        });
        c.Print($"  {result.Definition.Name} by {result.Provider} — {result.Definition.Features.Count} features, {result.Elapsed.TotalSeconds:0.0}s.");
        if (result.Explanation is { } ex2) c.Print($"  {ex2}");
    }

    private static IEnumerable<Prompt> Help(CommandContext c)
    {
        foreach (var g in c.Engine.Commands.GroupBy(x => x.Group).OrderBy(g => g.Key))
        {
            c.Print($"  {g.Key}");
            foreach (var cmd in g.OrderBy(x => x.Name))
                c.Print($"    {cmd.Name,-10} {(cmd.Aliases.Length > 0 ? "(" + string.Join(", ", cmd.Aliases) + ")" : ""),-22} {cmd.Description}");
        }
        yield break;
    }
}

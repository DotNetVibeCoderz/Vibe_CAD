using System.Globalization;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Studio.IO;

namespace Gravicode.CAD.Studio.Commands;

/// <summary>Drafting edits (TRIM, EXTEND, FILLET, CHAMFER), HATCH, blocks and PLOT.</summary>
public static partial class StudioCommands
{
    private static void RegisterDrafting(CommandEngine e)
    {
        void R(string name, string[] aliases, string group, string desc, Func<CommandContext, IEnumerable<Prompt>> run) =>
            e.Register(new CommandInfo(name, aliases, group, desc, run));

        R("TRIM", ["TR"], "Modify", "Trims objects back to the objects they cross (every object is a cutting edge)", TrimCmd);
        R("EXTEND", ["EX"], "Modify", "Extends lines, arcs and open polylines to the next object", ExtendCmd);
        R("FILLET", ["F"], "Modify", "Rounds the corner between two lines, or every corner of a polyline (R sets the radius)", FilletCmd);
        R("CHAMFER", ["CHA"], "Modify", "Bevels the corner between two lines, or every corner of a polyline (D sets distances)", ChamferCmd);
        R("HATCH", ["H", "BHATCH"], "Draw", "Fills closed areas with a pattern (ANSI31, ANSI37) or solid colour", HatchCmd);
        R("BLOCK", ["B"], "Blocks", "Turns selected objects into a named block", BlockCmd);
        R("INSERT", ["I"], "Blocks", "Places a block (scale and rotation)", InsertCmd);
        R("PLOT", ["PRINT"], "Output", "Plots the drawing to a PDF or SVG sheet (A4–A0) with scale and title block", PlotCmd);
    }

    private static readonly CultureInfo InvC = CultureInfo.InvariantCulture;

    private static IEnumerable<DrawingEntity> OthersNear(CommandContext c, StudioEntity target, bool everywhere)
    {
        var (lo, hi) = target.Geometry.Extents();
        foreach (var e in c.Doc.VisibleEntities)
        {
            if (e.Id == target.Id) continue;
            if (!everywhere)
            {
                var (a, b) = e.Geometry.Extents();
                if (b.X < lo.X - 1e-9 || a.X > hi.X + 1e-9 || b.Y < lo.Y - 1e-9 || a.Y > hi.Y + 1e-9) continue;
            }
            yield return e.Geometry;
        }
    }

    private static bool Trimmable(DrawingEntity g) => g is LineEntity or ArcEntity or CircleEntity or PolylineEntity;

    /// <summary>Replaces one entity by zero or more pieces inside the current edit (first piece keeps the id).</summary>
    private static void ReplaceWith(StudioDocument d, StudioEntity src, IReadOnlyList<DrawingEntity> pieces)
    {
        int i = d.Entities.FindIndex(x => x.Id == src.Id);
        if (i < 0) return;
        if (pieces.Count == 0) { d.Entities.RemoveAt(i); return; }
        d.Entities[i] = src with { Geometry = pieces[0] };
        foreach (var p in pieces.Skip(1)) d.Entities.Insert(++i, StudioEntity.Create(src.Layer, p, src.Color));
    }

    // ===================================================================== TRIM / EXTEND

    private static IEnumerable<Prompt> TrimCmd(CommandContext c)
    {
        c.Engine.Selection.Clear();
        while (true)
        {
            yield return Prompt.Pick("Select object to trim", p =>
                c.PickAt(p) is { } t && Trimmable(t.Geometry) && CurveOps.TrimDetailed(t.Geometry, OthersNear(c, t, false), p)?.Removed is { } r ? [r] : []);
            if (c.IsEnd) yield break;
            var pick = c.Point;
            var target = c.PickAt(pick);
            if (target is null || !Trimmable(target.Geometry)) { c.Print("  Pick a line, arc, circle or polyline."); continue; }
            var result = CurveOps.TrimDetailed(target.Geometry, OthersNear(c, target, false), pick);
            if (result is null) { c.Print("  A circle or closed shape needs two crossings to be trimmed."); continue; }
            c.Doc.Edit("Trim", d => ReplaceWith(d, target, result.Kept));
            c.Print(result.Kept.Count == 0 ? "  Nothing crosses it — object erased." : $"  Trimmed ({result.Kept.Count} piece(s) left).");
        }
    }

    private static IEnumerable<Prompt> ExtendCmd(CommandContext c)
    {
        c.Engine.Selection.Clear();
        while (true)
        {
            yield return Prompt.Pick("Select object to extend", p =>
                c.PickAt(p) is { } t && CurveOps.Extend(t.Geometry, OthersNear(c, t, true), p) is { } g ? [g] : []);
            if (c.IsEnd) yield break;
            var target = c.PickAt(c.Point);
            if (target is null) { c.Print("  Pick a line, arc or open polyline near the end to extend."); continue; }
            var g = CurveOps.Extend(target.Geometry, OthersNear(c, target, true), c.Point);
            if (g is null) { c.Print("  Nothing in that direction to extend to."); continue; }
            c.Doc.Edit("Extend", d => ReplaceWith(d, target, [g]));
        }
    }

    // ===================================================================== FILLET / CHAMFER

    private static IEnumerable<Prompt> FilletCmd(CommandContext c) => Corner(c, chamfer: false);
    private static IEnumerable<Prompt> ChamferCmd(CommandContext c) => Corner(c, chamfer: true);

    private static string CornerSettings(CommandContext c, bool chamfer) => chamfer
        ? $"distances {c.Settings.ChamferA.ToString("0.###", InvC)} / {c.Settings.ChamferB.ToString("0.###", InvC)}"
        : $"radius {c.Settings.FilletRadius.ToString("0.###", InvC)}";

    private static CurveOps.CornerResult CornerOf(CommandContext c, bool chamfer, LineEntity l1, Vec2 p1, LineEntity l2, Vec2 p2) => chamfer
        ? CurveOps.Chamfer(l1, p1, l2, p2, c.Settings.ChamferA, c.Settings.ChamferB)
        : CurveOps.Fillet(l1, p1, l2, p2, c.Settings.FilletRadius);

    private static IEnumerable<Prompt> Corner(CommandContext c, bool chamfer)
    {
        c.Engine.Selection.Clear();
        string name = chamfer ? "Chamfer" : "Fillet", option = chamfer ? "Distance" : "Radius";
        c.Print($"  Current settings: {CornerSettings(c, chamfer)}");
        StudioEntity? first;
        Vec2 firstPick;
        while (true)
        {
            yield return Prompt.Pick("Select first line", null, option, "Polyline");
            if (c.IsEnd) yield break;
            if (c.Keyword == option)
            {
                if (chamfer)
                {
                    yield return Prompt.Distance("Specify first chamfer distance", null, c.Settings.ChamferA);
                    if (!c.IsEnd) c.Settings.ChamferA = Math.Max(0, c.Number);
                    yield return Prompt.Distance("Specify second chamfer distance", null, c.Settings.ChamferA);
                    c.Settings.ChamferB = c.IsEnd ? c.Settings.ChamferA : Math.Max(0, c.Number);
                }
                else
                {
                    yield return Prompt.Distance("Specify fillet radius", null, c.Settings.FilletRadius);
                    if (!c.IsEnd) c.Settings.FilletRadius = Math.Max(0, c.Number);
                }
                c.Print($"  {CornerSettings(c, chamfer)}");
                continue;
            }
            if (c.Keyword == "Polyline")
            {
                yield return Prompt.Pick("Select polyline");
                if (c.IsEnd) continue;
                if (c.PickAt(c.Point) is not { Geometry: PolylineEntity pl } src) { c.Print("  That is not a polyline."); continue; }
                double size = chamfer ? c.Settings.ChamferA : c.Settings.FilletRadius;
                var rounded = CurveOps.RoundCorners(pl, size, chamfer);
                c.Doc.Edit($"{name} polyline", d => ReplaceWith(d, src, [rounded]));
                c.Print($"  {pl.Points.Count - (pl.Closed ? 0 : 2)} corner(s) {(chamfer ? "chamfered" : "filleted")}.");
                yield break;
            }
            first = c.PickAt(c.Point);
            firstPick = c.Point;
            if (first?.Geometry is LineEntity) break;
            c.Print("  Pick a line (use the Polyline option for polylines).");
        }
        var l1 = (LineEntity)first.Geometry;
        while (true)
        {
            yield return Prompt.Pick("Select second line", p =>
            {
                if (c.PickAt(p) is not { Geometry: LineEntity l2 } s2 || s2.Id == first.Id) return [];
                try { var r = CornerOf(c, chamfer, l1, firstPick, l2, p); return r.Joint is null ? [r.First, r.Second] : [r.First, r.Second, r.Joint]; }
                catch (InvalidOperationException) { return []; }
            });
            if (c.IsEnd) yield break;
            if (c.PickAt(c.Point) is not { Geometry: LineEntity second } secondEnt || secondEnt.Id == first.Id) { c.Print("  Pick a different line."); continue; }
            CurveOps.CornerResult res;
            try { res = CornerOf(c, chamfer, l1, firstPick, second, c.Point); }
            catch (InvalidOperationException ex) { c.Print($"  {ex.Message}"); continue; }
            c.Doc.Edit(name, d =>
            {
                ReplaceWith(d, first, [res.First]);
                ReplaceWith(d, secondEnt, [res.Second]);
                if (res.Joint is { } j) { var e = d.Add(j, first.Layer, first.Color); c.Engine.LastCreated = e.Id; }
            });
            yield break;
        }
    }

    // ===================================================================== HATCH

    /// <summary>Closed shapes of the drawing as loops (circles, closed polylines, rectangles, polygons).</summary>
    private static List<Polygon2> ClosedLoops(CommandContext c) =>
        [.. c.Doc.VisibleEntities.Select(e => Geometry2D.AsLoop(e.Geometry, 0.05)).OfType<Polygon2>()];

    /// <summary>Boundary of the area around <paramref name="p"/>: the smallest closed shape containing it plus every
    /// closed shape inside that one (even-odd turns them into islands).</summary>
    private static List<IReadOnlyList<Vec2>>? RegionAt(List<Polygon2> loops, Vec2 p)
    {
        var outer = loops.Where(l => l.Contains(p)).OrderBy(l => l.Area).FirstOrDefault();
        if (outer is null) return null;
        var result = new List<IReadOnlyList<Vec2>> { outer.Points };
        foreach (var l in loops)
            if (!ReferenceEquals(l, outer) && l.Area < outer.Area && l.Points.All(outer.Contains)) result.Add(l.Points);
        return result;
    }

    private static IEnumerable<Prompt> HatchCmd(CommandContext c)
    {
        var s = c.Settings;
        c.Engine.Selection.Clear();
        var loops = new List<IReadOnlyList<Vec2>>();
        List<Polygon2>? cached = null;
        c.Print($"  Pattern {s.HatchPattern}, scale {(s.HatchScale > 0 ? s.HatchScale.ToString("0.###", InvC) : "auto")}, angle {s.HatchAngle.ToString("0.#", InvC)}°");
        while (true)
        {
            yield return Prompt.Pick("Pick an internal point", p =>
            {
                cached ??= ClosedLoops(c);
                return RegionAt(cached, p) is { } r ? r.Select(l => (DrawingEntity)new PolylineEntity(l, true)) : [];
            }, "Select", "Pattern", "Scale", "Angle");
            if (c.IsEnd) break;
            switch (c.Keyword)
            {
                case "Pattern":
                    yield return Prompt.Keyword("Enter pattern", HatchEntity.Patterns);
                    if (c.Keyword is { } kw) s.HatchPattern = kw;
                    c.Print($"  Pattern {s.HatchPattern}");
                    continue;
                case "Scale":
                    yield return Prompt.Distance("Specify pattern scale (0 = auto)", null, s.HatchScale);
                    if (!c.IsEnd && c.Number >= 0) s.HatchScale = c.Number;
                    continue;
                case "Angle":
                    yield return Prompt.Distance("Specify pattern angle (degrees)", null, s.HatchAngle);
                    if (!c.IsEnd) s.HatchAngle = c.Number;
                    continue;
                case "Select":
                    yield return Prompt.Select("Select closed objects");
                    foreach (var e in c.SelectedEntities)
                        if (Geometry2D.AsLoop(e.Geometry, 0.05) is { } loop) loops.Add(loop.Points);
                    c.Engine.Selection.Clear();
                    continue;
            }
            cached ??= ClosedLoops(c);
            if (RegionAt(cached, c.Point) is { } region) loops.AddRange(region);
            else c.Print("  No closed boundary around that point.");
        }
        if (loops.Count == 0) yield break;
        double scale = s.HatchScale > 0 ? s.HatchScale : HatchEntity.AutoScale(loops);
        var hatch = new HatchEntity(loops, s.HatchPattern, scale, Units.Deg(s.HatchAngle));
        AddEntity(c, hatch, "Hatch");
        c.Print($"  Hatch {hatch.Pattern}: {loops.Count} boundary loop(s){(hatch.IsSolid ? "" : $", scale {scale.ToString("0.###", InvC)}, {hatch.Lines.Count} lines")}.");
    }

    // ===================================================================== BLOCK / INSERT

    private static IEnumerable<Prompt> BlockCmd(CommandContext c)
    {
        yield return Prompt.Text("Enter block name");
        var name = c.Text.Trim();
        if (name.Length == 0 || name.Contains(' ')) { c.Print("  A block name is one word, e.g. CHAIR or M8_BOLT."); yield break; }
        if (c.Doc.Blocks.ContainsKey(name))
        {
            yield return Prompt.Keyword($"Block \"{name}\" exists. Redefine it?", "Yes", "No");
            if (c.Keyword != "Yes") yield break;
        }
        yield return Prompt.Point("Specify insertion base point");
        var basePt = c.Point;
        foreach (var p in RequireSelection(c, "Select objects for the block")) yield return p;
        var ents = c.SelectedEntities.ToList();
        if (ents.Count == 0) { c.Print("  No drawing objects selected (solids cannot go into blocks)."); yield break; }
        var def = BlockDefinition.From(name, basePt, ents);
        var ids = ents.Select(e => e.Id).ToHashSet();
        c.Doc.Edit("Block", d =>
        {
            d.Blocks[name] = def;
            // existing references follow the new definition
            for (int i = 0; i < d.Entities.Count; i++)
                if (d.Entities[i].Geometry is BlockRefEntity br && br.Block.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    d.Entities[i] = d.Entities[i] with { Geometry = new BlockRefEntity(def, br.Position, br.ScaleX, br.ScaleY, br.Rotation) };
            int at = d.Entities.FindIndex(x => ids.Contains(x.Id));
            d.Entities.RemoveAll(x => ids.Contains(x.Id));
            var reference = StudioEntity.Create(d.CurrentLayer, new BlockRefEntity(def, basePt));
            d.Entities.Insert(Math.Clamp(at, 0, d.Entities.Count), reference);
            c.Engine.LastCreated = reference.Id;
        });
        c.Engine.Selection.Clear();
        c.Print($"  Block {name}: {def.Items.Count} object(s).");
    }

    private static IEnumerable<Prompt> InsertCmd(CommandContext c)
    {
        if (c.Doc.Blocks.Count == 0) { c.Print("  This drawing has no blocks yet — create one with BLOCK."); yield break; }
        yield return Prompt.Text($"Enter block name ({string.Join(", ", c.Doc.Blocks.Keys.Take(12))})");
        var name = c.Text.Trim();
        if (name.Length == 0) name = c.Doc.Blocks.Keys.First();
        if (!c.Doc.Blocks.TryGetValue(name, out var def)) { c.Print($"  Block \"{name}\" not found."); yield break; }
        yield return Prompt.Point("Specify insertion point", null, p => [new BlockRefEntity(def, p)]);
        var at = c.Point;
        yield return Prompt.Distance("Specify scale factor", null, 1);
        double sc = c.IsEnd ? 1 : c.Number;
        if (sc == 0) sc = 1;
        yield return Prompt.Distance("Specify rotation angle", at, 0, p => [new BlockRefEntity(def, at, sc, sc, (p - at).Angle)]);
        double rot = c.IsEnd ? 0 : c.Input is Input.PointInput pt ? (pt.Value - at).Angle : Units.Deg(c.Number);
        AddEntity(c, new BlockRefEntity(def, at, sc, sc, rot), "Insert");
    }

    // ===================================================================== PLOT

    private static IEnumerable<Prompt> PlotCmd(CommandContext c)
    {
        yield return Prompt.Keyword("Paper size <A3>", "A4", "A3", "A2", "A1", "A0");
        var paper = c.Keyword ?? "A3";
        yield return Prompt.Keyword("Orientation <Landscape>", "Landscape", "Portrait");
        bool landscape = c.Keyword != "Portrait";
        yield return Prompt.Keyword("Plot area <Extents>", "Extents", "Window");
        (Vec2, Vec2)? window = null;
        if (c.Keyword == "Window")
        {
            yield return Prompt.Point("Specify first corner of the plot window");
            var a = c.Point;
            yield return Prompt.Point("Specify opposite corner", a, p => [new PolylineEntity([a, new Vec2(p.X, a.Y), p, new Vec2(a.X, p.Y)], true)]);
            window = (a, c.Point);
        }
        yield return Prompt.Text("Plot scale (Fit, 1:50, 2:1…) <Fit>");
        var scale = Plotter.ParseScale(c.Text);
        yield return Prompt.Keyword("Title block <Yes>", "Yes", "No");
        bool title = c.Keyword != "No";
        yield return Prompt.Keyword("Format <PDF>", "PDF", "SVG");
        var fmt = c.Keyword == "SVG" ? "svg" : "pdf";
        PlotResult result;
        try { result = Plotter.Plot(c.Doc, new PlotSettings(paper, landscape, scale, window, title, fmt)); }
        catch (InvalidOperationException ex) { c.Print($"  {ex.Message}"); yield break; }
        c.Print($"  Plotted on {result.Paper} {(landscape ? "landscape" : "portrait")} at {Plotter.FormatScale(result.ScaleDenominator)}.");
        c.Engine.Deliver(result.FileName, result.Data, result.Mime);
    }
}

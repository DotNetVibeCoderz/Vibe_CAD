using System.Globalization;
using Gravicode.CAD.Studio.IO;

namespace Gravicode.CAD.Studio.Commands;

/// <summary>Work planes (sketch on a face) and paper-space layouts.</summary>
public static partial class StudioCommands
{
    private static void RegisterSpaces(CommandEngine e)
    {
        void R(string name, string[] aliases, string group, string desc, Func<CommandContext, IEnumerable<Prompt>> run) =>
            e.Register(new CommandInfo(name, aliases, group, desc, run));

        R("UCS", ["SKETCH"], "View", "Draw on a face: UCS Face (click a face in 3D) | World | List", UcsCmd);
        R("MODEL", ["MS"], "View", "Back to model space (top view of the world)", c => Simple(c, () => c.Engine.SwitchSpace(null)));
        R("LAYOUT", ["LO"], "Output", "Paper-space sheets: LAYOUT New | Set | List | Delete", LayoutCmd);
        R("MVIEW", ["MV"], "Output", "Adds a viewport onto the model to the current layout", MviewCmd);
        R("PARAM", ["PARAMS", "HISTORY"], "Solid", "Shows a solid's parametric history and edits its numbers (the solid rebuilds)", ParamCmd);
    }

    private static readonly double[] NiceScales = [1, 2, 2.5, 5, 10];

    /// <summary>Smallest standard scale 1:N (1, 2, 2.5, 5 × 10ⁿ) at which <paramref name="needed"/> fits.</summary>
    public static double NiceScale(double needed)
    {
        if (!(needed > 0)) return 1;
        double e = Math.Pow(10, Math.Floor(Math.Log10(needed)));
        foreach (var m in NiceScales) if (m * e >= needed - 1e-9) return m * e;
        return 10 * e;
    }

    /// <summary>Scale (1:N) that fits the model extents into a paper window.</summary>
    private static double FitScale(StudioDocument doc, Vec2 min, Vec2 max, out Vec2 center)
    {
        var bb = BoundingBox.Empty;
        foreach (var e in doc.ModelEntities) { var (a, b) = e.Geometry.Extents(); bb = bb.Include(a.ToVec3()).Include(b.ToVec3()); }
        foreach (var s in doc.VisibleSolids) bb = bb.Union(s.Mesh.Bounds);
        if (bb.IsEmpty) { center = Vec2.Zero; return 1; }
        center = bb.Center.XY;
        double unitMm = doc.Units.ToMillimeters();
        double fx = (max.X - min.X) / Math.Max(bb.Size.X, 1e-9), fy = (max.Y - min.Y) / Math.Max(bb.Size.Y, 1e-9);
        return NiceScale(unitMm / Math.Min(fx, fy));
    }

    // ===================================================================== UCS (sketch on a face)

    private static IEnumerable<Prompt> UcsCmd(CommandContext c)
    {
        yield return Prompt.Keyword("Work plane <Face>", "Face", "World", "List");
        switch (c.Keyword ?? "Face")
        {
            case "World":
                c.Engine.SwitchSpace(null);
                yield break;
            case "List":
                if (c.Doc.Planes.Count == 0) c.Print("  No work planes yet.");
                foreach (var p in c.Doc.Planes.Values) c.Print($"  {p.Name}: origin {p.Origin}, normal {p.Normal}");
                yield break;
        }
        if (c.Doc.Solids.Count == 0) { c.Print("  There are no solids to draw on."); yield break; }
        c.RequestView("view-3d");
        yield return Prompt.Face("Click a face in the 3D view");
        if (c.Face is not { } face) yield break;
        var solid = c.Doc.Solids.FirstOrDefault(s => s.Id == face.Solid);
        if (solid is null) yield break;
        int n = c.Doc.Planes.Count + 1;
        while (c.Doc.Planes.ContainsKey($"{Spaces.PlanePrefix}{n}")) n++;
        var plane = WorkPlane.OnFace($"{Spaces.PlanePrefix}{n}", $"Face {n} ({solid.Name})", solid, face.Point, face.Normal);
        c.Doc.Edit("Work plane", d => { d.Planes[plane.Key] = plane; d.ActiveSpace = plane.Key; });
        c.Engine.Selection.Clear();
        c.Print($"  Drawing on {plane.Name}. Draw a closed shape and EXTRUDE it: positive heights add material, negative heights cut. MODEL returns to the plan.");
        c.RequestView("zoom-extents");
    }

    // ===================================================================== parametric history

    private static IEnumerable<Prompt> ParamCmd(CommandContext c)
    {
        var solid = c.SelectedSolids.FirstOrDefault();
        if (solid is null)
        {
            yield return Prompt.Select("Select a solid") with { Single = true };
            solid = c.SelectedSolids.FirstOrDefault();
        }
        if (solid is null) yield break;
        if (solid.History is null) { c.Print("  This solid has no history (imported, AI or sample geometry)."); yield break; }
        while (true)
        {
            var current = c.Doc.Solids.FirstOrDefault(s => s.Id == solid.Id);
            if (current?.History is not { } tree) yield break;
            c.Print($"  {current.Name}: {Features.Describe(tree)}");
            var entries = Features.Parameters(tree);
            for (int i = 0; i < entries.Count; i++) c.Print($"    {i + 1,2}. {new string(' ', entries[i].Depth * 2)}{entries[i].Label} = {Features.Format(entries[i].Value)}");
            yield return Prompt.Integer("Parameter number to change (Enter to finish)", 1) with { AllowEnd = true };
            if (c.IsEnd) yield break;
            int n = (int)c.Number - 1;
            if (n < 0 || n >= entries.Count) { c.Print("  No such parameter."); continue; }
            yield return Prompt.Distance($"New value for {entries[n].Label}", null, entries[n].Value);
            if (c.IsEnd) continue;
            c.Engine.SetParameter(solid.Id, n, c.Number);
        }
    }

    // ===================================================================== layouts

    private static IEnumerable<Prompt> LayoutCmd(CommandContext c)
    {
        yield return Prompt.Keyword("Layout option <New>", "New", "Set", "List", "Delete");
        var opt = c.Keyword ?? "New";
        if (opt == "List")
        {
            if (c.Doc.Layouts.Count == 0) c.Print("  No layouts yet — LAYOUT New makes one.");
            foreach (var l in c.Doc.Layouts.Values) c.Print($"  {l.Name}: {l.Paper} {(l.Landscape ? "landscape" : "portrait")}, {l.Viewports.Count} viewport(s)");
            yield break;
        }
        if (opt is "Set" or "Delete")
        {
            yield return Prompt.Text("Layout name");
            var found = c.Doc.Layouts.Values.FirstOrDefault(l => l.Name.Equals(c.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found is null) { c.Print($"  No layout named \"{c.Text.Trim()}\"."); yield break; }
            if (opt == "Set") { c.Engine.SwitchSpace(found.Key); yield break; }
            c.Doc.Edit("Delete layout", d =>
            {
                d.Layouts.Remove(found.Key);
                d.Entities.RemoveAll(e => e.Space == found.Key);
                if (d.ActiveSpace == found.Key) d.ActiveSpace = null;
            });
            c.Print($"  Layout {found.Name} deleted.");
            yield break;
        }

        yield return Prompt.Text($"Layout name <Layout{c.Doc.Layouts.Count + 1}>");
        var name = c.Text.Trim();
        if (name.Length == 0) name = $"Layout{c.Doc.Layouts.Count + 1}";
        if (c.Doc.Layouts.Values.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { c.Print($"  Layout {name} already exists."); yield break; }
        yield return Prompt.Keyword("Paper size <A3>", "A4", "A3", "A2", "A1", "A0");
        var paper = c.Keyword ?? "A3";
        yield return Prompt.Keyword("Orientation <Landscape>", "Landscape", "Portrait");
        bool landscape = c.Keyword != "Portrait";
        var key = $"{Spaces.LayoutPrefix}{name}";
        var layout = new Layout(key, name, paper, landscape, true, []);
        var (w, h) = layout.Size;
        var (lo, hi) = Plotter.DrawableArea(w, h, true);
        double scale = FitScale(c.Doc, lo, hi, out var center);
        layout = layout with { Viewports = [new LayoutViewport(lo, hi, center, scale)] };
        c.Doc.Edit("New layout", d => { d.Layouts[key] = layout; d.ActiveSpace = key; });
        c.Engine.Selection.Clear();
        c.Print($"  Layout {name}: {paper} {(landscape ? "landscape" : "portrait")}, one viewport at {Plotter.FormatScale(scale)}. MVIEW adds viewports, PLOT prints the sheet, MODEL goes back.");
        c.RequestView("zoom-extents");
    }

    private static IEnumerable<Prompt> MviewCmd(CommandContext c)
    {
        if (c.Doc.ActiveLayout is not { } layout) { c.Print("  MVIEW works in a layout — LAYOUT New or pick a layout tab first."); yield break; }
        yield return Prompt.Point("Specify first corner of the viewport (paper mm)");
        var a = c.Point;
        yield return Prompt.Point("Specify opposite corner", a, p => [new PolylineEntity([a, new Vec2(p.X, a.Y), p, new Vec2(a.X, p.Y)], true)]);
        var b = c.Point;
        var lo = new Vec2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)); var hi = new Vec2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        if (hi.X - lo.X < 5 || hi.Y - lo.Y < 5) { c.Print("  The viewport is too small."); yield break; }
        double fit = FitScale(c.Doc, lo, hi, out var center);
        yield return Prompt.Text($"Viewport scale (1:N) <{Plotter.FormatScale(fit)}>");
        double scale = Plotter.ParseScale(c.Text) ?? fit;
        yield return Prompt.Text($"Model point at the centre x,y <{center.X.ToString("0.##", CultureInfo.InvariantCulture)},{center.Y.ToString("0.##", CultureInfo.InvariantCulture)}>");
        var parts = c.Text.Split(',');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            center = new Vec2(x, y);
        var vp = new LayoutViewport(lo, hi, center, scale);
        c.Doc.Edit("Viewport", d => d.Layouts[layout.Key] = layout with { Viewports = [.. layout.Viewports, vp] });
        c.Print($"  Viewport at {Plotter.FormatScale(scale)}.");
    }
}

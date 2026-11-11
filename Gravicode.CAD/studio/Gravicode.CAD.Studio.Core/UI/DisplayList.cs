namespace Gravicode.CAD.Studio.UI;

/// <summary>A drawable 2D item in world coordinates.</summary>
/// <summary>
/// One drawable for the drafting canvases. Kinds: <c>poly</c>, <c>segs</c>, <c>circle</c>, <c>point</c>, <c>text</c>,
/// <c>fill</c> (even-odd polygon; <see cref="Rings"/> holds the vertex count of each ring) and <c>grip</c>
/// (screen-sized square; <see cref="Hot"/> marks the grip being dragged).
/// </summary>
public sealed record DisplayItem(string Kind, double[] Points, bool Closed, string Color, bool Selected, bool Solid = false, string? Text = null, double Height = 0, string? Id = null,
    int[]? Rings = null, bool Hot = false);

/// <summary>Converts a document (or preview geometry) into flat display items for the drafting canvas.</summary>
public static class DisplayList
{
    public const string SolidOutlineColor = "#7FB8FF";

    /// <summary>Most grips drawn at once; larger selections show none (as in AutoCAD).</summary>
    public const int MaxGripObjects = 100;
    public const string GripColor = "#4F8DF7", HotGripColor = "#FF5A4E";

    /// <summary>Scene for an editor: the document, the selection highlight and — when idle — grips.</summary>
    public static List<DisplayItem> Build(Commands.CommandEngine engine)
    {
        var items = Build(engine.Document, engine.Selection);
        bool gripping = engine.HotGrip is not null;
        if ((engine.IsBusy && !gripping) || engine.Selection.Count is 0 or > MaxGripObjects) return items;
        foreach (var e in engine.Document.Entities)
        {
            if (!engine.Selection.Contains(e.Id)) continue;
            var grips = Grips.Of(e.Geometry);
            for (int i = 0; i < grips.Count; i++)
            {
                bool hot = engine.HotGrip is { } hg && hg.Entity == e.Id && hg.Index == i;
                items.Add(new DisplayItem("grip", [grips[i].X, grips[i].Y], false, hot ? HotGripColor : GripColor, false, Hot: hot, Id: e.Id.ToString()));
            }
        }
        return items;
    }

    public static List<DisplayItem> Build(StudioDocument doc, IReadOnlySet<Guid> selection)
    {
        var items = new List<DisplayItem>(doc.Entities.Count + doc.Solids.Count);
        foreach (var s in doc.VisibleSolids)
        {
            var segs = s.PlanOutline;
            var pts = new double[segs.Count * 4];
            for (int i = 0; i < segs.Count; i++) { pts[i * 4] = segs[i].A.X; pts[i * 4 + 1] = segs[i].A.Y; pts[i * 4 + 2] = segs[i].B.X; pts[i * 4 + 3] = segs[i].B.Y; }
            items.Add(new DisplayItem("segs", pts, false, s.Color, selection.Contains(s.Id), true, Id: s.Id.ToString()));
        }
        foreach (var e in doc.VisibleEntities)
        {
            var color = e.Color ?? doc.LayerOf(e.Layer).Color;
            items.AddRange(Items(e.Geometry, color, selection.Contains(e.Id), e.Id.ToString()));
        }
        return items;
    }

    public static IEnumerable<DisplayItem> Items(DrawingEntity g, string color, bool selected, string? id = null)
    {
        switch (g)
        {
            case HatchEntity h when h.IsSolid:
                {
                    var rings = h.Loops.Select(l => l.Count).ToArray();
                    var pts = h.Loops.SelectMany(l => l.SelectMany(p => new[] { p.X, p.Y })).ToArray();
                    yield return new DisplayItem("fill", pts, true, color, selected, Id: id, Rings: rings);
                    break;
                }
            case HatchEntity h:
                {
                    var lines = h.Lines;
                    var pts = new double[lines.Count * 4];
                    for (int i = 0; i < lines.Count; i++) { pts[i * 4] = lines[i].A.X; pts[i * 4 + 1] = lines[i].A.Y; pts[i * 4 + 2] = lines[i].B.X; pts[i * 4 + 3] = lines[i].B.Y; }
                    yield return new DisplayItem("segs", pts, false, color, selected, Id: id);
                    break;
                }
            case BlockRefEntity b:
                foreach (var c in b.Children())
                    foreach (var it in Items(c.Geometry, c.Color ?? color, selected, id)) yield return it;
                break;
            case TextEntity t:
                yield return new DisplayItem("text", [t.Position.X, t.Position.Y], false, color, selected, Text: t.Text, Height: t.Height, Id: id);
                break;
            case PointEntity p:
                yield return new DisplayItem("point", [p.Position.X, p.Position.Y], false, color, selected, Id: id);
                break;
            case DimensionEntity d:
                {
                    var segs = d.Segments().ToList();
                    var pts = segs.SelectMany(s => new[] { s.A.X, s.A.Y, s.B.X, s.B.Y }).ToArray();
                    yield return new DisplayItem("segs", pts, false, color, selected, Id: id);
                    var dir = d.B - d.A;
                    yield return new DisplayItem("text", [d.TextPosition.X, d.TextPosition.Y, dir.Angle], false, color, selected, Text: d.Label, Height: d.TextHeight, Id: id);
                    break;
                }
            case CircleEntity c:
                yield return new DisplayItem("circle", [c.Center.X, c.Center.Y, c.Radius], true, color, selected, Id: id);
                break;
            default:
                {
                    var pts = Geometry2D.Polyline(g, 0.02 + 0.002 * Size(g));
                    var arr = new double[pts.Count * 2];
                    for (int i = 0; i < pts.Count; i++) { arr[i * 2] = pts[i].X; arr[i * 2 + 1] = pts[i].Y; }
                    yield return new DisplayItem("poly", arr, g is PolylineEntity { Closed: true }, color, selected, Id: id);
                    break;
                }
        }
    }

    private static double Size(DrawingEntity g) { var (a, b) = g.Extents(); return Math.Max(b.X - a.X, b.Y - a.Y); }
}

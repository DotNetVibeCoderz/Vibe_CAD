namespace Gravicode.CAD.Studio.UI;

/// <summary>A drawable 2D item in world coordinates.</summary>
public sealed record DisplayItem(string Kind, double[] Points, bool Closed, string Color, bool Selected, bool Solid = false, string? Text = null, double Height = 0, string? Id = null);

/// <summary>Converts a document (or preview geometry) into flat display items for the drafting canvas.</summary>
public static class DisplayList
{
    public const string SolidOutlineColor = "#7FB8FF";

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

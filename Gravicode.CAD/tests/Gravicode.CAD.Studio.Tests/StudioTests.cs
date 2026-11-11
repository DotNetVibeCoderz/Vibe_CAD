using Gravicode.CAD.Geometry;
using Gravicode.CAD.Studio;
using Gravicode.CAD.Studio.Catalog;
using Gravicode.CAD.Studio.Commands;
using Gravicode.CAD.Studio.IO;

namespace Gravicode.CAD.Studio.Tests;

public class StudioTests
{
    private static (StudioDocument Doc, CommandEngine Engine) New()
    {
        var doc = StudioCatalog.Blank();
        return (doc, new CommandEngine(doc));
    }

    [Fact]
    public void Line_with_relative_and_polar_input()
    {
        var (doc, e) = New();
        e.RunScript("LINE 0,0 @100,0 @50<90 C");
        var lines = doc.Entities.Select(x => x.Geometry).OfType<LineEntity>().ToList();
        Assert.Equal(3, lines.Count);
        Assert.True(lines[1].B.ApproxEquals(new Vec2(100, 50), 1e-9));
        Assert.True(lines[2].B.ApproxEquals(Vec2.Zero, 1e-9)); // closed
    }

    [Fact]
    public void Rectangle_extrude_with_hole_creates_solid()
    {
        var (doc, e) = New();
        e.RunScript("""
            RECTANG 0,0 100,60
            CIRCLE 50,30 10
            EXTRUDE ALL 20
            """);
        var s = Assert.Single(doc.Solids);
        Assert.Equal(100 * 60 * 20 - Polygon2.Circle(10, 0, tolerance: 0.05).Area * 20, s.Mesh.Volume, 0);
        Assert.Empty(doc.Entities);
    }

    [Fact]
    public void Boolean_subtract_and_undo_redo()
    {
        var (doc, e) = New();
        e.RunScript("BOX 0,0 100,100 50");
        e.RunScript("CYLINDER 50,50 20 80");
        var box = doc.Solids[0]; var cyl = doc.Solids[1];
        e.Selection.Clear();
        e.Start("SUBTRACT");
        e.Selection.Add(box.Id); e.Enter();
        e.Selection.Add(cyl.Id); e.Enter();
        var r = Assert.Single(doc.Solids);
        Assert.True(r.Mesh.Volume < 500000 - 60000);
        Assert.True(doc.Undo());
        Assert.Equal(2, doc.Solids.Count);
        Assert.True(doc.Redo());
        Assert.Single(doc.Solids);
    }

    [Fact]
    public void Move_copy_rotate_mirror_array()
    {
        var (doc, e) = New();
        e.RunScript("CIRCLE 0,0 5");
        e.RunScript("MOVE L ; 0,0 100,0");
        Assert.Equal(100, ((CircleEntity)doc.Entities[0].Geometry).Center.X, 9);
        e.RunScript("COPY L ; 0,0 @0,50 ;");
        Assert.Equal(2, doc.Entities.Count);
        e.RunScript("ARRAY L ; P 0,0 4 360");
        Assert.Equal(5, doc.Entities.Count);
        e.RunScript("MIRROR L ; 0,-1 0,1 N");
        Assert.Equal(6, doc.Entities.Count);
    }

    [Fact]
    public void Ortho_snap_and_direct_distance()
    {
        var (doc, e) = New();
        e.RunScript("LINE 0,0 10,0 ;");
        var snap = e.Snap(new Vec2(9.7, 0.2), 1);
        Assert.Equal(SnapKind.Endpoint, snap.Kind);
        Assert.True(snap.Point.ApproxEquals(new Vec2(10, 0), 1e-9));
        e.Settings.Ortho = true;
        e.Start("LINE");
        e.Click(new Vec2(0, 0), 1);
        e.Cursor = new Vec2(30, 3);
        e.Submit("25");               // direct distance along the (ortho) cursor direction
        e.Enter();
        var last = (LineEntity)doc.Entities[^1].Geometry;
        Assert.True(last.B.ApproxEquals(new Vec2(25, 0), 1e-9));
    }

    [Fact]
    public void Offset_dimension_and_inquiry()
    {
        var (doc, e) = New();
        e.RunScript("RECTANG 0,0 40,20");
        e.Start("OFFSET"); e.Submit("5");
        e.Click(new Vec2(20, 0), 1);          // pick rectangle edge
        e.Click(new Vec2(20, -10), 1);        // outside
        e.Escape();
        var outer = doc.Entities.Select(x => x.Geometry).OfType<PolylineEntity>().Last();
        Assert.Equal(50 * 30, new Polygon2(outer.Points).Area, 6);
        e.RunScript("DIMLINEAR 0,0 40,0 20,-10");
        Assert.IsType<DimensionEntity>(doc.Entities[^1].Geometry);
        Assert.Equal("40", ((DimensionEntity)doc.Entities[^1].Geometry).Label);
    }

    [Fact]
    public void Project_file_roundtrip_is_lossless()
    {
        var doc = StudioCatalog.Find("gearbox")!.Create();
        var bytes = ProjectFile.Save(doc);
        var back = ProjectFile.Load(bytes);
        Assert.Equal(doc.Entities.Count, back.Entities.Count);
        Assert.Equal(doc.Solids.Count, back.Solids.Count);
        Assert.Equal(doc.Solids.Sum(s => s.Mesh.Volume), back.Solids.Sum(s => s.Mesh.Volume), 6);
        Assert.Equal(doc.Layers.Count, back.Layers.Count);
    }

    [Fact]
    public void Exports_to_interchange_formats()
    {
        var doc = StudioCatalog.Find("speaker")!.Create();
        foreach (var ext in new[] { "step", "stl", "glb", "dxf", "pdf", "svg", "png" })
            Assert.NotEmpty(ProjectFile.Export(doc, ext));
    }

    [Fact]
    public void Every_template_and_sample_builds()
    {
        foreach (var item in StudioCatalog.Templates.Concat(StudioCatalog.Samples))
        {
            var doc = item.Create();
            Assert.False(string.IsNullOrWhiteSpace(doc.Name));
            if (!item.IsTemplate) Assert.True(doc.Solids.Count > 0, $"{item.Id} has no solids");
            Assert.All(doc.Solids, s => Assert.True(s.Mesh.TriangleCount > 0, $"{item.Id}/{s.Name} empty"));
            Assert.False(doc.CanUndo);
        }
        Assert.True(StudioCatalog.Samples.Select(s => s.Category).Distinct().Count() >= 15);
    }

    [Fact]
    public void Help_lists_commands_and_unknown_command_reports()
    {
        var (_, e) = New();
        e.Submit("HELP");
        Assert.Contains(e.History, l => l.Contains("EXTRUDE"));
        e.Submit("FOOBAR");
        Assert.Contains(e.History, l => l.Contains("Unknown command"));
        Assert.True(e.Commands.Count >= 45);
    }
}

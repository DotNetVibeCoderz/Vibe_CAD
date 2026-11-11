using System.Text;
using Gravicode.CAD.Studio.Catalog;
using Gravicode.CAD.Studio.Commands;
using Gravicode.CAD.Studio.IO;

namespace Gravicode.CAD.Studio.Tests;

/// <summary>TRIM / EXTEND / FILLET / CHAMFER, HATCH, blocks, grips and PLOT.</summary>
public class DraftingTests
{
    private static (StudioDocument Doc, CommandEngine Engine) New()
    {
        var doc = StudioCatalog.Blank();
        return (doc, new CommandEngine(doc));
    }

    private static List<T> All<T>(StudioDocument d) where T : DrawingEntity => [.. d.Entities.Select(x => x.Geometry).OfType<T>()];
    private static void Near(Vec2 expected, Vec2 actual, double tol = 1e-6) =>
        Assert.True(expected.ApproxEquals(actual, tol), $"expected {expected}, got {actual}");

    [Fact]
    public void Trim_removes_the_piece_between_two_crossing_lines()
    {
        var (doc, e) = New();
        e.RunScript("""
            LINE 0,0 100,0 ;
            LINE 30,-10 30,10 ;
            LINE 70,-10 70,10 ;
            TRIM 50,0 ;
            """);
        var horizontal = All<LineEntity>(doc).Where(l => Math.Abs(l.A.Y) < 1e-9 && Math.Abs(l.B.Y) < 1e-9).OrderBy(l => l.A.X).ToList();
        Assert.Equal(2, horizontal.Count);
        Near(new Vec2(0, 0), horizontal[0].A); Near(new Vec2(30, 0), horizontal[0].B);
        Near(new Vec2(70, 0), horizontal[1].A); Near(new Vec2(100, 0), horizontal[1].B);
        Assert.True(doc.Undo());
        Assert.Equal(3, All<LineEntity>(doc).Count);
    }

    [Fact]
    public void Trim_circle_leaves_the_far_arc_and_unbounded_objects_are_erased()
    {
        var (doc, e) = New();
        e.RunScript("""
            CIRCLE 0,0 10
            LINE -20,0 20,0 ;
            TRIM 0,10 ;
            """);
        var arc = Assert.Single(All<ArcEntity>(doc));
        Assert.Equal(Math.PI, arc.Sweep, 6);
        Assert.True(arc.StartPoint.ApproxEquals(new Vec2(-10, 0), 1e-6) && arc.EndPoint.ApproxEquals(new Vec2(10, 0), 1e-6));

        // a lone object has nothing to trim against: quick mode erases it
        var (doc2, e2) = New();
        e2.RunScript("LINE 0,0 10,0 ;\nTRIM 5,0 ;");
        Assert.Empty(doc2.Entities);
    }

    [Fact]
    public void Extend_line_and_arc_to_a_boundary()
    {
        var (doc, e) = New();
        e.RunScript("""
            LINE 0,0 50,0 ;
            LINE 100,-50 100,50 ;
            EXTEND 45,0 ;
            """);
        var l = All<LineEntity>(doc).First(x => Math.Abs(x.A.Y) < 1e-9 && Math.Abs(x.B.Y) < 1e-9);
        Near(new Vec2(100, 0), l.B);

        var a = new ArcEntity(Vec2.Zero, 10, 0, Math.PI / 2);          // quarter arc ending at (0,10)
        var wall = new LineEntity(new Vec2(-10, -20), new Vec2(-10, 20)); // tangent at angle π
        var ext = Assert.IsType<ArcEntity>(CurveOps.Extend(a, [wall], new Vec2(0, 10)));
        Assert.Equal(Math.PI, ext.Sweep, 6);
    }

    [Fact]
    public void Fillet_two_lines_with_radius()
    {
        var (doc, e) = New();
        e.RunScript("""
            LINE 0,0 100,0 ;
            LINE 100,0 100,100 ;
            FILLET R 10 50,0 100,50
            """);
        var arc = Assert.Single(All<ArcEntity>(doc));
        Assert.Equal(10, arc.Radius, 9);
        Near(new Vec2(90, 10), arc.Center);
        var lines = All<LineEntity>(doc);
        Assert.Contains(lines, l => l.A.ApproxEquals(new Vec2(0, 0), 1e-6) && l.B.ApproxEquals(new Vec2(90, 0), 1e-6));
        Assert.Contains(lines, l => l.A.ApproxEquals(new Vec2(100, 100), 1e-6) && l.B.ApproxEquals(new Vec2(100, 10), 1e-6));
        Assert.Equal(10, e.Settings.FilletRadius);                     // remembered for the next run
    }

    [Fact]
    public void Fillet_radius_zero_closes_a_gap_and_chamfer_bevels()
    {
        var (doc, e) = New();
        e.RunScript("""
            LINE 0,0 80,0 ;
            LINE 100,20 100,100 ;
            FILLET R 0 40,0 100,60
            """);
        var lines = All<LineEntity>(doc);
        Assert.Contains(lines, l => l.B.ApproxEquals(new Vec2(100, 0), 1e-6));
        Assert.Equal(2, lines.Count(l => l.B.ApproxEquals(new Vec2(100, 0), 1e-6)));

        var (doc2, e2) = New();
        e2.RunScript("""
            LINE 0,0 100,0 ;
            LINE 100,0 100,100 ;
            CHAMFER D 10 20 50,0 100,50
            """);
        Assert.Contains(All<LineEntity>(doc2), l => l.A.ApproxEquals(new Vec2(90, 0), 1e-6) && l.B.ApproxEquals(new Vec2(100, 20), 1e-6));
    }

    [Fact]
    public void Fillet_polyline_rounds_every_corner()
    {
        var (doc, e) = New();
        e.RunScript("RECTANG 0,0 100,50\nFILLET R 5 P 50,0");
        var pl = Assert.Single(All<PolylineEntity>(doc));
        Assert.True(pl.Closed);
        Assert.True(pl.Points.Count > 12);
        var area = new Geometry.Polygon2(pl.Points).Area;
        // each corner loses r² − πr²/4; the arcs are chords (0.05 tolerance), so a little less area remains
        double exact = 100 * 50 - 4 * (25 - Math.PI * 25 / 4);
        Assert.InRange(area, exact - 1.5, exact + 1e-6);
    }

    [Fact]
    public void Hatch_pick_point_finds_boundary_and_keeps_islands_empty()
    {
        var (doc, e) = New();
        e.RunScript("""
            RECTANG 0,0 100,100
            CIRCLE 50,50 20
            HATCH 10,10 ;
            """);
        var h = Assert.Single(All<HatchEntity>(doc));
        Assert.Equal(2, h.Loops.Count);
        Assert.True(h.Contains(new Vec2(10, 10)));
        Assert.False(h.Contains(new Vec2(50, 50)));                    // island
        Assert.InRange(h.Lines.Count, 20, 200);                         // auto scale: about 40 lines across
        Assert.Equal(1, HatchEntity.AutoScale(h.Loops));
        Assert.Equal(20, HatchEntity.AutoScale([[new(0, 0), new(2000, 0), new(2000, 1000)]]));
        Assert.DoesNotContain(h.Lines, s => Vec2.Distance((s.A + s.B) / 2, new Vec2(50, 50)) < 15 && Vec2.Distance(s.A, s.B) < 1);

        var (doc2, e2) = New();
        e2.RunScript("RECTANG 0,0 10,10\nHATCH P SOLID 5,5 ;");
        var solid = Assert.Single(All<HatchEntity>(doc2));
        Assert.True(solid.IsSolid);
        Assert.Empty(solid.Lines);
    }

    [Fact]
    public void Block_insert_explode_and_project_roundtrip()
    {
        var (doc, e) = New();
        e.RunScript("""
            RECTANG 0,0 10,10
            CIRCLE 5,5 3
            BLOCK CHAIR 0,0 ALL ;
            INSERT CHAIR 100,0 2 90
            """);
        Assert.True(doc.Blocks.ContainsKey("chair"));
        var refs = All<BlockRefEntity>(doc);
        Assert.Equal(2, refs.Count);                                   // the converted original + the insert
        var placed = refs.Single(r => r.Position.ApproxEquals(new Vec2(100, 0), 1e-9));
        var circle = placed.Children().Select(c => c.Geometry).OfType<CircleEntity>().Single();
        Near(new Vec2(90, 10), circle.Center);
        Assert.Equal(6, circle.Radius, 9);

        var copy = ProjectFile.Load(ProjectFile.Save(doc));
        Assert.Single(copy.Blocks);
        Assert.Equal(2, All<BlockRefEntity>(copy).Count);
        Assert.Equal(2, copy.Blocks["CHAIR"].Items.Count);

        e.Selection.Clear();
        e.Selection.Add(doc.Entities.First(x => x.Geometry == placed).Id);
        e.RunScript("EXPLODE");
        Assert.Contains(All<CircleEntity>(doc), c => c.Center.ApproxEquals(new Vec2(90, 10), 1e-9));
        Assert.Single(All<BlockRefEntity>(doc));
    }

    [Fact]
    public void Grip_drag_moves_a_line_endpoint_and_keeps_the_selection()
    {
        var (doc, e) = New();
        e.RunScript("LINE 0,0 100,0 ;");
        var id = doc.Entities[0].Id;
        e.Selection.Add(id);
        e.Click(new Vec2(100, 0), 1);                                  // grab the end grip
        Assert.Equal((id, 1), e.HotGrip);
        e.Click(new Vec2(100, 50), 1);                                 // drop it
        Assert.Null(e.HotGrip);
        Near(new Vec2(100, 50), ((LineEntity)doc.Entities[0].Geometry).B);
        Assert.Contains(id, e.Selection);
    }

    [Fact]
    public void Polyline_grips_skip_tessellated_arc_vertices()
    {
        var rounded = CurveOps.RoundCorners(new PolylineEntity([new(0, 0), new(100, 0), new(100, 50), new(0, 50)], true), 10, chamfer: false);
        Assert.True(rounded.Points.Count > 20);
        Assert.True(Grips.Of(rounded).Count <= 12);
        // moving a grip moves the vertex it stands on
        var g = Grips.Of(rounded);
        var moved = (PolylineEntity)Grips.Move(rounded, 0, g[0] + new Vec2(-5, -5))!;
        Assert.Contains(moved.Points, q => q.ApproxEquals(g[0] + new Vec2(-5, -5), 1e-9));
        Assert.Equal(rounded.Points.Count, moved.Points.Count);
    }

    [Fact]
    public void Plot_pdf_and_svg_with_scale_and_title_block()
    {
        var doc = StudioCatalog.Find("house")!.Create();
        var fit = Plotter.Plot(doc, new PlotSettings("A3"));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(fit.Data, 0, 4));
        Assert.True(fit.ScaleDenominator > 30 && fit.ScaleDenominator < 100);   // 12 m house on A3

        var svg = Plotter.Plot(doc, new PlotSettings("A1", Scale: 50, Format: "svg"));
        Assert.Equal(50, svg.ScaleDenominator, 9);
        var text = Encoding.UTF8.GetString(svg.Data);
        Assert.Contains("<svg", text);
        Assert.Contains("1:50", text);
        Assert.Equal(50, Plotter.ParseScale("1:50"));
        Assert.Equal(0.5, Plotter.ParseScale("2:1"));
        Assert.Null(Plotter.ParseScale("Fit"));

        // the command hands the file to the host
        var (d2, e2) = New();
        e2.RunScript("RECTANG 0,0 200,100");
        string? name = null; byte[]? bytes = null;
        e2.FileProduced += (n, b, _) => { name = n; bytes = b; };
        e2.RunScript("PLOT A4 Landscape Extents 1:5 Yes PDF");
        Assert.EndsWith("-A4.pdf", name);
        Assert.NotNull(bytes);
    }

    [Fact]
    public void Semicolon_is_enter_in_the_interactive_command_line()
    {
        var (doc, e) = New();
        e.Submit("RECTANG 0,0 50,50");
        e.Submit("HATCH 10,10 ;");
        Assert.False(e.IsBusy);
        Assert.Single(All<HatchEntity>(doc));
        e.Submit("LINE 0,0 10,0");
        e.Submit(";");
        Assert.False(e.IsBusy);
    }

    [Fact]
    public void Exports_explode_dimensions_hatches_and_blocks()
    {
        var (doc, e) = New();
        e.RunScript("""
            DIMLINEAR 0,0 100,0 50,20
            RECTANG 200,0 260,40
            HATCH 210,10 ;
            """);
        var model = doc.ToCadModel();
        Assert.DoesNotContain(model.Drawing, g => g is DimensionEntity or HatchEntity or BlockRefEntity);
        Assert.Contains(model.Drawing, g => g is TextEntity t && t.Text == "100");
        var dxf = Encoding.ASCII.GetString(ProjectFile.Export(doc, "dxf"));
        Assert.Contains("\n100\n", dxf.Replace("\r", ""));             // dimension text survives in DXF now
    }
}

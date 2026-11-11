using Gravicode.CAD.CAM;

namespace Gravicode.CAD.Tests;

public class CamTests
{
    [Fact]
    public void Outside_contour_is_offset_by_tool_radius()
    {
        var tp = Milling.Contour(Polygon2.Rectangle(40, 20), Tool.EndMill(6), depth: 6, stepDown: 2);
        var cutXs = tp.Moves.Where(m => m.Kind == MoveKind.Linear).Select(m => m.To.X).ToList();
        Assert.Equal(23, cutXs.Max(), 6);
        Assert.Equal(3, tp.Moves.Where(m => m.Kind == MoveKind.Linear).Select(m => m.To.Z).Distinct().Count());
        Assert.Equal(-6, tp.Moves.Min(m => m.To.Z), 9);
    }

    [Fact]
    public void Pocket_stays_inside_and_avoids_island()
    {
        var region = new Profile2(Polygon2.Rectangle(60, 40), [Polygon2.Circle(8, 48)]);
        var tool = Tool.EndMill(6);
        var tp = Milling.Pocket(region, tool, 3, 3);
        foreach (var m in tp.Moves.Where(m => m.Kind == MoveKind.Linear))
        {
            Assert.InRange(m.To.X, -27 - 1e-6, 27 + 1e-6);
            Assert.InRange(m.To.Y, -17 - 1e-6, 17 + 1e-6);
            Assert.True(m.To.XY.Length >= 8 + 3 - 0.2, $"cut inside island at {m.To}");
        }
        var gcode = GCodeWriter.Write([tp]);
        Assert.Contains("M3 S12000", gcode);
        Assert.EndsWith("M30" + Environment.NewLine, gcode);
    }

    [Fact]
    public void Peck_drilling()
    {
        var tp = Milling.Drill([new(10, 0), new(0, 0), new(20, 0)], Tool.Drill(5), 10, peck: 3);
        Assert.Equal(new Vec2(0, 0), tp.Moves[0].To.XY);
        Assert.True(tp.Moves.Count(m => m.Kind == MoveKind.Linear) >= 12);
    }

    [Fact]
    public void Slicer_produces_loops_and_print_gcode()
    {
        var cube = Shapes.Box(20, 20, 4);
        var layers = Slicer.Slice(cube, 0.2);
        Assert.Equal(20, layers.Count);
        Assert.All(layers, l => Assert.Single(l.Loops));
        Assert.Equal(400, layers[3].Loops[0].Area, 6);
        var ring = Csg.Subtract(Shapes.Cylinder(20, 2, 96), Shapes.Cylinder(10, 6, 96, centered: true));
        var regions = Slicer.Slice(ring, 0.5)[1].Regions;
        Assert.Single(regions);
        Assert.Single(regions[0].Holes);
        var path = Slicer.PrintPath(cube, new PrintSettings { InfillDensity = 0.15 });
        Assert.True(path.Moves.Sum(m => m.Extrude) > 0);
        var g = GCodeWriter.Write([path], GCodeFlavor.Marlin);
        Assert.Contains("M109", g);
    }
}

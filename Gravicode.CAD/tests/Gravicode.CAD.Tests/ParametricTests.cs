using Gravicode.CAD.Constraint;
using Gravicode.CAD.Parametric;

namespace Gravicode.CAD.Tests;

public class ParametricTests
{
    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2 ^ 2", -4)]
    [InlineData("max(1, 7, 3) + min(4, 2)", 9)]
    [InlineData("cosd(60)", 0.5)]
    [InlineData("sqrt(16) / 2", 2)]
    [InlineData("1.5e2", 150)]
    public void Expressions(string expr, double expected) =>
        Assert.Equal(expected, ExpressionEvaluator.Evaluate(expr), 9);

    [Fact]
    public void Parameters_resolve_in_dependency_order_and_detect_cycles()
    {
        var p = new ParameterSet().Set("h", "w / 2").Set("w", 80).Set("t", "h + 1");
        var r = p.Resolve();
        Assert.Equal(41, r["t"]);
        Assert.Throws<ExpressionException>(() => new ParameterSet().Set("a", "b").Set("b", "a").Resolve());
    }

    [Fact]
    public void Builder_plate_with_holes()
    {
        var res = Part.Create("Plate")
            .Param("w", 100).Param("d", 60).Param("t", 10)
            .Box("w", "d", "t")
            .Holes(8, (-35, -20), (35, -20), (-35, 20), (35, 20))
            .Rebuild();
        Assert.True(res.Success, string.Join(";", res.Log.Select(l => l.Message)));
        var v = res.Model.Bodies[0].Mesh.Volume;
        double holeArea = Polygon2.Circle(4, 0, tolerance: 0.05).Area;
        Assert.InRange(v, 60000 - 4 * holeArea * 10 - 50, 60000 - 4 * holeArea * 10 + 50);
    }

    [Fact]
    public void Through_holes_produce_watertight_solid_with_matching_genus()
    {
        var m = Part.Create().Box(150, 90, 10).Holes(8.5, (-65, -35), (65, -35), (65, 35), (-65, 35), (0, -35), (0, 35)).Build();
        var r = new Gravicode.CAD.Topology.HalfEdgeMesh(m.Bodies[0].Mesh).Validate();
        Assert.True(r.Watertight, r.ToString());
        Assert.Equal(6, r.Genus);
    }

    [Fact]
    public void Overrides_change_geometry()
    {
        var part = Part.Create().Param("s", 10).Box("s", "s", "s");
        Assert.Equal(1000, part.Build().ToMesh().Volume, 6);
        Assert.Equal(8000, part.Build(new Dictionary<string, double> { ["s"] = 20 }).ToMesh().Volume, 6);
    }

    [Fact]
    public void Json_roundtrip_and_out_of_order_type()
    {
        var json = """
        {
          "name": "Flange",
          "parameters": { "od": 120, "id": "od/3" },
          "features": [
            { "radius": "od/2", "height": 12, "type": "cylinder" },
            { "type": "hole", "diameter": "id", "z": 20 },
            { "type": "pattern", "kind": "circular", "count": 6,
              "source": { "type": "hole", "diameter": 10, "x": 45, "z": 20 } }
          ]
        }
        """;
        var def = PartDefinition.FromJson(json);
        var res = def.Rebuild();
        Assert.True(res.Success, string.Join(";", res.Log.Select(l => l.Message)));
        var again = PartDefinition.FromJson(def.ToJson());
        Assert.Equal(3, again.Features.Count);
        Assert.IsType<PatternFeature>(again.Features[2]);
        Assert.True(res.Model.ToMesh().Volume < Math.PI * 60 * 60 * 12);
    }

    [Fact]
    public void Gear_and_revolve_features()
    {
        var m = Part.Create().Gear(2, 24, 10, 8).Build();
        Assert.True(m.ToMesh().Volume > 0);
        var v = Part.Create().Revolve([(0, 0), (20, 0), (20, 5), (8, 5), (8, 30), (0, 30)]).Build();
        Assert.True(v.ToMesh().Volume > 0);
    }

    [Fact]
    public void Constraint_solver_rectangle()
    {
        var s = new Sketch();
        var l = s.Polyline((0, 0), (9, 1), (8, 6), (-1, 4));
        s.FixAt(l[0].A, 0, 0);
        s.Horizontal(l[0]).Vertical(l[1]).Horizontal(l[2]).Vertical(l[3]);
        s.Length(l[0], 40).Length(l[1], 25);
        var r = s.Solve();
        Assert.True(r.Success, r.ToString());
        Assert.True(r.FullyConstrained, r.ToString());
        Assert.Equal(40, l[2].Length, 6);
        var prof = s.ToProfiles();
        Assert.Single(prof);
        Assert.Equal(1000, prof[0].Area, 4);
    }

    [Fact]
    public void Constraint_solver_tangent_circle()
    {
        var s = new Sketch();
        var line = s.Line(0, 0, 50, 1);
        var c = s.Circle(20, 12, 4);
        s.FixAt(line.A, 0, 0).Horizontal(line).Length(line, 50).Radius(c, 8).Tangent(line, c);
        s.HorizontalDistance(line.A, c.Center, 20);
        var r = s.Solve();
        Assert.True(r.Success, r.ToString());
        Assert.Equal(8, Math.Abs(c.Center.Y), 6);
    }
}

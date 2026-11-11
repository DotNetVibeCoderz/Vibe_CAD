namespace Gravicode.CAD.Tests;

public class GeometryTests
{
    private static void AssertNear(double expected, double actual, double relTol)
        => Assert.True(Math.Abs(expected - actual) <= relTol * Math.Abs(expected), $"expected {expected}, got {actual}");

    [Fact]
    public void Box_volume_and_area()
    {
        var m = Shapes.Box(10, 20, 30);
        AssertNear(6000, m.Volume, 1e-12);
        AssertNear(2 * (200 + 300 + 600), m.SurfaceArea, 1e-12);
        Assert.Equal(12, m.TriangleCount);
    }

    [Fact]
    public void Cylinder_volume_converges()
    {
        var m = Shapes.Cylinder(5, 10, 256);
        AssertNear(Math.PI * 25 * 10, m.Volume, 1e-3);
    }

    [Fact]
    public void Sphere_and_torus_are_positive()
    {
        AssertNear(4.0 / 3 * Math.PI * 1000, Shapes.Sphere(10, 128, 64).Volume, 5e-3);
        AssertNear(2 * Math.PI * Math.PI * 20 * 9, Shapes.Torus(20, 3, 128, 64).Volume, 5e-3);
    }

    [Fact]
    public void Cone_volume()
    {
        AssertNear(Math.PI * 25 * 12 / 3, Shapes.Cone(5, 0, 12, 256).Volume, 1e-3);
    }

    [Fact]
    public void Extrude_profile_with_hole()
    {
        var p = new Profile2(Polygon2.Rectangle(40, 30), [Polygon2.Circle(5, 64)]);
        var m = Shapes.Extrude(p, 10);
        var expected = (1200 - Polygon2.Circle(5, 64).Area) * 10;
        AssertNear(expected, m.Volume, 1e-9);
    }

    [Fact]
    public void Triangulate_concave_polygon_area_matches()
    {
        var star = Polygon2.Star(5, 10, 4);
        var (v, idx) = Triangulator.Triangulate(star);
        double a = 0;
        for (int i = 0; i < idx.Count; i += 3) a += Vec2.Cross(v[idx[i + 1]] - v[idx[i]], v[idx[i + 2]] - v[idx[i]]) / 2;
        AssertNear(star.Area, a, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Triangulate_collinear_outline_with_hole_any_rotation(int quarterTurns)
    {
        // rectangle with many collinear vertices (typical of CAD loops) and a circular hole aligned with them
        var outer = new List<Vec2>();
        for (int i = 0; i < 30; i++) outer.Add(new(-30 + 2 * i, -20));
        for (int i = 0; i < 20; i++) outer.Add(new(30, -20 + 2 * i));
        for (int i = 0; i < 30; i++) outer.Add(new(30 - 2 * i, 20));
        for (int i = 0; i < 20; i++) outer.Add(new(-30, 20 - 2 * i));
        double rot = quarterTurns * Math.PI / 2;
        var prof = new Profile2(new Polygon2(outer.Select(p => p.Rotate(rot))), [Polygon2.Circle(6, 48).Transformed(p => p.Rotate(rot))]);
        var (v, idx) = Triangulator.Triangulate(prof);
        double a = 0;
        for (int i = 0; i < idx.Count; i += 3) a += Vec2.Cross(v[idx[i + 1]] - v[idx[i]], v[idx[i + 2]] - v[idx[i]]) / 2;
        AssertNear(prof.Area, a, 1e-9);
    }

    [Fact]
    public void Revolve_rectangle_gives_tube()
    {
        // ring: r from 5 to 8, z from 0 to 10
        var m = Shapes.Revolve([new(5, 0), new(8, 0), new(8, 10), new(5, 10)], 360, 256);
        AssertNear(Math.PI * (64 - 25) * 10, m.Volume, 1e-3);
    }

    [Fact]
    public void Partial_revolve_is_closed_positive()
    {
        var m = Shapes.Revolve([new(5, 0), new(8, 0), new(8, 10), new(5, 10)], 90, 64);
        AssertNear(Math.PI * (64 - 25) * 10 / 4, m.Volume, 1e-3);
    }

    [Fact]
    public void Csg_subtract_box_minus_box()
    {
        var a = Shapes.Box(10, 10, 10);
        var b = Shapes.Box(10, 10, 10).Translated(5, 0, 0);
        AssertNear(500, Csg.Subtract(a, b).Volume, 1e-6);
        AssertNear(500, Csg.Intersect(a, b).Volume, 1e-6);
        AssertNear(1500, Csg.Union(a, b).Volume, 1e-6);
    }

    [Fact]
    public void Csg_drilled_plate()
    {
        var plate = Shapes.Box(100, 60, 10);
        var hole = Shapes.Cylinder(5, 20, 64, centered: true);
        var r = Csg.Subtract(plate, hole);
        var holeArea = Polygon2.Circle(5, 64).Area;
        AssertNear(60000 - holeArea * 10, r.Volume, 1e-6);
    }

    [Fact]
    public void Sweep_circle_along_line_is_cylinder()
    {
        var m = Shapes.Sweep(Polygon2.Circle(2, 128), new Polyline3Curve([Vec3.Zero, new Vec3(0, 0, 10)]));
        AssertNear(Math.PI * 4 * 10, m.Volume, 1e-3);
    }

    [Fact]
    public void Loft_square_to_square()
    {
        var m = Shapes.Loft(Polygon2.Rectangle(10, 10), Polygon2.Rectangle(10, 10), 5);
        AssertNear(500, m.Volume, 1e-9);
    }

    [Fact]
    public void Gear_profile_is_valid()
    {
        var g = Shapes.SpurGearProfile(2, 20);
        Assert.True(g.IsCounterClockwise);
        // area between root circle and addendum circle
        double rp = 20;
        Assert.InRange(g.Area, Math.PI * (rp - 2.5) * (rp - 2.5), Math.PI * (rp + 2) * (rp + 2));
        var m = Shapes.SpurGear(2, 20, 8, 10);
        Assert.True(m.Volume > 0);
    }

    [Fact]
    public void Nurbs_circle_is_exact()
    {
        var c = NurbsCurve.Circle(Vec3.Zero, 7);
        for (double t = 0; t <= 1; t += 0.05) Assert.Equal(7, c.PointAt(t).Length, 9);
    }

    [Fact]
    public void Offset_square_grows()
    {
        var o = Polygon2.Rectangle(10, 10).Offset(1);
        Assert.Equal(144, o.Area, 9);
    }

    [Fact]
    public void Mat4_inverse_roundtrip()
    {
        var m = Mat4.Translate(1, 2, 3) * Mat4.RotateAxis(new Vec3(1, 1, 0), 0.7) * Mat4.Scale(2, 3, 4);
        var p = new Vec3(3, -2, 5);
        Assert.True(m.Inverse().TransformPoint(m.TransformPoint(p)).ApproxEquals(p, 1e-9));
    }

    [Fact]
    public void Slice_box_gives_four_segments()
    {
        var segs = Shapes.Box(10, 10, 10).SliceZ(0.1);
        Assert.Equal(8, segs.Count); // two triangles per side face
    }
}

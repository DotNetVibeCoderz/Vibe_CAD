using Gravicode.CAD.FEM;
using Gravicode.CAD.Rendering;

namespace Gravicode.CAD.Tests;

public class FemTests
{
    [Fact]
    public void Axial_bar_matches_PL_over_EA()
    {
        var t = new Truss();
        int a = t.AddNode(0, 0), b = t.AddNode(1000, 0);
        t.AddMember(a, b, 100).Support(a).Support(b, x: false).Load(b, new Vec3(10_000, 0, 0));
        var r = t.Solve();
        Assert.Equal(10_000.0 * 1000 / (210_000 * 100), r.Displacements[b].X, 9);
        Assert.Equal(100, r.Stresses[0], 6);
        Assert.Equal(-10_000, r.Reactions[a].X, 6);
    }

    [Fact]
    public void Warren_truss_is_in_equilibrium()
    {
        var t = new Truss();
        var bottom = Enumerable.Range(0, 5).Select(i => t.AddNode(i * 1000, 0)).ToArray();
        var top = Enumerable.Range(0, 4).Select(i => t.AddNode(500 + i * 1000, 800)).ToArray();
        for (int i = 0; i < 4; i++) { t.AddMember(bottom[i], bottom[i + 1], 400); t.AddMember(bottom[i], top[i], 300); t.AddMember(top[i], bottom[i + 1], 300); }
        for (int i = 0; i < 3; i++) t.AddMember(top[i], top[i + 1], 400);
        t.Support(bottom[0]).Support(bottom[4], x: false);
        foreach (var n in top) t.Load(n, new Vec3(0, -5000, 0));
        var r = t.Solve();
        double ry = r.Reactions.Sum(v => v.Y);
        Assert.Equal(20_000, ry, 3);
        Assert.True(r.Displacements[bottom[2]].Y < 0);
        var model = t.ToModel(r);
        Assert.True(model.Bodies.Count > 10);
    }

    [Fact]
    public void Cantilever_plane_stress_close_to_beam_theory()
    {
        double L = 100, h = 10, th = 1, P = 100;
        var mesh = TriMesh2D.Rectangle(L, h, 80, 8);
        var m = new PlaneStressModel(mesh, Material.Steel, th)
            .FixWhere(p => p.X < 1e-9)
            .LoadWhere(p => Math.Abs(p.X - L) < 1e-9, new Vec2(0, -P));
        var r = m.Solve();
        double I = th * h * h * h / 12;
        double expected = P * L * L * L / (3 * Material.Steel.YoungsModulus * I);
        double tip = -r.Displacements.Where((_, i) => Math.Abs(mesh.Nodes[i].X - L) < 1e-9).Average(d => d.Y);
        Assert.InRange(tip / expected, 0.85, 1.1);
        // bending stress at root ≈ 6PL/(t h²) = 600 MPa
        Assert.InRange(r.MaxVonMises, 400, 800);
        var model = m.ToModel(r, displacementScale: 5);
        Assert.NotEmpty(model.Bodies);
    }

    [Fact]
    public void Profile_mesher_and_heat_conduction()
    {
        var mesh = TriMesh2D.FromProfile(new Profile2(Polygon2.Rectangle(100, 20, centered: false)), 4);
        Assert.True(mesh.Elements.Count > 50);
        var heat = new HeatConduction2D(mesh, 0.2)
            .Temperature(p => p.X < 1e-6, 20)
            .Temperature(p => p.X > 100 - 1e-6, 120);
        var T = heat.Solve();
        for (int i = 0; i < mesh.Nodes.Count; i++)
            Assert.Equal(20 + mesh.Nodes[i].X, T[i], 6);
    }

    [Fact]
    public void Software_renderer_writes_png()
    {
        var img = SoftwareRenderer.Render(new CadModel("c", Shapes.Cube(10)), new RenderOptions { Width = 64, Height = 48, Supersample = 1 });
        var png = img.EncodePng();
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal(64, img.Width);
    }
}

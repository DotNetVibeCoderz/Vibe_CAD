using Gravicode.CAD.Studio.Catalog;
using Gravicode.CAD.Studio.Commands;

namespace Gravicode.CAD.Studio.Tests;

/// <summary>3D picking, gizmo moves/rotations and the 3DMOVE / 3DROTATE commands.</summary>
public class Solid3DTests
{
    private static (StudioDocument Doc, CommandEngine Engine) TwoBoxes()
    {
        var doc = StudioCatalog.Blank();
        var e = new CommandEngine(doc);
        e.RunScript("BOX 0,0 100,100 50");          // z 0..50
        e.RunScript("BOX 0,0 40,40 120");           // overlapping, taller: z 0..120
        e.Selection.Clear();
        return (doc, e);
    }

    [Fact]
    public void Raycast_hits_the_nearest_solid()
    {
        var (doc, _) = TwoBoxes();
        // straight down over the tall box: the tall box's top (z=120) is hit before the low box (z=50)
        var hit = Solid3D.Raycast(doc, new Vec3(20, 20, 500), new Vec3(0, 0, -1));
        Assert.NotNull(hit);
        Assert.Equal(doc.Solids[1].Id, hit.Value.Id);
        Assert.Equal(120, hit.Value.Point.Z, 6);
        // over the low box only
        Assert.Equal(doc.Solids[0].Id, Solid3D.Raycast(doc, new Vec3(80, 80, 500), new Vec3(0, 0, -1))!.Value.Id);
        // missing everything
        Assert.Null(Solid3D.Raycast(doc, new Vec3(500, 500, 500), new Vec3(0, 0, -1)));
        // hidden layers are not pickable
        doc.Layers[doc.Layers.FindIndex(l => l.Name == "Solids")] = doc.LayerOf("Solids") with { Visible = false };
        Assert.Null(Solid3D.Raycast(doc, new Vec3(20, 20, 500), new Vec3(0, 0, -1)));
    }

    [Fact]
    public void Pick3D_selects_toggles_and_clears()
    {
        var (doc, e) = TwoBoxes();
        e.Pick3D(new Vec3(80, 80, 500), new Vec3(0, 0, -1));
        Assert.Equal([doc.Solids[0].Id], e.Selection);
        e.Pick3D(new Vec3(20, 20, 500), new Vec3(0, 0, -1), additive: true);
        Assert.Equal(2, e.Selection.Count);
        e.Pick3D(new Vec3(20, 20, 500), new Vec3(0, 0, -1), additive: true);   // shift-click again removes
        Assert.Single(e.Selection);
        e.Pick3D(new Vec3(900, 900, 500), new Vec3(0, 0, -1));                 // empty space clears
        Assert.Empty(e.Selection);
    }

    [Fact]
    public void Gizmo_move_and_rotate_are_undoable_and_keep_the_selection()
    {
        var (doc, e) = TwoBoxes();
        var id = doc.Solids[0].Id;
        e.Selection.Add(id);
        e.Move3D(new Vec3(10, 0, 5));
        Assert.Equal(new Vec3(10, 0, 5), doc.Solids[0].Mesh.Bounds.Min);
        Assert.Contains(id, e.Selection);

        // 90° about Z through the box centre keeps a square footprint where it was
        var before = doc.Solids[0].Mesh.Bounds;
        e.Rotate3D(Vec3.UnitZ, Math.PI / 2);
        var after = doc.Solids[0].Mesh.Bounds;
        Assert.True(before.Min.ApproxEquals(after.Min, 1e-6) && before.Max.ApproxEquals(after.Max, 1e-6));

        // 90° about X stands the 100×100×50 box up: Y and Z extents swap
        e.Rotate3D(Vec3.UnitX, Math.PI / 2);
        var up = doc.Solids[0].Mesh.Bounds.Size;
        Assert.Equal(50, up.Y, 6); Assert.Equal(100, up.Z, 6);
        Assert.Equal(before.Size.X * before.Size.Y * before.Size.Z, up.X * up.Y * up.Z, 3);

        Assert.True(doc.Undo()); Assert.True(doc.Undo()); Assert.True(doc.Undo());
        Assert.Equal(Vec3.Zero, doc.Solids[0].Mesh.Bounds.Min);
    }

    [Fact]
    public void Commands_3dmove_and_3drotate()
    {
        var (doc, e) = TwoBoxes();
        e.Selection.Add(doc.Solids[1].Id);
        e.RunScript("3DMOVE 0,0,30");
        Assert.Equal(30, doc.Solids[1].Mesh.Bounds.Min.Z, 9);
        e.Selection.Add(doc.Solids[1].Id);
        e.RunScript("3DROTATE Y 90");
        var size = doc.Solids[1].Mesh.Bounds.Size;
        Assert.Equal(120, size.X, 6); Assert.Equal(40, size.Z, 6);
    }

    [Fact]
    public void Viewer_order_skips_empty_and_hidden_solids()
    {
        var (doc, _) = TwoBoxes();
        doc.Solids.Add(StudioSolid.Create("empty", "Solids", new Mesh()));
        Assert.Equal(2, doc.ViewerSolids().Count);
        Assert.Equal(2, doc.ToCadModel(includeDrawing: false).Bodies.Count);
        int k = doc.SolidsKey();
        doc.Add(new LineEntity(Vec2.Zero, new Vec2(1, 1)));
        Assert.Equal(k, doc.SolidsKey());           // drawing edits don't force a 3D rebuild
    }
}

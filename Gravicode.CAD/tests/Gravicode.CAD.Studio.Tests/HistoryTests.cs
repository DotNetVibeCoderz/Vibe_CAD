using Gravicode.CAD.Studio.Catalog;
using Gravicode.CAD.Studio.Commands;
using Gravicode.CAD.Studio.IO;

namespace Gravicode.CAD.Studio.Tests;

/// <summary>Parametric history: solids remember their features and rebuild when a number changes.</summary>
public class HistoryTests
{
    private static (StudioDocument Doc, CommandEngine Engine) New()
    {
        var doc = StudioCatalog.Blank();
        return (doc, new CommandEngine(doc));
    }

    private static int Index(Feature tree, string label) =>
        Features.Parameters(tree).FindIndex(p => p.Label == label);

    [Fact]
    public void Subtracted_hole_radius_can_be_edited_later()
    {
        var (doc, e) = New();
        e.RunScript("BOX 0,0 100,100 20");
        e.RunScript("CYLINDER 50,50 10 40");
        e.Selection.Clear();
        e.Start("SUBTRACT");
        e.Selection.Add(doc.Solids[0].Id); e.Enter();
        e.Selection.Add(doc.Solids[1].Id); e.Enter();
        var s = Assert.Single(doc.Solids);
        Assert.Equal("Subtract(Box, Cylinder)", Features.Describe(s.History!));
        double v10 = s.Mesh.Volume;

        int r = Index(s.History!, "Subtract › Cylinder · radius");
        Assert.True(r >= 0);
        Assert.True(e.SetParameter(s.Id, r, 20));
        double v20 = doc.Solids[0].Mesh.Volume;
        // the hole area grows from π·10² to π·20² (polygonal circles: within 1 %)
        Assert.InRange(v10 - v20, Math.PI * 300 * 20 * 0.99, Math.PI * 300 * 20 * 1.01);

        // the box height too
        Assert.True(e.SetParameter(s.Id, Index(doc.Solids[0].History!, "Subtract › Box · height"), 30));
        Assert.Equal(30, doc.Solids[0].Mesh.Bounds.Size.Z, 6);

        Assert.True(doc.Undo()); Assert.True(doc.Undo());
        Assert.Equal(v10, doc.Solids[0].Mesh.Volume, 3);
    }

    [Fact]
    public void Edits_keep_moves_and_rotations()
    {
        var (doc, e) = New();
        e.RunScript("BOX 0,0 40,20 10");
        var id = doc.Solids[0].Id;
        e.Selection.Add(id);
        e.Move3D(new Vec3(100, 0, 0));
        e.Rotate3D(Vec3.UnitZ, Math.PI / 2);
        Assert.Equal("Placement(Box)", Features.Describe(doc.Solids[0].History!));   // merged into one placement
        var centre = doc.Solids[0].Mesh.Bounds.Center;
        Assert.True(e.SetParameter(id, Index(doc.Solids[0].History!, "Box · height"), 50));
        var bb = doc.Solids[0].Mesh.Bounds;
        Assert.Equal(50, bb.Size.Z, 6);
        Assert.Equal(centre.X, bb.Center.X, 6);                                         // still where it was moved to
        Assert.Equal(20, bb.Size.X, 6);                                                // still rotated (40×20 → 20×40)
    }

    [Fact]
    public void Extrude_and_face_cut_heights_are_parameters()
    {
        var (doc, e) = New();
        e.RunScript("RECTANG 0,0 100,60\nCIRCLE 50,30 10\nEXTRUDE ALL 20");
        var s = doc.Solids[0];
        Assert.True(e.SetParameter(s.Id, Index(s.History!, "Extrude · height"), 40));
        Assert.Equal(40, doc.Solids[0].Mesh.Bounds.Size.Z, 6);

        // pocket cut from the top face, then made deeper
        e.Start("UCS"); e.Submit("Face");
        e.Pick3D(new Vec3(20, 20, 500), new Vec3(0, 0, -1));
        e.RunScript("RECTANG 10,10 30,50\nEXTRUDE L -10");
        e.SetSpace(null);
        var cut = doc.Solids[0];
        Assert.Equal("Subtract(Extrude, Placement(Extrude))", Features.Describe(cut.History!));
        double before = cut.Mesh.Volume;
        int depth = Features.Parameters(cut.History!).FindLastIndex(p => p.Name == "height");
        Assert.True(e.SetParameter(cut.Id, depth, -25));
        Assert.Equal(before - 20 * 40 * 15, doc.Solids[0].Mesh.Volume, 0);
    }

    [Fact]
    public void History_survives_save_and_load_and_revolve_rebuilds_identically()
    {
        var (doc, e) = New();
        e.RunScript("PLINE 10,0 30,0 30,40 10,40 C\nREVOLVE L 360");
        var s = doc.Solids[0];
        Assert.Equal(s.Mesh.Volume, Features.Evaluate(s.History!).Mesh_Volume(), 6);
        e.RunScript("GEAR 200,0 2 20 10 6");

        var copy = ProjectFile.Load(ProjectFile.Save(doc));
        Assert.Equal(Features.Describe(s.History!), Features.Describe(copy.Solids[0].History!));
        var e2 = new CommandEngine(copy);
        var gear = copy.Solids[1];
        Assert.True(e2.SetParameter(gear.Id, Index(gear.History!, "Gear · teeth"), 30));
        Assert.True(copy.Solids[1].Mesh.Bounds.Size.X > gear.Mesh.Bounds.Size.X);       // more teeth, bigger gear
        Assert.False(e2.SetParameter(Guid.NewGuid(), 0, 1));
    }

    [Fact]
    public void Param_command_lists_and_edits()
    {
        var (doc, e) = New();
        e.RunScript("CYLINDER 0,0 10 30");
        e.Selection.Add(doc.Solids[0].Id);
        e.RunScript("PARAM 4 15 ;");                                                    // 4 = radius
        Assert.Equal(30, doc.Solids[0].Mesh.Bounds.Size.X, 3);
        Assert.Contains(e.History, l => l.Contains("Cylinder · radius = 15"));
    }
}

internal static class MeshExt
{
    public static double Mesh_Volume(this Mesh m) => m.Volume;
}

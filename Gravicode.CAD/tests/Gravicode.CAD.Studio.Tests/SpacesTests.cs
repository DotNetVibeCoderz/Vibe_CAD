using System.Text;
using Gravicode.CAD.Studio.Catalog;
using Gravicode.CAD.Studio.Commands;
using Gravicode.CAD.Studio.IO;

namespace Gravicode.CAD.Studio.Tests;

/// <summary>Sketching on faces (work planes) and paper-space layouts.</summary>
public class SpacesTests
{
    private static (StudioDocument Doc, CommandEngine Engine) Box()
    {
        var doc = StudioCatalog.Blank();
        var e = new CommandEngine(doc);
        e.RunScript("BOX 0,0 100,100 50");
        e.Selection.Clear();
        return (doc, e);
    }

    private static void PickFace(CommandEngine e, Vec3 from, Vec3 dir)
    {
        e.Start("UCS");
        e.Submit("Face");
        Assert.Equal(PromptKind.Face, e.CurrentPrompt?.Kind);
        e.Pick3D(from, dir);
    }

    [Fact]
    public void Sketch_on_top_face_and_cut_a_pocket()
    {
        var (doc, e) = Box();
        double v0 = doc.Solids[0].Mesh.Volume;
        PickFace(e, new Vec3(30, 40, 500), new Vec3(0, 0, -1));
        var plane = Assert.IsType<WorkPlane>(doc.ActivePlane);
        Assert.Equal(new Vec3(0, 0, 1), plane.Normal);
        Assert.True(plane.Origin.ApproxEquals(new Vec3(0, 0, 50), 1e-6));   // lower-left corner of the face

        e.RunScript("CIRCLE 50,50 10");
        var circle = Assert.Single(doc.Entities);
        Assert.Equal(plane.Key, circle.Space);
        Assert.Single(doc.VisibleEntities);
        Assert.NotEmpty(e.ReferenceGeometry());                               // the face outline is shown

        e.RunScript("EXTRUDE L -20");
        var s = Assert.Single(doc.Solids);
        Assert.InRange(v0 - s.Mesh.Volume, Math.PI * 100 * 20 * 0.98, Math.PI * 100 * 20 * 1.001);
        Assert.Empty(doc.Entities);

        e.RunScript("MODEL");
        Assert.Null(doc.ActiveSpace);
    }

    [Fact]
    public void Sketch_on_side_face_adds_a_boss_and_objects_stay_in_their_space()
    {
        var (doc, e) = Box();
        PickFace(e, new Vec3(500, 50, 25), new Vec3(-1, 0, 0));
        var plane = doc.ActivePlane!;
        Assert.Equal(new Vec3(1, 0, 0), plane.Normal);
        Assert.Equal(new Vec3(0, 0, 1), plane.YAxis);                         // "up" stays up on a wall
        e.RunScript("RECTANG 30,10 70,40\nEXTRUDE L 15");
        var bb = doc.Solids[0].Mesh.Bounds;
        Assert.Equal(115, bb.Max.X, 6);
        Assert.Equal(100 * 100 * 50 + 40 * 30 * 15, doc.Solids[0].Mesh.Volume, 0);

        // a line drawn on the plane is not part of model space (exports, plan view)
        e.RunScript("LINE 0,0 10,10 ;");
        Assert.Single(doc.Entities, x => x.Space == plane.Key);
        e.SetSpace(null);
        Assert.Empty(doc.VisibleEntities);
        Assert.Empty(doc.ToCadModel().Drawing);
        Assert.Single(doc.ToCadModel().Wires);                                // but it shows in 3D on the face
    }

    [Fact]
    public void Layout_viewports_plot_and_roundtrip()
    {
        var doc = StudioCatalog.Find("house")!.Create();
        var e = new CommandEngine(doc);
        e.RunScript("LAYOUT New Sheet1 A3 Landscape");
        var layout = Assert.IsType<Layout>(doc.ActiveLayout);
        var vp = Assert.Single(layout.Viewports);
        Assert.Contains(vp.Scale, new[] { 50.0, 100 });                       // a standard scale that fits a 12 m house
        var refs = e.ReferenceGeometry();
        Assert.Contains(refs, r => r.Geometry.Layer == "VPORT");
        Assert.True(refs.Count > 50);                                         // the plan seen through the viewport

        e.RunScript("TEXT 30,280 5 NOTES");                                   // paper annotation
        Assert.Equal(layout.Key, doc.Entities[^1].Space);
        e.RunScript("MVIEW 300,200 400,280 1:200 6000,4000");
        Assert.Equal(2, doc.ActiveLayout!.Viewports.Count);
        Assert.Equal(200, doc.ActiveLayout.Viewports[1].Scale, 9);

        var pdf = Plotter.PlotLayout(doc, doc.ActiveLayout);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.Data, 0, 4));
        Assert.Equal(0, pdf.ScaleDenominator);                                // two scales: "AS SHOWN"
        var svg = Encoding.UTF8.GetString(Plotter.PlotLayout(doc, doc.ActiveLayout, "svg").Data);
        Assert.Contains("NOTES", svg);
        Assert.Contains("AS SHOWN", svg);

        var copy = ProjectFile.Load(ProjectFile.Save(doc));
        Assert.Equal(doc.ActiveSpace, copy.ActiveSpace);
        Assert.Equal(2, copy.ActiveLayout!.Viewports.Count);
        Assert.Contains(copy.Entities, x => x.Space == layout.Key && x.Geometry is TextEntity);

        Assert.True(doc.Undo());                                              // MVIEW undone
        Assert.Single(doc.ActiveLayout!.Viewports);
    }

    [Fact]
    public void Nice_scales()
    {
        Assert.Equal(50, StudioCommands.NiceScale(41));
        Assert.Equal(100, StudioCommands.NiceScale(51));
        Assert.Equal(2.5, StudioCommands.NiceScale(2.2));
        Assert.Equal(1, StudioCommands.NiceScale(1));
    }
}

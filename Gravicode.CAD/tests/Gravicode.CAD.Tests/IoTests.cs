using Gravicode.CAD.IO;
using Gravicode.CAD.Topology;

namespace Gravicode.CAD.Tests;

public class IoTests
{
    private static CadModel Sample()
    {
        var plate = Csg.Subtract(Shapes.Box(60, 40, 10), Shapes.Cylinder(6, 30, 48, centered: true));
        var m = new CadModel("Plate");
        m.AddBody("Plate", plate, CadColor.Orange);
        return m;
    }

    private static CadModel RoundTrip(CadModel m, string ext)
    {
        CadIO.RegisterAll();
        var bytes = m.SaveToBytes(ext);
        Assert.NotEmpty(bytes);
        using var ms = new MemoryStream(bytes);
        return CadModel.Load(ms, ext);
    }

    [Theory]
    [InlineData("stl")]
    [InlineData("obj")]
    [InlineData("glb")]
    [InlineData("gltf")]
    [InlineData("3mf")]
    [InlineData("step")]
    public void Mesh_formats_preserve_volume(string ext)
    {
        var src = Sample();
        var back = RoundTrip(src, ext);
        Assert.NotEmpty(back.Bodies);
        double v0 = src.ToMesh().Volume, v1 = back.ToMesh().Volume;
        Assert.True(Math.Abs(v0 - v1) / v0 < 1e-4, $"{ext}: {v0} vs {v1}");
    }

    [Fact]
    public void Ascii_stl_roundtrip()
    {
        CadIO.RegisterAll();
        var m = Sample();
        using var ms = new MemoryStream();
        m.Save(ms, "stl", new ExportOptions { Binary = false });
        ms.Position = 0;
        var back = CadModel.Load(ms, "stl");
        Assert.Equal(m.ToMesh().TriangleCount, back.ToMesh().TriangleCount);
    }

    [Fact]
    public void Step_export_merges_planar_faces_and_keeps_color()
    {
        var src = new CadModel("Cube", Shapes.Box(10, 10, 10), CadColor.Blueprint);
        var text = System.Text.Encoding.UTF8.GetString(src.SaveToBytes("step"));
        Assert.Contains("FACETED_BREP", text);
        Assert.Equal(6, text.Split("FACE_OUTER_BOUND").Length - 1);
        var back = RoundTrip(src, "step");
        Assert.Equal(CadColor.Blueprint.ToHex(), back.Bodies[0].Color.ToHex());
        Assert.True(new HalfEdgeMesh(back.Bodies[0].Mesh).IsClosedManifold);
    }

    [Fact]
    public void Step_reads_advanced_brep_with_cylinder()
    {
        // Minimal AP214 cylinder (r=5, h=10) written in the advanced B-Rep style CAD systems use.
        var step = """
ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('test'),'2;1');
FILE_NAME('cyl.stp','2026-01-01',(''),(''),'','','');
FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));
ENDSEC;
DATA;
#1=CARTESIAN_POINT('',(0.,0.,0.));
#2=DIRECTION('',(0.,0.,1.));
#3=DIRECTION('',(1.,0.,0.));
#4=AXIS2_PLACEMENT_3D('',#1,#2,#3);
#5=CYLINDRICAL_SURFACE('',#4,5.);
#6=CARTESIAN_POINT('',(5.,0.,0.));
#7=CARTESIAN_POINT('',(5.,0.,10.));
#8=VERTEX_POINT('',#6);
#9=VERTEX_POINT('',#7);
#10=CIRCLE('',#4,5.);
#11=CARTESIAN_POINT('',(0.,0.,10.));
#12=AXIS2_PLACEMENT_3D('',#11,#2,#3);
#13=CIRCLE('',#12,5.);
#14=EDGE_CURVE('',#8,#8,#10,.T.);
#15=EDGE_CURVE('',#9,#9,#13,.T.);
#16=DIRECTION('',(0.,0.,1.));
#17=VECTOR('',#16,1.);
#18=LINE('',#6,#17);
#19=EDGE_CURVE('',#8,#9,#18,.T.);
#20=ORIENTED_EDGE('',*,*,#14,.T.);
#21=ORIENTED_EDGE('',*,*,#19,.T.);
#22=ORIENTED_EDGE('',*,*,#15,.F.);
#23=ORIENTED_EDGE('',*,*,#19,.F.);
#24=EDGE_LOOP('',(#20,#21,#22,#23));
#25=FACE_OUTER_BOUND('',#24,.T.);
#26=ADVANCED_FACE('',(#25),#5,.T.);
#27=PLANE('',#4);
#28=ORIENTED_EDGE('',*,*,#14,.F.);
#29=EDGE_LOOP('',(#28));
#30=FACE_OUTER_BOUND('',#29,.T.);
#31=ADVANCED_FACE('',(#30),#27,.F.);
#32=PLANE('',#12);
#33=ORIENTED_EDGE('',*,*,#15,.T.);
#34=EDGE_LOOP('',(#33));
#35=FACE_OUTER_BOUND('',#34,.T.);
#36=ADVANCED_FACE('',(#35),#32,.T.);
#37=CLOSED_SHELL('',(#26,#31,#36));
#38=MANIFOLD_SOLID_BREP('Cylinder',#37);
ENDSEC;
END-ISO-10303-21;
""";
        CadIO.RegisterAll();
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(step));
        var m = CadModel.Load(ms, "step");
        Assert.Single(m.Bodies);
        Assert.True(Math.Abs(m.Bodies[0].Mesh.Volume - Math.PI * 250) / (Math.PI * 250) < 0.01, $"volume {m.Bodies[0].Mesh.Volume}");
    }

    [Fact]
    public void Dxf_roundtrip_entities()
    {
        var m = new CadModel("Sketch");
        m.Drawing.Add(new LineEntity(new(0, 0), new(100, 0)));
        m.Drawing.Add(new CircleEntity(new(50, 50), 20));
        m.Drawing.Add(new ArcEntity(new(0, 0), 10, 0, Math.PI / 2));
        m.Drawing.Add(new PolylineEntity([new(0, 0), new(10, 0), new(10, 10)], true));
        var back = RoundTrip(m, "dxf");
        Assert.Equal(4, back.Drawing.Count);
        var c = Assert.IsType<CircleEntity>(back.Drawing[1]);
        Assert.Equal(20, c.Radius, 9);
        var a = Assert.IsType<ArcEntity>(back.Drawing[2]);
        Assert.Equal(Math.PI / 2, a.EndAngle, 6);
    }

    [Fact]
    public void Dxf_lwpolyline_bulge_is_arc()
    {
        var dxf = "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\n0\n90\n2\n70\n0\n10\n0\n20\n0\n42\n1\n10\n10\n20\n0\n0\nENDSEC\n0\nEOF\n";
        CadIO.RegisterAll();
        var m = CadModel.Load(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(dxf)), "dxf");
        var pl = Assert.IsType<PolylineEntity>(m.Drawing[0]);
        // bulge 1 = half circle of radius 5 -> max |y| = 5
        Assert.Equal(5, pl.Points.Max(p => Math.Abs(p.Y)), 1);
    }

    [Fact]
    public void Svg_path_parsing()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg"><path d="M 0 0 L 10 0 L 10 10 Z"/><circle cx="5" cy="5" r="2"/><path d="M0,0 A5,5 0 0 1 10,0"/></svg>""";
        CadIO.RegisterAll();
        var m = CadModel.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg)), "svg");
        Assert.Equal(3, m.Drawing.Count);
        Assert.True(((PolylineEntity)m.Drawing[0]).Closed);
    }

    [Fact]
    public void Pdf_and_svg_drawings_are_written()
    {
        CadIO.RegisterAll();
        var pdf = Sample().SaveToBytes("pdf");
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(pdf, 0, 8));
        var svg = System.Text.Encoding.UTF8.GetString(Sample().SaveToBytes("svg"));
        Assert.Contains("<line", svg);
        Assert.Contains("Gravicode", svg);
    }

    [Fact]
    public void Iges_roundtrip_wires()
    {
        var m = new CadModel("W");
        m.Wires.Add(new Polyline3([new(0, 0, 0), new(10, 0, 5), new(10, 10, 5)]));
        m.Drawing.Add(new LineEntity(new(0, 0), new(5, 5)));
        var back = RoundTrip(m, "igs");
        Assert.Single(back.Wires);
        Assert.Equal(3, back.Wires[0].Points.Count);
        Assert.Single(back.Drawing);
    }

    [Fact]
    public void Dwg_gives_guidance()
    {
        CadIO.RegisterAll();
        var ex = Assert.Throws<NotSupportedException>(() => CadModel.Load(new MemoryStream([1, 2, 3]), "dwg"));
        Assert.Contains("DXF", ex.Message);
    }

    [Fact]
    public void Topology_reports_genus_of_drilled_plate()
    {
        var plate = Csg.Subtract(Shapes.Box(60, 40, 10), Shapes.Cylinder(6, 30, 48, centered: true));
        var r = new HalfEdgeMesh(plate).Validate();
        Assert.True(r.Volume > 0);
        Assert.True(r.Watertight, r.ToString());
        Assert.Equal(1, r.Genus);
        var brep = BrepSolid.FromMesh(Shapes.Box(10, 20, 30));
        Assert.Equal(6, brep.PlanarFaceCount);
        Assert.True(brep.SelectFace(Vec3.UnitZ).Normal.ApproxEquals(Vec3.UnitZ, 1e-9));
    }
}

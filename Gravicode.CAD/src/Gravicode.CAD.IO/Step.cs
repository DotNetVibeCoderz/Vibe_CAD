using System.Text;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Topology;
using static Gravicode.CAD.IO.CadIO;
using P21 = Gravicode.CAD.IO.Part21File;

namespace Gravicode.CAD.IO;

/// <summary>
/// STEP AP203/AP214/AP242 (Part 21).
/// <para>Import: MANIFOLD_SOLID_BREP, BREP_WITH_VOIDS, FACETED_BREP and SHELL_BASED_SURFACE_MODEL; faces on PLANE,
/// CYLINDRICAL_SURFACE and CONICAL_SURFACE are tessellated exactly from their edge loops (LINE, CIRCLE, ELLIPSE,
/// B_SPLINE_CURVE edges); other surfaces fall back to loop fans with a warning. Colors from STYLED_ITEM.</para>
/// <para>Export: AP214 FACETED_BREP with merged planar faces (holes as inner bounds), per-body colors and units.</para>
/// </summary>
public sealed class StepFormat : ICadImporter, ICadExporter
{
    public string FormatName => "STEP";
    public IReadOnlyList<string> Extensions => ["step", "stp", "p21"];

    // ============================== export ==============================
    public void Export(CadModel model, Stream stream, ExportOptions options)
    {
        var sb = new StringBuilder(1 << 20);
        int next = 1;
        int E(string body) { int id = next++; sb.Append('#').Append(id).Append('=').Append(body).Append(";\n"); return id; }
        string S(string s) => "'" + s.Replace("'", "''") + "'";
        string R(double v) { var t = v.ToString("0.0#########E+0", Inv); return t; }

        var header = new StringBuilder();
        header.Append("ISO-10303-21;\nHEADER;\n");
        header.Append($"FILE_DESCRIPTION(({S($"{model.Name} - {GravicodeCad.Credits}")}),'2;1');\n");
        header.Append($"FILE_NAME({S(model.Name + ".step")},{S(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"))},({S(options.Author)}),({S(options.Organization)}),{S(GravicodeCad.ProductName + " " + GravicodeCad.Version)},{S(GravicodeCad.ProductName)},'');\n");
        header.Append("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));\nENDSEC;\nDATA;\n");

        int appCtx = E("APPLICATION_CONTEXT('core data for automotive mechanical design processes')");
        E($"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{appCtx})");
        int prodCtx = E($"PRODUCT_CONTEXT('',#{appCtx},'mechanical')");
        int product = E($"PRODUCT({S(model.Name)},{S(model.Name)},{S(GravicodeCad.Credits)},(#{prodCtx}))");
        E($"PRODUCT_RELATED_PRODUCT_CATEGORY('part',$,(#{product}))");
        int pdf = E($"PRODUCT_DEFINITION_FORMATION('','',#{product})");
        int pdc = E($"PRODUCT_DEFINITION_CONTEXT('part definition',#{appCtx},'design')");
        int pd = E($"PRODUCT_DEFINITION('design','',#{pdf},#{pdc})");
        int pds = E($"PRODUCT_DEFINITION_SHAPE('','',#{pd})");
        string prefix = model.Units switch { LengthUnit.Meter => "$", LengthUnit.Centimeter => ".CENTI.", LengthUnit.Micrometer => ".MICRO.", _ => ".MILLI." };
        int lenUnit;
        if (model.Units is LengthUnit.Inch or LengthUnit.Foot)
        {
            int mm = E("(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))");
            int lmu = E($"LENGTH_MEASURE_WITH_UNIT(LENGTH_MEASURE({R(model.Units.ToMillimeters())}),#{mm})");
            int dim = E("DIMENSIONAL_EXPONENTS(1.,0.,0.,0.,0.,0.,0.)");
            lenUnit = E($"(CONVERSION_BASED_UNIT({S(model.Units == LengthUnit.Inch ? "INCH" : "FOOT")},#{lmu})LENGTH_UNIT()NAMED_UNIT(#{dim}))");
        }
        else lenUnit = E($"(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT({prefix},.METRE.))");
        int angUnit = E("(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.))");
        int solUnit = E("(NAMED_UNIT(*)SI_UNIT($,.STERADIAN.)SOLID_ANGLE_UNIT())");
        int unc = E($"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-07),#{lenUnit},'distance_accuracy_value','confusion accuracy')");
        int ctx = E($"(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{unc}))GLOBAL_UNIT_ASSIGNED_CONTEXT((#{lenUnit},#{angUnit},#{solUnit}))REPRESENTATION_CONTEXT('Context #1','3D Context with UNIT and UNCERTAINTY'))");
        int origin = E("CARTESIAN_POINT('',(0.,0.,0.))");
        int dz = E("DIRECTION('',(0.,0.,1.))");
        int dx = E("DIRECTION('',(1.,0.,0.))");
        int axis = E($"AXIS2_PLACEMENT_3D('',#{origin},#{dz},#{dx})");

        var breps = new List<int>(); var styled = new List<int>();
        foreach (var body in model.Bodies.Where(b => !b.Mesh.IsEmpty))
        {
            var brep = BrepSolid.FromMesh(body.Mesh);
            var m = brep.Mesh;
            var pointIds = new int[m.VertexCount];
            for (int v = 0; v < m.VertexCount; v++) { var p = m.Positions[v]; pointIds[v] = E($"CARTESIAN_POINT('',({R(p.X)},{R(p.Y)},{R(p.Z)}))"); }
            var faces = new List<int>();
            int Loop(IEnumerable<int> verts) => E($"POLY_LOOP('',({string.Join(',', verts.Select(v => "#" + pointIds[v]))}))");
            foreach (var f in brep.Faces)
            {
                if (f.Kind == SurfaceKind.Planar && f.Loops.Count > 0 && f.Loops.All(l => l.Count >= 3))
                {
                    var bounds = new List<int> { E($"FACE_OUTER_BOUND('',#{Loop(f.Loops[0])},.T.)") };
                    for (int li = 1; li < f.Loops.Count; li++) bounds.Add(E($"FACE_BOUND('',#{Loop(f.Loops[li])},.T.)"));
                    faces.Add(E($"FACE('',({string.Join(',', bounds.Select(b => "#" + b))}))"));
                }
                else
                {
                    foreach (var t in f.Triangles)
                    {
                        int lp = Loop([m.Indices[t * 3], m.Indices[t * 3 + 1], m.Indices[t * 3 + 2]]);
                        faces.Add(E($"FACE('',(#{E($"FACE_OUTER_BOUND('',#{lp},.T.)")}))"));
                    }
                }
            }
            int shell = E($"CLOSED_SHELL('',({string.Join(',', faces.Select(x => "#" + x))}))");
            int fb = E($"FACETED_BREP({S(body.Name)},#{shell})");
            breps.Add(fb);
            var c = body.Color;
            int rgb = E($"COLOUR_RGB('',{R(c.R)},{R(c.G)},{R(c.B)})");
            int fasc = E($"FILL_AREA_STYLE_COLOUR('',#{rgb})");
            int fas = E($"FILL_AREA_STYLE('',(#{fasc}))");
            int ssfa = E($"SURFACE_STYLE_FILL_AREA(#{fas})");
            int sss = E($"SURFACE_SIDE_STYLE('',(#{ssfa}))");
            int ssu = E($"SURFACE_STYLE_USAGE(.BOTH.,#{sss})");
            int psa = E($"PRESENTATION_STYLE_ASSIGNMENT((#{ssu}))");
            styled.Add(E($"STYLED_ITEM('color',(#{psa}),#{fb})"));
        }
        int rep = E($"FACETED_BREP_SHAPE_REPRESENTATION({S(model.Name)},(#{axis}{string.Concat(breps.Select(b => ",#" + b))}),#{ctx})");
        E($"SHAPE_DEFINITION_REPRESENTATION(#{pds},#{rep})");
        if (styled.Count > 0) E($"MECHANICAL_DESIGN_GEOMETRIC_PRESENTATION_REPRESENTATION('',({string.Join(',', styled.Select(x => "#" + x))}),#{ctx})");
        sb.Append("ENDSEC;\nEND-ISO-10303-21;\n");

        var bytes = Encoding.UTF8.GetBytes(header.ToString() + sb);
        stream.Write(bytes);
    }

    // ============================== import ==============================
    public CadModel Import(Stream stream, ImportOptions options)
    {
        string text;
        using (var r = new StreamReader(stream, Encoding.UTF8, true, 1 << 16, leaveOpen: true)) text = r.ReadToEnd();
        var f = P21.Parse(text);
        var model = new CadModel();
        if (!string.IsNullOrWhiteSpace(f.FileName)) model.Name = Path.GetFileNameWithoutExtension(f.FileName);
        if (f.OfType("PRODUCT").FirstOrDefault() is { } prod && prod.Args.ElementAtOrDefault(0) is P21.Str pn && pn.Text.Length > 0) model.Name = pn.Text;
        model.Units = DetectUnits(f);
        model.Metadata["step.schema"] = string.Join(", ", f.Schemas);

        var colors = new Dictionary<int, CadColor>();
        foreach (var si in f.OfType("STYLED_ITEM"))
            if (si.Args.ElementAtOrDefault(2) is P21.Ref target && FindColor(f, si, 0) is { } col) colors[target.Id] = col;
        if (f.OfType("NEXT_ASSEMBLY_USAGE_OCCURRENCE").Any())
            model.Warnings.Add("Assembly placements (NAUO / mapped items) are not applied yet; parts are imported in their own coordinates.");

        var tess = new StepTessellator(f, options.ChordTolerance, model.Warnings);
        var solids = f.Entities.Values.Where(e => e.Type is "MANIFOLD_SOLID_BREP" or "FACETED_BREP" or "BREP_WITH_VOIDS" or "SHELL_BASED_SURFACE_MODEL").ToList();
        var usedShells = new HashSet<int>();
        foreach (var s in solids)
        {
            var shells = new List<P21.Entity>();
            foreach (var a in s.Args.Skip(1))
            {
                if (a is P21.Ref r && f[r.Id] is { } e) shells.Add(e);
                else if (a is P21.List l) shells.AddRange(l.Items.OfType<P21.Ref>().Select(x => f[x.Id]).OfType<P21.Entity>());
            }
            var mesh = new Mesh();
            foreach (var sh in shells) { usedShells.Add(sh.Id); mesh.Append(tess.Shell(sh)); }
            if (mesh.IsEmpty) continue;
            mesh = mesh.Welded(1e-6);
            if (s.Type != "SHELL_BASED_SURFACE_MODEL" && mesh.Volume < 0) mesh = mesh.Flipped();
            var name = s.Args.ElementAtOrDefault(0) is P21.Str n && n.Text.Length > 0 ? n.Text : $"Solid{model.Bodies.Count + 1}";
            var body = model.AddBody(name, mesh);
            if (colors.TryGetValue(s.Id, out var c)) body.Color = c;
            else if (shells.Count > 0 && colors.TryGetValue(shells[0].Id, out c)) body.Color = c;
        }
        if (model.Bodies.Count == 0)
            foreach (var sh in f.Entities.Values.Where(e => e.Type is "CLOSED_SHELL" or "OPEN_SHELL" && !usedShells.Contains(e.Id)))
            {
                var m = tess.Shell(sh).Welded(1e-6);
                if (!m.IsEmpty) model.AddBody($"Shell{sh.Id}", m);
            }
        return model;
    }

    private static LengthUnit DetectUnits(P21 f)
    {
        foreach (var e in f.Entities.Values.Where(e => e.Is("LENGTH_UNIT")))
        {
            if (e.Part("CONVERSION_BASED_UNIT") is { } cb && cb.ElementAtOrDefault(0) is P21.Str n)
                return n.Text.ToUpperInvariant().Contains("INCH") ? LengthUnit.Inch : n.Text.ToUpperInvariant().Contains("FOOT") ? LengthUnit.Foot : LengthUnit.Millimeter;
            if (e.Part("SI_UNIT") is { } si)
                return si.ElementAtOrDefault(0) is P21.Enum pre ? pre.Name switch { "MILLI" => LengthUnit.Millimeter, "CENTI" => LengthUnit.Centimeter, "MICRO" => LengthUnit.Micrometer, _ => LengthUnit.Millimeter } : LengthUnit.Meter;
        }
        return LengthUnit.Millimeter;
    }

    private static CadColor? FindColor(P21 f, P21.Entity e, int depth)
    {
        if (depth > 12) return null;
        if (e.Type == "COLOUR_RGB" && e.Args.Count >= 4 && e.Args[1] is P21.Num r && e.Args[2] is P21.Num g && e.Args[3] is P21.Num b) return new CadColor(r.V, g.V, b.V);
        foreach (var a in e.Args.Skip(e.Type == "STYLED_ITEM" ? 1 : 0).Take(e.Type == "STYLED_ITEM" ? 1 : int.MaxValue))
            foreach (var child in Refs(f, a))
                if (FindColor(f, child, depth + 1) is { } c) return c;
        return null;
    }

    private static IEnumerable<P21.Entity> Refs(P21 f, P21.Value v)
    {
        if (v is P21.Ref r && f[r.Id] is { } e) yield return e;
        else if (v is P21.List l) foreach (var i in l.Items) foreach (var x in Refs(f, i)) yield return x;
        else if (v is P21.Typed t) foreach (var i in t.Args) foreach (var x in Refs(f, i)) yield return x;
    }
}

/// <summary>Turns STEP topology (shells → faces → loops → edges) into triangles.</summary>
internal sealed class StepTessellator(P21 f, double tol, List<string> warnings)
{
    private readonly HashSet<string> _warned = [];

    private void Warn(string msg) { if (_warned.Add(msg)) warnings.Add(msg); }

    public Mesh Shell(P21.Entity shell)
    {
        var mesh = new Mesh();
        if (shell.Args.ElementAtOrDefault(1) is not P21.List faces) return mesh;
        foreach (var fr in faces.Items.OfType<P21.Ref>())
        {
            var face = f[fr.Id];
            if (face is null) continue;
            try { Face(face, mesh); }
            catch (Exception ex) { Warn($"Face #{face.Id} ({face.Type}) skipped: {ex.Message}"); }
        }
        return mesh;
    }

    private Vec3 Point(P21.Value? v)
    {
        var e = f.Get(v) ?? throw new InvalidDataException("Missing point.");
        if (e.Type == "VERTEX_POINT") return Point(e.Args[1]);
        var coords = (P21.List)e.Args[1];
        double C(int i) => i < coords.Items.Count && coords.Items[i] is P21.Num n ? n.V : 0;
        return new(C(0), C(1), C(2));
    }

    private Vec3 Dir(P21.Value? v)
    {
        var e = f.Get(v);
        if (e is null) return Vec3.UnitZ;
        if (e.Type == "VECTOR") return Dir(e.Args[1]);
        var l = (P21.List)e.Args[1];
        return new Vec3(N(l, 0), N(l, 1), l.Items.Count > 2 ? N(l, 2) : 0).Normalized();
        static double N(P21.List l, int i) => l.Items[i] is P21.Num n ? n.V : 0;
    }

    /// <summary>Placement → (origin, x, y, z).</summary>
    private (Vec3 O, Vec3 X, Vec3 Y, Vec3 Z) Frame(P21.Value? v)
    {
        var e = f.Get(v) ?? throw new InvalidDataException("Missing placement.");
        var o = Point(e.Args[1]);
        var z = e.Args.Count > 2 && e.Args[2] is P21.Ref ? Dir(e.Args[2]) : Vec3.UnitZ;
        var xr = e.Args.Count > 3 && e.Args[3] is P21.Ref ? Dir(e.Args[3]) : z.AnyPerpendicular();
        var x = (xr - z * Vec3.Dot(xr, z)).Normalized();
        if (x.LengthSquared < 0.5) x = z.AnyPerpendicular();
        return (o, x, Vec3.Cross(z, x), z);
    }

    private void Face(P21.Entity face, Mesh mesh)
    {
        // FACE(name, bounds) | ADVANCED_FACE(name, bounds, surface, same_sense) | FACE_SURFACE(...)
        var bounds = ((P21.List)face.Args[1]).Items.OfType<P21.Ref>().Select(r => f[r.Id]!).ToList();
        P21.Entity? surface = face.Args.Count > 2 ? f.Get(face.Args[2]) : null;
        bool sameSense = face.Args.Count <= 3 || face.Args[3] is not P21.Enum { Name: "F" };

        var loops = new List<List<Vec3>>();
        P21.Entity? outer = null;
        foreach (var b in bounds)
        {
            var loopEnt = f.Get(b.Args[1])!;
            bool orient = b.Args.Count <= 2 || b.Args[2] is not P21.Enum { Name: "F" };
            var pts = Loop(loopEnt);
            if (!orient) pts.Reverse();
            if (pts.Count < 3) continue;
            if (b.Type == "FACE_OUTER_BOUND") { outer = b; loops.Insert(0, pts); } else loops.Add(pts);
        }
        if (loops.Count == 0) return;

        string kind = surface?.Type ?? "PLANE";
        switch (kind)
        {
            case "PLANE":
                {
                    var n = surface != null ? Frame(surface.Args[1]).Z : Newell(loops[0]);
                    if (!sameSense) n = -n;
                    // make sure the outer loop is the largest when not flagged
                    if (outer is null) loops.Sort((a, b) => Area2(b, n).CompareTo(Area2(a, n)));
                    TriangulatePlanar(loops, n, mesh);
                    break;
                }
            case "CYLINDRICAL_SURFACE":
            case "CONICAL_SURFACE":
                {
                    var fr = Frame(surface!.Args[1]);
                    double r0 = surface.Args[2] is P21.Num rn ? rn.V : 1;
                    double semi = kind == "CONICAL_SURFACE" && surface.Args.Count > 3 && surface.Args[3] is P21.Num sa ? sa.V : 0;
                    if (Math.Abs(semi) > 2 * Math.PI) semi = Units.Deg(semi); // some writers use degrees
                    TriangulateRevolved(loops, fr, r0, Math.Tan(semi), sameSense, mesh);
                    break;
                }
            default:
                Warn($"Surface type {kind} is approximated from its boundary loops.");
                var nn = Newell(loops[0]);
                TriangulatePlanar(loops, sameSense ? nn : -nn, mesh, allowNonPlanar: true);
                break;
        }
    }

    private static double Area2(List<Vec3> loop, Vec3 n) => Math.Abs(Vec3.Dot(Newell(loop, normalize: false), n));

    private static Vec3 Newell(List<Vec3> ring, bool normalize = true)
    {
        Vec3 n = Vec3.Zero;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            n += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return normalize ? n.Normalized() : n;
    }

    private static void TriangulatePlanar(List<List<Vec3>> loops, Vec3 n, Mesh mesh, bool allowNonPlanar = false)
    {
        var u = n.AnyPerpendicular(); var v = Vec3.Cross(n, u);
        var flat = loops.Select(l => new Polygon2(l.Select(p => new Vec2(Vec3.Dot(p, u), Vec3.Dot(p, v))))).ToList();
        var prof = new Profile2(flat[0], flat.Skip(1));
        var (verts, idx) = Triangulator.Triangulate(prof);
        // map back: build lookup from 2D vertex to 3D (same order as profile: outer then holes)
        var all3 = new List<Vec3>();
        all3.AddRange(OrientLike(loops[0], flat[0], prof.Outer));
        for (int h = 0; h < prof.Holes.Count; h++) all3.AddRange(OrientLike(loops[h + 1], flat[h + 1], prof.Holes[h]));
        int baseIdx = mesh.VertexCount;
        if (all3.Count != verts.Count) return;
        foreach (var p in all3) mesh.AddVertex(p);
        for (int i = 0; i < idx.Count; i += 3) mesh.AddTriangle(baseIdx + idx[i], baseIdx + idx[i + 1], baseIdx + idx[i + 2]);
    }

    /// <summary>Returns the 3D loop in the same vertex order as the oriented 2D polygon used by the profile.</summary>
    private static IEnumerable<Vec3> OrientLike(List<Vec3> loop3, Polygon2 original, Polygon2 oriented)
    {
        var l = new List<Vec3>(loop3);
        if (l.Count > 1 && l[0].ApproxEquals(l[^1], 1e-12)) l.RemoveAt(l.Count - 1);
        if (original.Count == oriented.Count && original.Points.Count > 1 && !original.Points[1].ApproxEquals(oriented.Points[1], 1e-12)) l.Reverse();
        return l;
    }

    /// <summary>Cylinders/cones: triangulate in (angle, height) parameter space then map to 3D.</summary>
    private static void TriangulateRevolved(List<List<Vec3>> loops, (Vec3 O, Vec3 X, Vec3 Y, Vec3 Z) fr, double r0, double tanSemi, bool sameSense, Mesh mesh)
    {
        // param loops with continuous angle unwrapping
        var param = new List<List<Vec2>>();
        foreach (var loop in loops)
        {
            var pl = new List<Vec2>(loop.Count);
            double prev = double.NaN;
            foreach (var p in loop)
            {
                var d = p - fr.O;
                double h = Vec3.Dot(d, fr.Z);
                double a = Math.Atan2(Vec3.Dot(d, fr.Y), Vec3.Dot(d, fr.X));
                if (!double.IsNaN(prev)) { while (a - prev > Math.PI) a -= 2 * Math.PI; while (a - prev < -Math.PI) a += 2 * Math.PI; }
                prev = a;
                pl.Add(new Vec2(a, h));
            }
            param.Add(pl);
        }
        double angleScale = Math.Max(r0, 1e-6);
        Vec3 Map(Vec2 q)
        {
            double r = r0 + q.Y * tanSemi;
            double a = q.X;
            return fr.O + fr.X * (r * Math.Cos(a)) + fr.Y * (r * Math.Sin(a)) + fr.Z * q.Y;
        }
        // full-revolution loops without seams (two separate circles): build a band between them
        bool closedCircles = param.Count == 2 && param.All(l => l.Max(q => q.X) - l.Min(q => q.X) > 1.9 * Math.PI && l.Max(q => q.Y) - l.Min(q => q.Y) < 1e-6 * Math.Max(1, r0));
        if (closedCircles)
        {
            BuildBand(param, Map, sameSense, mesh);
            return;
        }
        var polys = param.Select(l => new Polygon2(l.Select(q => new Vec2(q.X * angleScale, q.Y)))).ToList();
        var prof = new Profile2(polys[0], polys.Skip(1));
        var (verts, idx) = Triangulator.Triangulate(prof);
        // subdivide in angle so long triangles follow the surface
        int baseIdx = mesh.VertexCount;
        var sub = new Mesh();
        foreach (var v in verts) sub.AddVertex(new Vec3(v.X / angleScale, v.Y, 0));
        for (int i = 0; i < idx.Count; i += 3) sub.AddTriangle(idx[i], idx[i + 1], idx[i + 2]);
        sub = Refine(sub, Math.PI / 24);
        // orientation: param (a,h) CCW gives outward normal for a cylinder with +Z axis
        bool flip = !sameSense;
        foreach (var p in sub.Positions) mesh.AddVertex(Map(new Vec2(p.X, p.Y)));
        for (int i = 0; i < sub.Indices.Count; i += 3)
        {
            if (flip) mesh.AddTriangle(baseIdx + sub.Indices[i], baseIdx + sub.Indices[i + 2], baseIdx + sub.Indices[i + 1]);
            else mesh.AddTriangle(baseIdx + sub.Indices[i], baseIdx + sub.Indices[i + 1], baseIdx + sub.Indices[i + 2]);
        }
    }

    private static void BuildBand(List<List<Vec2>> param, Func<Vec2, Vec3> map, bool sameSense, Mesh mesh)
    {
        int n = 96;
        var a = param[0]; var b = param[1];
        double ha = a.Average(q => q.Y), hb = b.Average(q => q.Y);
        int baseIdx = mesh.VertexCount;
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            mesh.AddVertex(map(new Vec2(t, Math.Min(ha, hb))));
            mesh.AddVertex(map(new Vec2(t, Math.Max(ha, hb))));
        }
        for (int i = 0; i < n; i++)
        {
            int b0 = baseIdx + 2 * i, t0 = b0 + 1, b1 = baseIdx + 2 * ((i + 1) % n), t1 = b1 + 1;
            if (sameSense) { mesh.AddTriangle(b0, b1, t1); mesh.AddTriangle(b0, t1, t0); }
            else { mesh.AddTriangle(b0, t1, b1); mesh.AddTriangle(b0, t0, t1); }
        }
    }

    /// <summary>Splits triangles whose X (angle) extent exceeds <paramref name="maxStep"/>.</summary>
    private static Mesh Refine(Mesh m, double maxStep)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            bool changed = false;
            var r = new Mesh(); r.Positions.AddRange(m.Positions);
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var k = a < b ? (a, b) : (b, a);
                if (!mid.TryGetValue(k, out var i)) { i = r.AddVertex((r.Positions[a] + r.Positions[b]) * 0.5); mid[k] = i; }
                return i;
            }
            for (int t = 0; t < m.TriangleCount; t++)
            {
                int a = m.Indices[t * 3], b = m.Indices[t * 3 + 1], c = m.Indices[t * 3 + 2];
                double ext = Math.Max(Math.Abs(m.Positions[a].X - m.Positions[b].X), Math.Max(Math.Abs(m.Positions[b].X - m.Positions[c].X), Math.Abs(m.Positions[c].X - m.Positions[a].X)));
                if (ext <= maxStep) { r.AddTriangle(a, b, c); continue; }
                changed = true;
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                r.AddTriangle(a, ab, ca); r.AddTriangle(ab, b, bc); r.AddTriangle(ca, bc, c); r.AddTriangle(ab, bc, ca);
            }
            m = r;
            if (!changed) break;
        }
        return m;
    }

    private List<Vec3> Loop(P21.Entity loop)
    {
        var pts = new List<Vec3>();
        switch (loop.Type)
        {
            case "POLY_LOOP":
                foreach (var r in ((P21.List)loop.Args[1]).Items) pts.Add(Point(r));
                break;
            case "VERTEX_LOOP":
                pts.Add(Point(loop.Args[1]));
                break;
            case "EDGE_LOOP":
                foreach (var oeRef in ((P21.List)loop.Args[1]).Items)
                {
                    var oe = f.Get(oeRef)!; // ORIENTED_EDGE(name, *, *, edge_element, orientation)
                    bool orient = oe.Args[4] is not P21.Enum { Name: "F" };
                    var edge = f.Get(oe.Args[3])!;
                    var seg = Edge(edge);
                    if (!orient) seg.Reverse();
                    if (pts.Count > 0 && seg.Count > 0 && pts[^1].ApproxEquals(seg[0], 1e-6)) seg.RemoveAt(0);
                    pts.AddRange(seg);
                }
                if (pts.Count > 1 && pts[0].ApproxEquals(pts[^1], 1e-6)) pts.RemoveAt(pts.Count - 1);
                break;
        }
        return pts;
    }

    /// <summary>EDGE_CURVE(name, v1, v2, curve, same_sense) → sampled points from v1 to v2.</summary>
    private List<Vec3> Edge(P21.Entity edge)
    {
        var a = Point(edge.Args[1]); var b = Point(edge.Args[2]);
        var curve = f.Get(edge.Args[3]);
        bool same = edge.Args.Count <= 4 || edge.Args[4] is not P21.Enum { Name: "F" };
        if (curve is null) return [a, b];
        while (curve.Type is "SURFACE_CURVE" or "SEAM_CURVE" or "TRIMMED_CURVE")
        {
            var inner = f.Get(curve.Args[1]);
            if (inner is null) break;
            curve = inner;
        }
        switch (curve.Type)
        {
            case "LINE": return [a, b];
            case "CIRCLE":
            case "ELLIPSE":
                {
                    var fr = Frame(curve.Args[1]);
                    double r1 = ((P21.Num)curve.Args[2]).V;
                    double r2 = curve.Type == "ELLIPSE" ? ((P21.Num)curve.Args[3]).V : r1;
                    double Ang(Vec3 p) { var d = p - fr.O; return Math.Atan2(Vec3.Dot(d, fr.Y) / r2, Vec3.Dot(d, fr.X) / r1); }
                    double t0 = Ang(a), t1 = Ang(b);
                    if (same) { while (t1 <= t0 + 1e-9) t1 += 2 * Math.PI; }
                    else { while (t1 >= t0 - 1e-9) t1 -= 2 * Math.PI; }
                    int n = CurveMath.ArcSegments(Math.Max(r1, r2), Math.Abs(t1 - t0), tol);
                    var pts = new List<Vec3>(n + 1);
                    for (int i = 0; i <= n; i++)
                    {
                        double t = t0 + (t1 - t0) * i / n;
                        pts.Add(fr.O + fr.X * (r1 * Math.Cos(t)) + fr.Y * (r2 * Math.Sin(t)));
                    }
                    pts[0] = a; pts[^1] = b;
                    return pts;
                }
            default:
                if (curve.Is("B_SPLINE_CURVE") || curve.Type == "B_SPLINE_CURVE_WITH_KNOTS")
                {
                    try
                    {
                        var nurbs = Nurbs(curve);
                        var pts = nurbs.Tessellate(tol).ToList();
                        if (pts.Count >= 2)
                        {
                            if (Vec3.Distance(pts[0], a) > Vec3.Distance(pts[^1], a)) pts.Reverse();
                            pts[0] = a; pts[^1] = b;
                            return pts;
                        }
                    }
                    catch (Exception ex) { Warn($"B-spline edge approximated as a line: {ex.Message}"); }
                }
                else Warn($"Edge curve {curve.Type} approximated as a straight segment.");
                return [a, b];
        }
    }

    private NurbsCurve Nurbs(P21.Entity c)
    {
        IReadOnlyList<P21.Value> bs = c.Part("B_SPLINE_CURVE") ?? c.Args.Skip(1).ToList();
        IReadOnlyList<P21.Value>? kn = c.Part("B_SPLINE_CURVE_WITH_KNOTS");
        if (c.Type == "B_SPLINE_CURVE_WITH_KNOTS") { bs = c.Args.Skip(1).ToList(); kn = c.Args.Skip(6).ToList(); }
        int degree = (int)((P21.Num)bs[0]).V;
        var cps = ((P21.List)bs[1]).Items.Select(Point).ToList();
        List<double>? knots = null;
        if (kn is { Count: >= 2 } && kn[0] is P21.List mults && kn[1] is P21.List kv)
        {
            knots = [];
            for (int i = 0; i < kv.Items.Count; i++)
                for (int m = 0; m < (int)((P21.Num)mults.Items[i]).V; m++) knots.Add(((P21.Num)kv.Items[i]).V);
        }
        List<double>? weights = null;
        if (c.Part("RATIONAL_B_SPLINE_CURVE") is { } rat && rat.ElementAtOrDefault(0) is P21.List wl)
            weights = [.. wl.Items.Select(w => ((P21.Num)w).V)];
        if (knots != null && knots.Count == cps.Count + degree + 1)
        {
            double k0 = knots[0], k1 = knots[^1];
            knots = [.. knots.Select(k => (k - k0) / Math.Max(k1 - k0, 1e-12))];
        }
        else knots = null;
        return new NurbsCurve(degree, cps, weights, knots);
    }
}

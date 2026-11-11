using System.Text.Json.Serialization;
using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Parametric;

/// <summary>How a feature's solid combines with the body it targets.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeatureOperation>))]
public enum FeatureOperation { Add, Subtract, Intersect }

/// <summary>Evaluation context passed to features during rebuild.</summary>
public sealed class FeatureContext(IReadOnlyDictionary<string, double> vars, double tolerance)
{
    public IReadOnlyDictionary<string, double> Vars { get; } = vars;
    public double Tolerance { get; } = tolerance;
    public double E(Expr e, double fallback = 0) => e.IsEmpty ? fallback : e.Eval(Vars);
    /// <summary>Current state of all bodies (used by mirror and pattern-of-body features).</summary>
    public IReadOnlyDictionary<string, Mesh> Bodies { get; internal set; } = new Dictionary<string, Mesh>();
}

/// <summary>
/// Base class of all features. Every feature produces a solid in its local frame which is then
/// rotated (degrees, X→Y→Z order) and translated by its placement, and finally combined with the
/// target body using <see cref="Operation"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BoxFeature), "box")]
[JsonDerivedType(typeof(CylinderFeature), "cylinder")]
[JsonDerivedType(typeof(ConeFeature), "cone")]
[JsonDerivedType(typeof(SphereFeature), "sphere")]
[JsonDerivedType(typeof(TorusFeature), "torus")]
[JsonDerivedType(typeof(TubeFeature), "tube")]
[JsonDerivedType(typeof(ExtrudeFeature), "extrude")]
[JsonDerivedType(typeof(RevolveFeature), "revolve")]
[JsonDerivedType(typeof(LoftFeature), "loft")]
[JsonDerivedType(typeof(SweepFeature), "sweep")]
[JsonDerivedType(typeof(GearFeature), "gear")]
[JsonDerivedType(typeof(HoleFeature), "hole")]
[JsonDerivedType(typeof(PatternFeature), "pattern")]
[JsonDerivedType(typeof(MirrorFeature), "mirror")]
public abstract class Feature
{
    public string? Id { get; set; }
    public FeatureOperation Operation { get; set; } = FeatureOperation.Add;
    /// <summary>Target body name (features with different names build separate bodies).</summary>
    public string? Body { get; set; }
    public Expr X { get; set; }
    public Expr Y { get; set; }
    public Expr Z { get; set; }
    public Expr Rx { get; set; }
    public Expr Ry { get; set; }
    public Expr Rz { get; set; }
    /// <summary>Optional hex color for the body this feature creates.</summary>
    public string? Color { get; set; }
    public bool Suppressed { get; set; }

    /// <summary>Builds the feature solid in local coordinates.</summary>
    protected abstract Mesh BuildLocal(FeatureContext ctx);

    public virtual Mesh Build(FeatureContext ctx) => BuildLocal(ctx).Transformed(Placement(ctx));

    public Mat4 Placement(FeatureContext ctx)
    {
        var rot = Mat4.RotateZ(Units.Deg(ctx.E(Rz))) * Mat4.RotateY(Units.Deg(ctx.E(Ry))) * Mat4.RotateX(Units.Deg(ctx.E(Rx)));
        return Mat4.Translate(ctx.E(X), ctx.E(Y), ctx.E(Z)) * rot;
    }

    protected static int Seg(FeatureContext ctx, double r) =>
        Math.Clamp(CurveMath.ArcSegments(Math.Max(r, 0.01), 2 * Math.PI, ctx.Tolerance), 16, 256);
}

/// <summary>Where a primitive sits relative to its placement point.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Anchor>))]
public enum Anchor { Bottom, Center, Corner }

public sealed class BoxFeature : Feature
{
    public Expr Width { get; set; } = 10;
    public Expr Depth { get; set; } = 10;
    public Expr Height { get; set; } = 10;
    /// <summary>Bottom = centred in XY with base at z=0 (default); Center = fully centred; Corner = min corner at origin.</summary>
    public Anchor Anchor { get; set; } = Anchor.Bottom;
    protected override Mesh BuildLocal(FeatureContext c)
    {
        double w = c.E(Width), d = c.E(Depth), h = c.E(Height);
        return Anchor switch
        {
            Anchor.Center => Shapes.Box(w, d, h, true),
            Anchor.Corner => Shapes.Box(w, d, h, false),
            _ => Shapes.Box(w, d, h, true).Translated(0, 0, h / 2)
        };
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<Axis>))]
public enum Axis { X, Y, Z }

public sealed class CylinderFeature : Feature
{
    public Expr Radius { get; set; }
    public Expr Diameter { get; set; }
    public Expr Height { get; set; } = 10;
    public Axis Axis { get; set; } = Axis.Z;
    public Anchor Anchor { get; set; } = Anchor.Bottom;
    protected override Mesh BuildLocal(FeatureContext c)
    {
        double r = !Radius.IsEmpty ? c.E(Radius) : c.E(Diameter, 10) / 2;
        double h = c.E(Height);
        var m = Shapes.Cylinder(r, h, Seg(c, r), centered: Anchor == Anchor.Center);
        return AxisMesh(m, Axis);
    }
    internal static Mesh AxisMesh(Mesh m, Axis axis) => axis switch
    {
        Axis.X => m.Transformed(Mat4.RotateY(Math.PI / 2)),
        Axis.Y => m.Transformed(Mat4.RotateX(-Math.PI / 2)),
        _ => m
    };
}

public sealed class ConeFeature : Feature
{
    public Expr BottomRadius { get; set; } = 5;
    public Expr TopRadius { get; set; } = 0;
    public Expr Height { get; set; } = 10;
    public Axis Axis { get; set; } = Axis.Z;
    protected override Mesh BuildLocal(FeatureContext c)
    {
        double r0 = c.E(BottomRadius), r1 = c.E(TopRadius);
        return CylinderFeature.AxisMesh(Shapes.Cone(r0, r1, c.E(Height), Seg(c, Math.Max(r0, r1))), Axis);
    }
}

public sealed class SphereFeature : Feature
{
    public Expr Radius { get; set; } = 5;
    protected override Mesh BuildLocal(FeatureContext c) { var r = c.E(Radius); return Shapes.Sphere(r, Seg(c, r)); }
}

public sealed class TorusFeature : Feature
{
    public Expr MajorRadius { get; set; } = 10;
    public Expr MinorRadius { get; set; } = 2;
    protected override Mesh BuildLocal(FeatureContext c) => Shapes.Torus(c.E(MajorRadius), c.E(MinorRadius));
}

public sealed class TubeFeature : Feature
{
    public Expr OuterRadius { get; set; } = 10;
    public Expr InnerRadius { get; set; } = 8;
    public Expr Height { get; set; } = 10;
    public Axis Axis { get; set; } = Axis.Z;
    protected override Mesh BuildLocal(FeatureContext c)
    {
        var ro = c.E(OuterRadius);
        return CylinderFeature.AxisMesh(Shapes.Tube(ro, c.E(InnerRadius), c.E(Height), Seg(c, ro)), Axis);
    }
}

/// <summary>2D sketch description used by extrude / loft / sweep.</summary>
public sealed class SketchSpec
{
    /// <summary>rect | roundedRect | circle | polygon | regularPolygon | slot | star</summary>
    public string Shape { get; set; } = "rect";
    public Expr Width { get; set; } = 10;
    public Expr Height { get; set; } = 10;
    public Expr Radius { get; set; } = 5;
    public Expr InnerRadius { get; set; } = 2;
    public Expr CornerRadius { get; set; } = 1;
    public Expr Sides { get; set; } = 6;
    /// <summary>Explicit vertices for "polygon": [[x,y], ...].</summary>
    public List<Expr[]>? Points { get; set; }
    /// <summary>Offset of this sketch in its plane.</summary>
    public Expr X { get; set; }
    public Expr Y { get; set; }
    public List<SketchSpec>? Holes { get; set; }

    public Polygon2 ToPolygon(FeatureContext c)
    {
        double w = c.E(Width), h = c.E(Height), r = c.E(Radius);
        Polygon2 p = Shape.ToLowerInvariant() switch
        {
            "rect" or "rectangle" or "square" => Polygon2.Rectangle(w, Shape.Equals("square", StringComparison.OrdinalIgnoreCase) ? w : h),
            "roundedrect" or "rounded" => Polygon2.RoundedRectangle(w, h, c.E(CornerRadius), c.Tolerance),
            "circle" => Polygon2.Circle(r, Math.Clamp(CurveMath.ArcSegments(r, 2 * Math.PI, c.Tolerance), 16, 256)),
            "regularpolygon" or "hexagon" => Polygon2.RegularPolygon((int)c.E(Sides, 6), r),
            "slot" => Polygon2.Slot(w, h, c.Tolerance),
            "star" => Polygon2.Star((int)c.E(Sides, 5), r, c.E(InnerRadius)),
            "polygon" or "points" => new Polygon2((Points ?? throw new ArgumentException("polygon sketch needs points")).Select(a => new Vec2(c.E(a[0]), c.E(a[1])))),
            _ => throw new ArgumentException($"Unknown sketch shape '{Shape}'.")
        };
        return p.Translated(c.E(X), c.E(Y));
    }

    public Profile2 ToProfile(FeatureContext c)
    {
        var prof = new Profile2(ToPolygon(c));
        if (Holes != null) foreach (var h in Holes) prof.WithHole(h.ToPolygon(c));
        return prof;
    }
}

public sealed class ExtrudeFeature : Feature
{
    public SketchSpec Sketch { get; set; } = new();
    public Expr Height { get; set; } = 10;
    public Expr Twist { get; set; }
    public Expr TopScale { get; set; }
    protected override Mesh BuildLocal(FeatureContext c)
    {
        double twist = c.E(Twist), scale = c.E(TopScale, 1);
        if (Math.Abs(twist) > 1e-9 || Math.Abs(scale - 1) > 1e-9)
            return Shapes.ExtrudeTwisted(Sketch.ToPolygon(c), c.E(Height), twist, scale, Math.Max(8, (int)(Math.Abs(twist) / 5)));
        return Shapes.Extrude(Sketch.ToProfile(c), c.E(Height));
    }
}

public sealed class RevolveFeature : Feature
{
    /// <summary>Profile points [[radius, z], ...] in the XZ half-plane.</summary>
    public List<Expr[]> Profile { get; set; } = [];
    public Expr Angle { get; set; } = 360;
    protected override Mesh BuildLocal(FeatureContext c) =>
        Shapes.Revolve([.. Profile.Select(p => new Vec2(c.E(p[0]), c.E(p[1])))], c.E(Angle, 360));
}

public sealed class LoftFeature : Feature
{
    public SketchSpec Bottom { get; set; } = new();
    public SketchSpec Top { get; set; } = new() { Shape = "circle" };
    public Expr Height { get; set; } = 10;
    protected override Mesh BuildLocal(FeatureContext c) => Shapes.Loft(Bottom.ToPolygon(c), Top.ToPolygon(c), c.E(Height));
}

public sealed class SweepFeature : Feature
{
    public SketchSpec Section { get; set; } = new() { Shape = "circle", Radius = 1 };
    /// <summary>Path points [[x,y,z], ...]; ignored when a helix is given.</summary>
    public List<Expr[]>? Path { get; set; }
    public Expr HelixRadius { get; set; }
    public Expr HelixPitch { get; set; }
    public Expr HelixTurns { get; set; }
    protected override Mesh BuildLocal(FeatureContext c)
    {
        ICurve3 path = !HelixRadius.IsEmpty
            ? new Helix(c.E(HelixRadius), c.E(HelixPitch, 5), c.E(HelixTurns, 3))
            : new Polyline3Curve([.. (Path ?? throw new ArgumentException("sweep needs a path or helix")).Select(p => new Vec3(c.E(p[0]), c.E(p[1]), c.E(p[2])))]);
        return Shapes.Sweep(Section.ToPolygon(c), path, c.Tolerance);
    }
}

public sealed class GearFeature : Feature
{
    public Expr Module { get; set; } = 2;
    public Expr Teeth { get; set; } = 20;
    public Expr Thickness { get; set; } = 8;
    public Expr Bore { get; set; }
    public Expr PressureAngle { get; set; } = 20;
    protected override Mesh BuildLocal(FeatureContext c) =>
        Shapes.SpurGear(c.E(Module), (int)c.E(Teeth), c.E(Thickness), c.E(Bore), c.E(PressureAngle, 20));
}

/// <summary>
/// Drills holes (subtract by default) downward (-Z) from the placement point, at one or more XY positions.
/// Supports counterbores. Depth defaults to a through-hole.
/// </summary>
public sealed class HoleFeature : Feature
{
    public HoleFeature() { Operation = FeatureOperation.Subtract; }
    public Expr Diameter { get; set; } = 5;
    public Expr Depth { get; set; }
    public Expr CounterboreDiameter { get; set; }
    public Expr CounterboreDepth { get; set; }
    /// <summary>Additional hole centres [[x,y], ...] relative to the placement point.</summary>
    public List<Expr[]>? Positions { get; set; }

    protected override Mesh BuildLocal(FeatureContext c)
    {
        double r = c.E(Diameter) / 2;
        // Through holes are sized from the existing bodies: very long tools hurt boolean precision.
        var bb = BoundingBox.Empty;
        foreach (var m in c.Bodies.Values) bb = bb.Union(m.Bounds);
        double shift = 0;                          // when "z" is omitted, drill from the top of the part
        if (Z.IsEmpty && !bb.IsEmpty) shift = bb.Max.Z;
        double top = Z.IsEmpty ? shift : c.E(Z);
        double through = bb.IsEmpty ? 1e3 : Math.Max(1, top - bb.Min.Z + 1);
        double depth = Depth.IsEmpty ? through : c.E(Depth);
        double over = Math.Max(0.5, r * 0.1);      // pierce slightly above the surface for clean booleans
        var single = Shapes.Cylinder(r, depth + over, Seg(c, r)).Translated(0, 0, shift - depth);
        double cbD = c.E(CounterboreDiameter);
        if (cbD > 2 * r)
        {
            double cbDepth = c.E(CounterboreDepth, r);
            var cb = Shapes.Cylinder(cbD / 2, cbDepth + over, Seg(c, cbD / 2)).Translated(0, 0, shift - cbDepth);
            single = Csg.Union(single, cb);
        }
        var pts = Positions is { Count: > 0 } ? Positions.Select(p => new Vec2(c.E(p[0]), c.E(p[1]))).ToList() : [Vec2.Zero];
        return Mesh.Merge(pts.Select(p => single.Translated(p.X, p.Y, 0)));
    }
}

/// <summary>Repeats a feature: linear (X, optional Y grid) or circular about the Z axis through (CenterX, CenterY).</summary>
public sealed class PatternFeature : Feature
{
    /// <summary>linear | grid | circular</summary>
    public string Kind { get; set; } = "linear";
    public Feature? Source { get; set; }
    public Expr Count { get; set; } = 2;
    public Expr CountY { get; set; } = 1;
    public Expr Spacing { get; set; } = 10;
    public Expr SpacingY { get; set; } = 10;
    /// <summary>Total angle covered by a circular pattern (360 = evenly around).</summary>
    public Expr Angle { get; set; } = 360;
    public Expr CenterX { get; set; }
    public Expr CenterY { get; set; }

    protected override Mesh BuildLocal(FeatureContext c)
    {
        if (Source is null) throw new ArgumentException("pattern needs a source feature");
        var baseMesh = Source.Build(c);
        int n = Math.Max(1, (int)c.E(Count, 2));
        var parts = new List<Mesh>();
        if (Kind.Equals("circular", StringComparison.OrdinalIgnoreCase))
        {
            double total = c.E(Angle, 360);
            double step = Math.Abs(total - 360) < 1e-9 ? 360.0 / n : total / Math.Max(1, n - 1);
            var center = new Vec3(c.E(CenterX), c.E(CenterY), 0);
            for (int i = 0; i < n; i++) parts.Add(baseMesh.Transformed(Mat4.RotateAround(center, Vec3.UnitZ, Units.Deg(step * i))));
        }
        else
        {
            int ny = Kind.Equals("grid", StringComparison.OrdinalIgnoreCase) ? Math.Max(1, (int)c.E(CountY, 1)) : 1;
            double sx = c.E(Spacing), sy = c.E(SpacingY);
            for (int j = 0; j < ny; j++) for (int i = 0; i < n; i++) parts.Add(baseMesh.Translated(i * sx, j * sy, 0));
        }
        return Source.Operation == FeatureOperation.Add ? Csg.UnionAll(parts) : Mesh.Merge(parts);
    }
}

/// <summary>Mirrors the current target body across a principal plane and unions the copy.</summary>
public sealed class MirrorFeature : Feature
{
    /// <summary>yz | xz | xy</summary>
    public string Plane { get; set; } = "yz";
    protected override Mesh BuildLocal(FeatureContext c)
    {
        var name = Body ?? PartDefinition.DefaultBody;
        if (!c.Bodies.TryGetValue(name, out var body)) return new Mesh();
        var n = Plane.ToLowerInvariant() switch { "xz" => Vec3.UnitY, "xy" => Vec3.UnitZ, _ => Vec3.UnitX };
        return body.Transformed(Mat4.Mirror(Vec3.Zero, n));
    }
    public override Mesh Build(FeatureContext ctx) => BuildLocal(ctx);
}

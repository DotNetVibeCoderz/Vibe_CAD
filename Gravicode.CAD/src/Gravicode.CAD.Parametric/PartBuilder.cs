namespace Gravicode.CAD.Parametric;

/// <summary>
/// Fluent, CadQuery/Build123D-flavoured builder over <see cref="PartDefinition"/>.
/// <code>
/// var model = Part.Create("Bracket")
///     .Param("w", 80).Param("t", 8)
///     .Box("w", 40, "t")
///     .Holes(6, (-30, 12), (30, 12), top: "t")
///     .Build();
/// </code>
/// </summary>
public sealed class Part
{
    public PartDefinition Definition { get; }
    private Feature? _last;

    private Part(string name) { Definition = new PartDefinition { Name = name }; }
    public static Part Create(string name = "Part") => new(name);
    public static Part From(PartDefinition def) { var p = new Part(def.Name); p.Definition.Parameters = def.Parameters; p.Definition.Features = def.Features; return p; }

    public Part Param(string name, Expr value) { Definition.Parameters[name] = value; return this; }
    public Part Describe(string description) { Definition.Description = description; return this; }
    public Part Tolerance(double chord) { Definition.Tolerance = chord; return this; }

    public Part Add(Feature f) { Definition.Features.Add(f); _last = f; return this; }

    /// <summary>Sets the placement of the last feature.</summary>
    public Part At(Expr x, Expr y, Expr z) { Last.X = x; Last.Y = y; Last.Z = z; return this; }
    public Part Rotate(Expr rx, Expr ry, Expr rz) { Last.Rx = rx; Last.Ry = ry; Last.Rz = rz; return this; }
    public Part Color(string hex) { Last.Color = hex; return this; }
    public Part InBody(string body) { Last.Body = body; return this; }
    public Part Named(string id) { Last.Id = id; return this; }
    public Part AsCut() { Last.Operation = FeatureOperation.Subtract; return this; }
    public Part AsIntersect() { Last.Operation = FeatureOperation.Intersect; return this; }
    private Feature Last => _last ?? throw new InvalidOperationException("Add a feature first.");

    public Part Box(Expr width, Expr depth, Expr height, Anchor anchor = Anchor.Bottom) =>
        Add(new BoxFeature { Width = width, Depth = depth, Height = height, Anchor = anchor });

    public Part Cylinder(Expr diameter, Expr height, Axis axis = Axis.Z, Anchor anchor = Anchor.Bottom) =>
        Add(new CylinderFeature { Diameter = diameter, Height = height, Axis = axis, Anchor = anchor });

    public Part Cone(Expr bottomRadius, Expr topRadius, Expr height) =>
        Add(new ConeFeature { BottomRadius = bottomRadius, TopRadius = topRadius, Height = height });

    public Part Sphere(Expr radius) => Add(new SphereFeature { Radius = radius });
    public Part Torus(Expr major, Expr minor) => Add(new TorusFeature { MajorRadius = major, MinorRadius = minor });
    public Part Tube(Expr outerRadius, Expr innerRadius, Expr height) => Add(new TubeFeature { OuterRadius = outerRadius, InnerRadius = innerRadius, Height = height });

    public Part Extrude(SketchSpec sketch, Expr height) => Add(new ExtrudeFeature { Sketch = sketch, Height = height });
    public Part ExtrudeRect(Expr w, Expr h, Expr height) => Extrude(new SketchSpec { Shape = "rect", Width = w, Height = h }, height);
    public Part ExtrudeCircle(Expr r, Expr height) => Extrude(new SketchSpec { Shape = "circle", Radius = r }, height);
    public Part ExtrudePolygon(IEnumerable<(double X, double Y)> pts, Expr height) =>
        Extrude(new SketchSpec { Shape = "polygon", Points = [.. pts.Select(p => new Expr[] { p.X, p.Y })] }, height);

    public Part Revolve(IEnumerable<(double R, double Z)> profile, Expr? angle = null) =>
        Add(new RevolveFeature { Profile = [.. profile.Select(p => new Expr[] { p.R, p.Z })], Angle = angle ?? 360 });

    public Part Gear(Expr module, Expr teeth, Expr thickness, Expr? bore = null) =>
        Add(new GearFeature { Module = module, Teeth = teeth, Thickness = thickness, Bore = bore ?? default });

    /// <summary>Drills through-holes along Z at the given XY centres.</summary>
    public Part Holes(Expr diameter, params (double X, double Y)[] at) => Holes(diameter, default, default, at);

    public Part Holes(Expr diameter, Expr top, Expr depth, params (double X, double Y)[] at) =>
        Add(new HoleFeature
        {
            Diameter = diameter, Z = top, Depth = depth,
            Positions = [.. at.Select(p => new Expr[] { p.X, p.Y })]
        });

    public Part CounterboredHoles(Expr diameter, Expr cbDiameter, Expr cbDepth, Expr top, params (double X, double Y)[] at) =>
        Add(new HoleFeature
        {
            Diameter = diameter, CounterboreDiameter = cbDiameter, CounterboreDepth = cbDepth, Z = top,
            Positions = [.. at.Select(p => new Expr[] { p.X, p.Y })]
        });

    /// <summary>Circular pattern of the last feature around Z.</summary>
    public Part CircularPattern(Expr count, Expr? angle = null, Expr? cx = null, Expr? cy = null)
    {
        var src = Last;
        Definition.Features.Remove(src);
        return Add(new PatternFeature { Kind = "circular", Source = src, Count = count, Angle = angle ?? 360, CenterX = cx ?? default, CenterY = cy ?? default, Body = src.Body });
    }

    /// <summary>Linear (or grid when countY &gt; 1) pattern of the last feature.</summary>
    public Part LinearPattern(Expr count, Expr spacing, Expr? countY = null, Expr? spacingY = null)
    {
        var src = Last;
        Definition.Features.Remove(src);
        bool grid = countY is { } cy && !cy.IsEmpty;
        return Add(new PatternFeature { Kind = grid ? "grid" : "linear", Source = src, Count = count, Spacing = spacing, CountY = countY ?? 1, SpacingY = spacingY ?? 0, Body = src.Body });
    }

    public Part Mirror(string plane = "yz") => Add(new MirrorFeature { Plane = plane });

    public RebuildResult Rebuild(IReadOnlyDictionary<string, double>? overrides = null) => Definition.Rebuild(overrides);
    public CadModel Build(IReadOnlyDictionary<string, double>? overrides = null) => Definition.Build(overrides);
    public string ToJson() => Definition.ToJson();
}

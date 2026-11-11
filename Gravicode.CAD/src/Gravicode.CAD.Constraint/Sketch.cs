using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Constraint;

/// <summary>A point whose coordinates are solver unknowns.</summary>
public sealed class SketchPoint(Sketch owner, int index)
{
    internal readonly int Index = index; // x at Index, y at Index+1
    public Sketch Sketch { get; } = owner;
    public double X { get => Sketch.Values[Index]; set => Sketch.Values[Index] = value; }
    public double Y { get => Sketch.Values[Index + 1]; set => Sketch.Values[Index + 1] = value; }
    public Vec2 Position => new(X, Y);
    public bool Fixed { get; set; }
    public override string ToString() => $"P{Index / 2}{Position}";
}

public abstract class SketchEntity { public bool Construction { get; set; } }

public sealed class SketchLine(SketchPoint a, SketchPoint b) : SketchEntity
{
    public SketchPoint A { get; } = a;
    public SketchPoint B { get; } = b;
    public double Length => Vec2.Distance(A.Position, B.Position);
}

public sealed class SketchCircle(SketchPoint center, int radiusIndex, Sketch s) : SketchEntity
{
    public SketchPoint Center { get; } = center;
    internal readonly int RadiusIndex = radiusIndex;
    public double Radius { get => s.Values[RadiusIndex]; set => s.Values[RadiusIndex] = value; }
}

/// <summary>Counter-clockwise arc from Start to End around Center (radius equality is enforced automatically).</summary>
public sealed class SketchArc(SketchPoint center, SketchPoint start, SketchPoint end) : SketchEntity
{
    public SketchPoint Center { get; } = center;
    public SketchPoint Start { get; } = start;
    public SketchPoint End { get; } = end;
    public double Radius => Vec2.Distance(Center.Position, Start.Position);
}

/// <summary>
/// A 2D parametric sketch: entities + constraints over a vector of unknowns. Call <see cref="Solve"/>
/// to satisfy all constraints starting from the current (approximate) geometry.
/// </summary>
public sealed class Sketch
{
    internal List<double> Values { get; } = [];
    private readonly List<int> _fixedIdx = [];
    public List<SketchPoint> Points { get; } = [];
    public List<SketchEntity> Entities { get; } = [];
    public List<SketchConstraint> Constraints { get; } = [];
    public string Name { get; set; } = "Sketch";

    public SketchPoint Point(double x, double y, bool fixedPoint = false)
    {
        var p = new SketchPoint(this, Values.Count) { Fixed = fixedPoint };
        Values.Add(x); Values.Add(y);
        Points.Add(p);
        return p;
    }

    public SketchLine Line(SketchPoint a, SketchPoint b) { var l = new SketchLine(a, b); Entities.Add(l); return l; }
    public SketchLine Line(double x0, double y0, double x1, double y1) => Line(Point(x0, y0), Point(x1, y1));

    public SketchCircle Circle(SketchPoint center, double radius)
    {
        int ri = Values.Count; Values.Add(radius);
        var c = new SketchCircle(center, ri, this); Entities.Add(c); return c;
    }
    public SketchCircle Circle(double cx, double cy, double r) => Circle(Point(cx, cy), r);

    public SketchArc Arc(SketchPoint center, SketchPoint start, SketchPoint end)
    {
        var a = new SketchArc(center, start, end);
        Entities.Add(a);
        Constraints.Add(new EqualDistanceConstraint(center, start, center, end));
        return a;
    }

    /// <summary>Closed polyline of lines sharing end points (coincidence by construction).</summary>
    public List<SketchLine> Polyline(params (double X, double Y)[] pts)
    {
        var ps = pts.Select(p => Point(p.X, p.Y)).ToList();
        var lines = new List<SketchLine>();
        for (int i = 0; i < ps.Count; i++) lines.Add(Line(ps[i], ps[(i + 1) % ps.Count]));
        return lines;
    }

    public Sketch Add(SketchConstraint c) { Constraints.Add(c); return this; }

    // ----- constraint shorthands -----
    public Sketch Coincident(SketchPoint a, SketchPoint b) => Add(new CoincidentConstraint(a, b));
    public Sketch Horizontal(SketchLine l) => Add(new HorizontalConstraint(l));
    public Sketch Vertical(SketchLine l) => Add(new VerticalConstraint(l));
    public Sketch Parallel(SketchLine a, SketchLine b) => Add(new ParallelConstraint(a, b));
    public Sketch Perpendicular(SketchLine a, SketchLine b) => Add(new PerpendicularConstraint(a, b));
    public Sketch Distance(SketchPoint a, SketchPoint b, double d) => Add(new DistanceConstraint(a, b, d));
    public Sketch Length(SketchLine l, double d) => Add(new DistanceConstraint(l.A, l.B, d));
    public Sketch HorizontalDistance(SketchPoint a, SketchPoint b, double d) => Add(new AxisDistanceConstraint(a, b, d, 0));
    public Sketch VerticalDistance(SketchPoint a, SketchPoint b, double d) => Add(new AxisDistanceConstraint(a, b, d, 1));
    public Sketch Angle(SketchLine a, SketchLine b, double degrees) => Add(new AngleConstraint(a, b, Units.Deg(degrees)));
    public Sketch Radius(SketchCircle c, double r) => Add(new RadiusConstraint(c, r));
    public Sketch Diameter(SketchCircle c, double d) => Add(new RadiusConstraint(c, d / 2));
    public Sketch ArcRadius(SketchArc a, double r) => Add(new DistanceConstraint(a.Center, a.Start, r));
    public Sketch EqualLength(SketchLine a, SketchLine b) => Add(new EqualDistanceConstraint(a.A, a.B, b.A, b.B));
    public Sketch Fix(SketchPoint p) { p.Fixed = true; return this; }
    public Sketch FixAt(SketchPoint p, double x, double y) { p.X = x; p.Y = y; p.Fixed = true; return this; }
    public Sketch PointOnLine(SketchPoint p, SketchLine l) => Add(new PointOnLineConstraint(p, l));
    public Sketch PointOnCircle(SketchPoint p, SketchCircle c) => Add(new PointOnCircleConstraint(p, c));
    public Sketch Midpoint(SketchPoint p, SketchLine l) => Add(new MidpointConstraint(p, l));
    public Sketch Tangent(SketchLine l, SketchCircle c) => Add(new TangentLineCircleConstraint(l, c));
    public Sketch Symmetric(SketchPoint a, SketchPoint b, SketchLine axis) => Add(new SymmetricConstraint(a, b, axis));

    internal bool IsFixed(int valueIndex)
    {
        foreach (var p in Points) if (p.Fixed && (valueIndex == p.Index || valueIndex == p.Index + 1)) return true;
        return false;
    }

    /// <summary>Solves the constraint system; returns convergence info and remaining degrees of freedom.</summary>
    public SolveResult Solve(int maxIterations = 200, double tolerance = 1e-10) => ConstraintSolver.Solve(this, maxIterations, tolerance);

    /// <summary>Closed loops built from non-construction lines and arcs (end points matched within tolerance).</summary>
    public List<Polygon2> ToLoops(double chordTolerance = 0.02, double joinTol = 1e-6)
    {
        var segs = new List<List<Vec2>>();
        foreach (var e in Entities.Where(e => !e.Construction))
        {
            switch (e)
            {
                case SketchLine l: segs.Add([l.A.Position, l.B.Position]); break;
                case SketchArc a:
                    {
                        var c = a.Center.Position;
                        var arc = new Arc2(c, a.Radius, (a.Start.Position - c).Angle, (a.End.Position - c).Angle);
                        segs.Add([.. arc.Tessellate(chordTolerance)]);
                        break;
                    }
            }
        }
        var loops = new List<Polygon2>();
        foreach (var circ in Entities.OfType<SketchCircle>().Where(c => !c.Construction))
            loops.Add(Polygon2.Circle(circ.Radius, center: circ.Center.Position, tolerance: chordTolerance));

        while (segs.Count > 0)
        {
            var chain = new List<Vec2>(segs[0]); segs.RemoveAt(0);
            bool grew = true;
            while (grew && !chain[0].ApproxEquals(chain[^1], joinTol))
            {
                grew = false;
                for (int i = 0; i < segs.Count; i++)
                {
                    var s = segs[i];
                    if (s[0].ApproxEquals(chain[^1], joinTol)) { chain.AddRange(s.Skip(1)); }
                    else if (s[^1].ApproxEquals(chain[^1], joinTol)) { chain.AddRange(Enumerable.Reverse(s).Skip(1)); }
                    else continue;
                    segs.RemoveAt(i); grew = true; break;
                }
            }
            if (chain.Count >= 4 && chain[0].ApproxEquals(chain[^1], joinTol)) loops.Add(new Polygon2(chain));
        }
        return loops;
    }

    /// <summary>Profiles (outer + holes) ready for extrusion.</summary>
    public List<Profile2> ToProfiles(double chordTolerance = 0.02) => Profile2.FromLoops(ToLoops(chordTolerance));

    /// <summary>Exports the sketch as drawing entities (for DXF / SVG).</summary>
    public IEnumerable<DrawingEntity> ToDrawing()
    {
        foreach (var e in Entities)
        {
            var layer = e.Construction ? "CONSTRUCTION" : "SKETCH";
            yield return e switch
            {
                SketchLine l => new LineEntity(l.A.Position, l.B.Position) { Layer = layer },
                SketchCircle c => new CircleEntity(c.Center.Position, c.Radius) { Layer = layer },
                SketchArc a => new ArcEntity(a.Center.Position, a.Radius, (a.Start.Position - a.Center.Position).Angle, (a.End.Position - a.Center.Position).Angle) { Layer = layer },
                _ => throw new NotSupportedException()
            };
        }
    }
}

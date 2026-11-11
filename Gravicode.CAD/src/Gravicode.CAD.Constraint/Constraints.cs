namespace Gravicode.CAD.Constraint;

/// <summary>A constraint contributes one or more residuals that must reach zero.</summary>
public abstract class SketchConstraint
{
    public abstract int ResidualCount { get; }
    /// <summary>Writes residuals for the given unknown vector.</summary>
    public abstract void Residuals(ReadOnlySpan<double> v, Span<double> r);
    public string Name => GetType().Name.Replace("Constraint", "");

    protected static (double X, double Y) P(ReadOnlySpan<double> v, SketchPoint p) => (v[p.Index], v[p.Index + 1]);
    protected static (double X, double Y) D(ReadOnlySpan<double> v, SketchLine l)
    {
        var a = P(v, l.A); var b = P(v, l.B);
        return (b.X - a.X, b.Y - a.Y);
    }
    protected static double Len((double X, double Y) d) => Math.Sqrt(d.X * d.X + d.Y * d.Y);
}

public sealed class CoincidentConstraint(SketchPoint a, SketchPoint b) : SketchConstraint
{
    public override int ResidualCount => 2;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    { var p = P(v, a); var q = P(v, b); r[0] = p.X - q.X; r[1] = p.Y - q.Y; }
}

public sealed class HorizontalConstraint(SketchLine l) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r) => r[0] = D(v, l).Y;
}

public sealed class VerticalConstraint(SketchLine l) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r) => r[0] = D(v, l).X;
}

public sealed class DistanceConstraint(SketchPoint a, SketchPoint b, double distance) : SketchConstraint
{
    public double Value { get; set; } = distance;
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    { var p = P(v, a); var q = P(v, b); r[0] = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)) - Value; }
}

/// <summary>Signed horizontal (axis 0) or vertical (axis 1) distance b - a.</summary>
public sealed class AxisDistanceConstraint(SketchPoint a, SketchPoint b, double distance, int axis) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    { var p = P(v, a); var q = P(v, b); r[0] = (axis == 0 ? q.X - p.X : q.Y - p.Y) - distance; }
}

public sealed class EqualDistanceConstraint(SketchPoint a, SketchPoint b, SketchPoint c, SketchPoint d) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var pa = P(v, a); var pb = P(v, b); var pc = P(v, c); var pd = P(v, d);
        r[0] = Len((pb.X - pa.X, pb.Y - pa.Y)) - Len((pd.X - pc.X, pd.Y - pc.Y));
    }
}

public sealed class ParallelConstraint(SketchLine a, SketchLine b) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var u = D(v, a); var w = D(v, b);
        r[0] = (u.X * w.Y - u.Y * w.X) / Math.Max(Len(u) * Len(w), 1e-12);
    }
}

public sealed class PerpendicularConstraint(SketchLine a, SketchLine b) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var u = D(v, a); var w = D(v, b);
        r[0] = (u.X * w.X + u.Y * w.Y) / Math.Max(Len(u) * Len(w), 1e-12);
    }
}

/// <summary>Angle from line a to line b (CCW, radians).</summary>
public sealed class AngleConstraint(SketchLine a, SketchLine b, double radians) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var u = D(v, a); var w = D(v, b);
        double ang = Math.Atan2(u.X * w.Y - u.Y * w.X, u.X * w.X + u.Y * w.Y);
        double diff = ang - radians;
        r[0] = Math.Atan2(Math.Sin(diff), Math.Cos(diff));
    }
}

public sealed class RadiusConstraint(SketchCircle c, double radius) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r) => r[0] = v[c.RadiusIndex] - radius;
}

public sealed class PointOnLineConstraint(SketchPoint p, SketchLine l) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var a = P(v, l.A); var d = D(v, l); var q = P(v, p);
        r[0] = (d.X * (q.Y - a.Y) - d.Y * (q.X - a.X)) / Math.Max(Len(d), 1e-12);
    }
}

public sealed class PointOnCircleConstraint(SketchPoint p, SketchCircle c) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var q = P(v, p); var o = P(v, c.Center);
        r[0] = Len((q.X - o.X, q.Y - o.Y)) - v[c.RadiusIndex];
    }
}

public sealed class MidpointConstraint(SketchPoint p, SketchLine l) : SketchConstraint
{
    public override int ResidualCount => 2;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var a = P(v, l.A); var b = P(v, l.B); var q = P(v, p);
        r[0] = q.X - (a.X + b.X) / 2; r[1] = q.Y - (a.Y + b.Y) / 2;
    }
}

public sealed class TangentLineCircleConstraint(SketchLine l, SketchCircle c) : SketchConstraint
{
    public override int ResidualCount => 1;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var a = P(v, l.A); var d = D(v, l); var o = P(v, c.Center);
        double dist = Math.Abs(d.X * (o.Y - a.Y) - d.Y * (o.X - a.X)) / Math.Max(Len(d), 1e-12);
        r[0] = dist - v[c.RadiusIndex];
    }
}

/// <summary>a and b mirror each other across the axis line.</summary>
public sealed class SymmetricConstraint(SketchPoint a, SketchPoint b, SketchLine axis) : SketchConstraint
{
    public override int ResidualCount => 2;
    public override void Residuals(ReadOnlySpan<double> v, Span<double> r)
    {
        var pa = P(v, a); var pb = P(v, b); var o = P(v, axis.A); var d = D(v, axis);
        double L = Math.Max(Len(d), 1e-12);
        var mid = ((pa.X + pb.X) / 2 - o.X, (pa.Y + pb.Y) / 2 - o.Y);
        r[0] = (d.X * mid.Item2 - d.Y * mid.Item1) / L;              // midpoint on axis
        r[1] = ((pb.X - pa.X) * d.X + (pb.Y - pa.Y) * d.Y) / L;      // ab ⟂ axis
    }
}

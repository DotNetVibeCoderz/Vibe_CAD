namespace Gravicode.CAD.Geometry;

/// <summary>A parametric 2D curve on t ∈ [0,1].</summary>
public interface ICurve2
{
    Vec2 PointAt(double t);
    double Length { get; }
    /// <summary>Polyline approximation within the chordal tolerance.</summary>
    IReadOnlyList<Vec2> Tessellate(double tolerance = 0.05);
}

/// <summary>A parametric 3D curve on t ∈ [0,1].</summary>
public interface ICurve3
{
    Vec3 PointAt(double t);
    IReadOnlyList<Vec3> Tessellate(double tolerance = 0.05);
}

public sealed record Line2(Vec2 A, Vec2 B) : ICurve2
{
    public Vec2 PointAt(double t) => Vec2.Lerp(A, B, t);
    public double Length => Vec2.Distance(A, B);
    public IReadOnlyList<Vec2> Tessellate(double tolerance = 0.05) => [A, B];
}

/// <summary>Circular arc, CCW from start to end angle (radians).</summary>
public sealed record Arc2(Vec2 Center, double Radius, double StartAngle, double EndAngle) : ICurve2
{
    public double Sweep { get { var s = EndAngle - StartAngle; while (s <= 0) s += 2 * Math.PI; return s; } }
    public Vec2 PointAt(double t) => Center + Vec2.FromAngle(StartAngle + Sweep * t, Radius);
    public double Length => Radius * Sweep;
    public IReadOnlyList<Vec2> Tessellate(double tolerance = 0.05)
    {
        int n = CurveMath.ArcSegments(Radius, Sweep, tolerance);
        var r = new Vec2[n + 1];
        for (int i = 0; i <= n; i++) r[i] = PointAt((double)i / n);
        return r;
    }

    /// <summary>Arc through three points.</summary>
    public static Arc2 ThroughPoints(Vec2 a, Vec2 b, Vec2 c)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-14) throw new ArgumentException("Points are collinear.");
        double ux = ((a.LengthSquared) * (b.Y - c.Y) + (b.LengthSquared) * (c.Y - a.Y) + (c.LengthSquared) * (a.Y - b.Y)) / d;
        double uy = ((a.LengthSquared) * (c.X - b.X) + (b.LengthSquared) * (a.X - c.X) + (c.LengthSquared) * (b.X - a.X)) / d;
        var center = new Vec2(ux, uy);
        double r = Vec2.Distance(center, a);
        double a0 = (a - center).Angle, a2 = (c - center).Angle;
        bool ccw = Vec2.Cross(b - a, c - b) > 0;
        return ccw ? new Arc2(center, r, a0, a2) : new Arc2(center, r, a2, a0);
    }
}

/// <summary>Bezier curve of any degree (de Casteljau).</summary>
public sealed class Bezier2(IReadOnlyList<Vec2> controlPoints) : ICurve2
{
    public IReadOnlyList<Vec2> ControlPoints { get; } = controlPoints;
    public Vec2 PointAt(double t)
    {
        Span<Vec2> p = stackalloc Vec2[ControlPoints.Count];
        for (int i = 0; i < p.Length; i++) p[i] = ControlPoints[i];
        for (int k = p.Length - 1; k > 0; k--)
            for (int i = 0; i < k; i++) p[i] = Vec2.Lerp(p[i], p[i + 1], t);
        return p[0];
    }
    public double Length => CurveMath.PolyLength(Tessellate(0.01));
    public IReadOnlyList<Vec2> Tessellate(double tolerance = 0.05) => CurveMath.Adaptive(PointAt, tolerance);
}

/// <summary>Non-uniform rational B-spline curve in 3D (2D curves use Z=0).</summary>
public sealed class NurbsCurve : ICurve3
{
    public int Degree { get; }
    public IReadOnlyList<Vec3> ControlPoints { get; }
    public IReadOnlyList<double> Weights { get; }
    public IReadOnlyList<double> Knots { get; }

    public NurbsCurve(int degree, IReadOnlyList<Vec3> controlPoints, IReadOnlyList<double>? weights = null, IReadOnlyList<double>? knots = null)
    {
        if (controlPoints.Count <= degree) throw new ArgumentException("Need more control points than the degree.");
        Degree = degree;
        ControlPoints = controlPoints;
        Weights = weights ?? Enumerable.Repeat(1.0, controlPoints.Count).ToArray();
        Knots = knots ?? ClampedUniformKnots(controlPoints.Count, degree);
        if (Knots.Count != controlPoints.Count + degree + 1) throw new ArgumentException("Knot vector length must be n + p + 1.");
    }

    public static double[] ClampedUniformKnots(int n, int p)
    {
        var k = new double[n + p + 1];
        int interior = n - p;
        for (int i = 0; i < k.Length; i++)
            k[i] = i <= p ? 0 : i >= n ? 1 : (double)(i - p) / interior;
        return k;
    }

    /// <summary>Exact circle as a degree-2 rational NURBS (9 control points).</summary>
    public static NurbsCurve Circle(Vec3 center, double r)
    {
        double w = Math.Sqrt(0.5);
        Vec3[] cp = [new(r, 0, 0), new(r, r, 0), new(0, r, 0), new(-r, r, 0), new(-r, 0, 0), new(-r, -r, 0), new(0, -r, 0), new(r, -r, 0), new(r, 0, 0)];
        return new NurbsCurve(2, [.. cp.Select(p => p + center)], [1, w, 1, w, 1, w, 1, w, 1], [0, 0, 0, .25, .25, .5, .5, .75, .75, 1, 1, 1]);
    }

    private int FindSpan(double u)
    {
        int n = ControlPoints.Count - 1;
        if (u >= Knots[n + 1]) return n;
        if (u <= Knots[Degree]) return Degree;
        int lo = Degree, hi = n + 1, mid = (lo + hi) / 2;
        while (u < Knots[mid] || u >= Knots[mid + 1])
        {
            if (u < Knots[mid]) hi = mid; else lo = mid;
            mid = (lo + hi) / 2;
        }
        return mid;
    }

    public Vec3 PointAt(double t)
    {
        double u = Knots[Degree] + (Knots[ControlPoints.Count] - Knots[Degree]) * Math.Clamp(t, 0, 1);
        int span = FindSpan(u);
        Span<double> N = stackalloc double[Degree + 1];
        Span<double> left = stackalloc double[Degree + 1];
        Span<double> right = stackalloc double[Degree + 1];
        N[0] = 1;
        for (int j = 1; j <= Degree; j++)
        {
            left[j] = u - Knots[span + 1 - j];
            right[j] = Knots[span + j] - u;
            double saved = 0;
            for (int r = 0; r < j; r++)
            {
                double tmp = N[r] / (right[r + 1] + left[j - r]);
                N[r] = saved + right[r + 1] * tmp;
                saved = left[j - r] * tmp;
            }
            N[j] = saved;
        }
        Vec3 p = Vec3.Zero; double wsum = 0;
        for (int i = 0; i <= Degree; i++)
        {
            int idx = span - Degree + i;
            double w = N[i] * Weights[idx];
            p += ControlPoints[idx] * w;
            wsum += w;
        }
        return p / wsum;
    }

    public IReadOnlyList<Vec3> Tessellate(double tolerance = 0.05) => CurveMath.Adaptive3(PointAt, tolerance);
}

/// <summary>Helix around +Z (threads, springs, sweep paths).</summary>
public sealed record Helix(double Radius, double Pitch, double Turns, double StartAngle = 0) : ICurve3
{
    public Vec3 PointAt(double t)
    {
        double a = StartAngle + 2 * Math.PI * Turns * t;
        return new Vec3(Radius * Math.Cos(a), Radius * Math.Sin(a), Pitch * Turns * t);
    }
    public IReadOnlyList<Vec3> Tessellate(double tolerance = 0.05)
    {
        int n = Math.Max(8, CurveMath.ArcSegments(Radius, 2 * Math.PI * Turns, tolerance));
        return [.. Enumerable.Range(0, n + 1).Select(i => PointAt((double)i / n))];
    }
}

public sealed class Polyline3Curve(IReadOnlyList<Vec3> points) : ICurve3
{
    public IReadOnlyList<Vec3> Points { get; } = points;
    public Vec3 PointAt(double t)
    {
        double f = Math.Clamp(t, 0, 1) * (Points.Count - 1);
        int i = Math.Min((int)f, Points.Count - 2);
        return Vec3.Lerp(Points[i], Points[i + 1], f - i);
    }
    public IReadOnlyList<Vec3> Tessellate(double tolerance = 0.05) => Points;
}

public static class CurveMath
{
    public static int ArcSegments(double radius, double sweep, double tol)
    {
        if (radius <= tol) return 4;
        double step = 2 * Math.Acos(Math.Clamp(1 - tol / radius, -1, 1));
        return Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / Math.Max(step, 1e-3)), 4, 1024);
    }

    public static double PolyLength(IReadOnlyList<Vec2> p)
    {
        double l = 0;
        for (int i = 1; i < p.Count; i++) l += Vec2.Distance(p[i - 1], p[i]);
        return l;
    }

    /// <summary>Adaptive subdivision until the midpoint deviates less than tol from the chord.</summary>
    public static IReadOnlyList<Vec2> Adaptive(Func<double, Vec2> f, double tol, int depthLimit = 14)
    {
        var r = new List<Vec2> { f(0) };
        const int initial = 8;
        for (int i = 0; i < initial; i++) Rec(i / (double)initial, (i + 1) / (double)initial, f(i / (double)initial), f((i + 1) / (double)initial), 0);
        return r;

        void Rec(double t0, double t1, Vec2 p0, Vec2 p1, int depth)
        {
            double tm = (t0 + t1) / 2;
            var pm = f(tm);
            var chordMid = (p0 + p1) * 0.5;
            if (depth >= depthLimit || Vec2.Distance(pm, chordMid) <= tol) { r.Add(p1); return; }
            Rec(t0, tm, p0, pm, depth + 1);
            Rec(tm, t1, pm, p1, depth + 1);
        }
    }

    public static IReadOnlyList<Vec3> Adaptive3(Func<double, Vec3> f, double tol, int depthLimit = 14)
    {
        var r = new List<Vec3> { f(0) };
        const int initial = 16;
        for (int i = 0; i < initial; i++) Rec(i / (double)initial, (i + 1) / (double)initial, f(i / (double)initial), f((i + 1) / (double)initial), 0);
        return r;

        void Rec(double t0, double t1, Vec3 p0, Vec3 p1, int depth)
        {
            double tm = (t0 + t1) / 2;
            var pm = f(tm);
            if (depth >= depthLimit || Vec3.Distance(pm, (p0 + p1) * 0.5) <= tol) { r.Add(p1); return; }
            Rec(t0, tm, p0, pm, depth + 1);
            Rec(tm, t1, pm, p1, depth + 1);
        }
    }
}

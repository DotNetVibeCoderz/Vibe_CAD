using System.Runtime.CompilerServices;

namespace Gravicode.CAD;

/// <summary>Double-precision 2D vector / point.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);
    public static readonly Vec2 UnitX = new(1, 0);
    public static readonly Vec2 UnitY = new(0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec2 Normalized() { var l = Length; return l > 0 ? new(X / l, Y / l) : Zero; }
    /// <summary>Counter-clockwise perpendicular.</summary>
    public Vec2 Perp() => new(-Y, X);
    public Vec2 Rotate(double radians) { var c = Math.Cos(radians); var s = Math.Sin(radians); return new(X * c - Y * s, X * s + Y * c); }
    public double Angle => Math.Atan2(Y, X);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    /// <summary>2D cross product (z component of the 3D cross).</summary>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;
    public static Vec2 FromAngle(double radians, double length = 1) => new(Math.Cos(radians) * length, Math.Sin(radians) * length);
    public bool ApproxEquals(Vec2 o, double eps = Tolerance.Linear) => Math.Abs(X - o.X) <= eps && Math.Abs(Y - o.Y) <= eps;
    public Vec3 ToVec3(double z = 0) => new(X, Y, z);

    public override string ToString() => $"({X:G6}, {Y:G6})";
}

/// <summary>Double-precision 3D vector / point.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 One = new(1, 1, 1);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 Normalized() { var l = Length; return l > 1e-300 ? new(X / l, Y / l, Z / l) : Zero; }
    public Vec2 XY => new(X, Y);

    public double this[int axis] => axis switch { 0 => X, 1 => Y, 2 => Z, _ => throw new ArgumentOutOfRangeException(nameof(axis)) };

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;
    public static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    public static Vec3 Mul(Vec3 a, Vec3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static double AngleBetween(Vec3 a, Vec3 b) => Math.Acos(Math.Clamp(Dot(a.Normalized(), b.Normalized()), -1, 1));

    /// <summary>Any unit vector perpendicular to this one.</summary>
    public Vec3 AnyPerpendicular()
    {
        var a = Math.Abs(X) < 0.9 ? UnitX : UnitY;
        return Cross(this, a).Normalized();
    }

    public bool ApproxEquals(Vec3 o, double eps = Tolerance.Linear) =>
        Math.Abs(X - o.X) <= eps && Math.Abs(Y - o.Y) <= eps && Math.Abs(Z - o.Z) <= eps;

    public System.Numerics.Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
    public static Vec3 FromVector3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public override string ToString() => $"({X:G6}, {Y:G6}, {Z:G6})";
}

/// <summary>Global geometric tolerances.</summary>
public static class Tolerance
{
    /// <summary>Linear tolerance (model units) used for point coincidence.</summary>
    public const double Linear = 1e-7;
    /// <summary>Angular tolerance in radians.</summary>
    public const double Angular = 1e-9;
    /// <summary>Tolerance used by mesh boolean operations (plane classification).</summary>
    public const double Boolean = 1e-5;
}

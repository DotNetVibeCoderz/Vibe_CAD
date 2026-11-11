namespace Gravicode.CAD;

/// <summary>Row-major double-precision 4x4 transform. Points are column vectors: p' = M * p.</summary>
public readonly struct Mat4 : IEquatable<Mat4>
{
    public readonly double M11, M12, M13, M14;
    public readonly double M21, M22, M23, M24;
    public readonly double M31, M32, M33, M34;
    public readonly double M41, M42, M43, M44;

    public Mat4(double m11, double m12, double m13, double m14,
                double m21, double m22, double m23, double m24,
                double m31, double m32, double m33, double m34,
                double m41, double m42, double m43, double m44)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        M41 = m41; M42 = m42; M43 = m43; M44 = m44;
    }

    public static readonly Mat4 Identity = new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    public Vec3 Translation => new(M14, M24, M34);
    public bool IsIdentity => Equals(Identity);

    public static Mat4 Translation3(Vec3 t) => new(1, 0, 0, t.X, 0, 1, 0, t.Y, 0, 0, 1, t.Z, 0, 0, 0, 1);
    public static Mat4 Translate(double x, double y, double z) => Translation3(new Vec3(x, y, z));
    public static Mat4 Scale(double s) => Scale(s, s, s);
    public static Mat4 Scale(double x, double y, double z) => new(x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1);
    public static Mat4 RotateX(double r) { var c = Math.Cos(r); var s = Math.Sin(r); return new(1, 0, 0, 0, 0, c, -s, 0, 0, s, c, 0, 0, 0, 0, 1); }
    public static Mat4 RotateY(double r) { var c = Math.Cos(r); var s = Math.Sin(r); return new(c, 0, s, 0, 0, 1, 0, 0, -s, 0, c, 0, 0, 0, 0, 1); }
    public static Mat4 RotateZ(double r) { var c = Math.Cos(r); var s = Math.Sin(r); return new(c, -s, 0, 0, s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1); }

    /// <summary>Rotation of <paramref name="radians"/> around an axis through the origin (Rodrigues).</summary>
    public static Mat4 RotateAxis(Vec3 axis, double radians)
    {
        var a = axis.Normalized();
        double c = Math.Cos(radians), s = Math.Sin(radians), t = 1 - c;
        return new(
            t * a.X * a.X + c, t * a.X * a.Y - s * a.Z, t * a.X * a.Z + s * a.Y, 0,
            t * a.X * a.Y + s * a.Z, t * a.Y * a.Y + c, t * a.Y * a.Z - s * a.X, 0,
            t * a.X * a.Z - s * a.Y, t * a.Y * a.Z + s * a.X, t * a.Z * a.Z + c, 0,
            0, 0, 0, 1);
    }

    /// <summary>Rotation about an axis passing through <paramref name="origin"/>.</summary>
    public static Mat4 RotateAround(Vec3 origin, Vec3 axis, double radians) =>
        Translation3(origin) * RotateAxis(axis, radians) * Translation3(-origin);

    /// <summary>Mirror across a plane through <paramref name="origin"/> with the given normal.</summary>
    public static Mat4 Mirror(Vec3 origin, Vec3 normal)
    {
        var n = normal.Normalized();
        var m = new Mat4(
            1 - 2 * n.X * n.X, -2 * n.X * n.Y, -2 * n.X * n.Z, 0,
            -2 * n.X * n.Y, 1 - 2 * n.Y * n.Y, -2 * n.Y * n.Z, 0,
            -2 * n.X * n.Z, -2 * n.Y * n.Z, 1 - 2 * n.Z * n.Z, 0,
            0, 0, 0, 1);
        return Translation3(origin) * m * Translation3(-origin);
    }

    /// <summary>Frame whose local X/Y/Z map to the given world axes, placed at origin.</summary>
    public static Mat4 FromBasis(Vec3 origin, Vec3 xAxis, Vec3 yAxis, Vec3 zAxis) => new(
        xAxis.X, yAxis.X, zAxis.X, origin.X,
        xAxis.Y, yAxis.Y, zAxis.Y, origin.Y,
        xAxis.Z, yAxis.Z, zAxis.Z, origin.Z,
        0, 0, 0, 1);

    public static Mat4 operator *(Mat4 a, Mat4 b) => new(
        a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41,
        a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42,
        a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43,
        a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,
        a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41,
        a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42,
        a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43,
        a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,
        a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41,
        a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42,
        a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43,
        a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,
        a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41,
        a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42,
        a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43,
        a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44);

    public Vec3 TransformPoint(Vec3 p)
    {
        double x = M11 * p.X + M12 * p.Y + M13 * p.Z + M14;
        double y = M21 * p.X + M22 * p.Y + M23 * p.Z + M24;
        double z = M31 * p.X + M32 * p.Y + M33 * p.Z + M34;
        double w = M41 * p.X + M42 * p.Y + M43 * p.Z + M44;
        return w == 1 || w == 0 ? new(x, y, z) : new(x / w, y / w, z / w);
    }

    public Vec3 TransformVector(Vec3 v) => new(
        M11 * v.X + M12 * v.Y + M13 * v.Z,
        M21 * v.X + M22 * v.Y + M23 * v.Z,
        M31 * v.X + M32 * v.Y + M33 * v.Z);

    /// <summary>Determinant of the upper-left 3x3 (negative = mirrors / flips winding).</summary>
    public double Determinant3 =>
        M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31);

    /// <summary>General 4x4 inverse.</summary>
    public Mat4 Inverse()
    {
        Span<double> m = [M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44];
        Span<double> inv = stackalloc double[16];
        inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
        inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
        inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
        inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
        inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
        inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
        inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
        inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
        inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
        inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
        inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
        inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
        inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
        inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
        inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
        inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];
        double det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
        if (Math.Abs(det) < 1e-300) throw new InvalidOperationException("Matrix is singular.");
        det = 1.0 / det;
        for (int i = 0; i < 16; i++) inv[i] *= det;
        return new(inv[0], inv[1], inv[2], inv[3], inv[4], inv[5], inv[6], inv[7], inv[8], inv[9], inv[10], inv[11], inv[12], inv[13], inv[14], inv[15]);
    }

    /// <summary>Normal matrix: transforms a normal by the inverse-transpose of the linear part.</summary>
    public Vec3 TransformNormal(Vec3 n)
    {
        var inv = Inverse();
        return new Vec3(
            inv.M11 * n.X + inv.M21 * n.Y + inv.M31 * n.Z,
            inv.M12 * n.X + inv.M22 * n.Y + inv.M32 * n.Z,
            inv.M13 * n.X + inv.M23 * n.Y + inv.M33 * n.Z).Normalized();
    }

    public double[] ToArray() => [M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44];

    /// <summary>Builds from a column-major array (as used by glTF / OpenGL).</summary>
    public static Mat4 FromColumnMajor(ReadOnlySpan<double> c) => new(
        c[0], c[4], c[8], c[12], c[1], c[5], c[9], c[13], c[2], c[6], c[10], c[14], c[3], c[7], c[11], c[15]);

    public bool Equals(Mat4 o) => ToArray().AsSpan().SequenceEqual(o.ToArray());
    public override bool Equals(object? obj) => obj is Mat4 m && Equals(m);
    public override int GetHashCode() => HashCode.Combine(M11, M22, M33, M14, M24, M34);
    public static bool operator ==(Mat4 a, Mat4 b) => a.Equals(b);
    public static bool operator !=(Mat4 a, Mat4 b) => !a.Equals(b);
}

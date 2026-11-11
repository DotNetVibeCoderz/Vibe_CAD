namespace Gravicode.CAD.Geometry;

/// <summary>Closed, outward-oriented primitive solids and sweep operations producing <see cref="Mesh"/>es.</summary>
public static class Shapes
{
    /// <summary>Axis-aligned box. When <paramref name="centered"/> the box is centred on the origin, otherwise it starts at the origin.</summary>
    public static Mesh Box(double width, double depth, double height, bool centered = true)
    {
        double x0 = centered ? -width / 2 : 0, y0 = centered ? -depth / 2 : 0, z0 = centered ? -height / 2 : 0;
        double x1 = x0 + width, y1 = y0 + depth, z1 = z0 + height;
        var m = new Mesh(8, 12);
        m.AddVertex(new(x0, y0, z0)); m.AddVertex(new(x1, y0, z0)); m.AddVertex(new(x1, y1, z0)); m.AddVertex(new(x0, y1, z0));
        m.AddVertex(new(x0, y0, z1)); m.AddVertex(new(x1, y0, z1)); m.AddVertex(new(x1, y1, z1)); m.AddVertex(new(x0, y1, z1));
        int[] q = [0, 3, 2, 1, /*bottom*/ 4, 5, 6, 7, /*top*/ 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7];
        for (int i = 0; i < q.Length; i += 4) { m.AddTriangle(q[i], q[i + 1], q[i + 2]); m.AddTriangle(q[i], q[i + 2], q[i + 3]); }
        return m;
    }

    public static Mesh Cube(double size, bool centered = true) => Box(size, size, size, centered);

    /// <summary>Cylinder along +Z with its base on z=0 (or centred when <paramref name="centered"/>).</summary>
    public static Mesh Cylinder(double radius, double height, int segments = 0, bool centered = false) =>
        Cone(radius, radius, height, segments, centered);

    /// <summary>Truncated cone (frustum) along +Z. A zero radius makes a pointed cone.</summary>
    public static Mesh Cone(double bottomRadius, double topRadius, double height, int segments = 0, bool centered = false)
    {
        if (segments <= 0) segments = AutoSegments(Math.Max(bottomRadius, topRadius));
        double z0 = centered ? -height / 2 : 0, z1 = z0 + height;
        var m = new Mesh(segments * 2 + 2, segments * 4);
        int cb = m.AddVertex(new(0, 0, z0)), ct = m.AddVertex(new(0, 0, z1));
        int start = m.VertexCount;
        for (int i = 0; i < segments; i++)
        {
            double a = 2 * Math.PI * i / segments;
            m.AddVertex(new(bottomRadius * Math.Cos(a), bottomRadius * Math.Sin(a), z0));
            m.AddVertex(new(topRadius * Math.Cos(a), topRadius * Math.Sin(a), z1));
        }
        for (int i = 0; i < segments; i++)
        {
            int b0 = start + 2 * i, t0 = b0 + 1, b1 = start + 2 * ((i + 1) % segments), t1 = b1 + 1;
            if (bottomRadius > 0) m.AddTriangle(cb, b1, b0);
            if (topRadius > 0) m.AddTriangle(ct, t0, t1);
            if (bottomRadius > 0 && topRadius > 0) { m.AddTriangle(b0, b1, t1); m.AddTriangle(b0, t1, t0); }
            else if (topRadius <= 0) m.AddTriangle(b0, b1, t0);
            else m.AddTriangle(b0, t1, t0);
        }
        return topRadius > 0 && bottomRadius > 0 ? m : m.Welded(1e-9);
    }

    public static Mesh Sphere(double radius, int segments = 0, int rings = 0)
    {
        if (segments <= 0) segments = AutoSegments(radius);
        if (rings <= 0) rings = Math.Max(6, segments / 2);
        var m = new Mesh();
        int top = m.AddVertex(new(0, 0, radius));
        for (int r = 1; r < rings; r++)
        {
            double phi = Math.PI * r / rings;
            for (int s = 0; s < segments; s++)
            {
                double th = 2 * Math.PI * s / segments;
                m.AddVertex(new(radius * Math.Sin(phi) * Math.Cos(th), radius * Math.Sin(phi) * Math.Sin(th), radius * Math.Cos(phi)));
            }
        }
        int bottom = m.AddVertex(new(0, 0, -radius));
        int V(int r, int s) => 1 + (r - 1) * segments + (s % segments);
        for (int s = 0; s < segments; s++)
        {
            m.AddTriangle(top, V(1, s), V(1, s + 1));
            m.AddTriangle(bottom, V(rings - 1, s + 1), V(rings - 1, s));
        }
        for (int r = 1; r < rings - 1; r++)
            for (int s = 0; s < segments; s++)
            {
                m.AddTriangle(V(r, s), V(r + 1, s), V(r + 1, s + 1));
                m.AddTriangle(V(r, s), V(r + 1, s + 1), V(r, s + 1));
            }
        return m;
    }

    public static Mesh Torus(double majorRadius, double minorRadius, int majorSegments = 0, int minorSegments = 0)
    {
        if (majorSegments <= 0) majorSegments = AutoSegments(majorRadius + minorRadius);
        if (minorSegments <= 0) minorSegments = Math.Max(12, AutoSegments(minorRadius));
        var m = new Mesh(majorSegments * minorSegments, majorSegments * minorSegments * 2);
        for (int i = 0; i < majorSegments; i++)
        {
            double u = 2 * Math.PI * i / majorSegments;
            for (int j = 0; j < minorSegments; j++)
            {
                double v = 2 * Math.PI * j / minorSegments;
                double rr = majorRadius + minorRadius * Math.Cos(v);
                m.AddVertex(new(rr * Math.Cos(u), rr * Math.Sin(u), minorRadius * Math.Sin(v)));
            }
        }
        int I(int i, int j) => (i % majorSegments) * minorSegments + (j % minorSegments);
        for (int i = 0; i < majorSegments; i++)
            for (int j = 0; j < minorSegments; j++)
            {
                m.AddTriangle(I(i, j), I(i + 1, j), I(i + 1, j + 1));
                m.AddTriangle(I(i, j), I(i + 1, j + 1), I(i, j + 1));
            }
        return m;
    }

    /// <summary>Hollow cylinder (pipe / bushing).</summary>
    public static Mesh Tube(double outerRadius, double innerRadius, double height, int segments = 0)
    {
        if (segments <= 0) segments = AutoSegments(outerRadius);
        var prof = new Profile2(Polygon2.Circle(outerRadius, segments), [Polygon2.Circle(innerRadius, segments)]);
        return Extrude(prof, height);
    }

    /// <summary>Square pyramid / wedge helper.</summary>
    public static Mesh Pyramid(double baseSize, double height) => Cone(baseSize / Math.Sqrt(2), 0, height, 4).Rotated(Vec3.UnitZ, 45);

    /// <summary>Linear extrusion of a profile along +Z from z=0 to z=<paramref name="height"/>.</summary>
    public static Mesh Extrude(Profile2 profile, double height)
    {
        var (verts, tris) = Triangulator.Triangulate(profile);
        var m = new Mesh(verts.Count * 2, tris.Count / 3 * 2 + verts.Count * 2);
        int n = verts.Count;
        foreach (var v in verts) m.AddVertex(new(v.X, v.Y, 0));
        foreach (var v in verts) m.AddVertex(new(v.X, v.Y, height));
        for (int i = 0; i < tris.Count; i += 3)
        {
            m.AddTriangle(tris[i], tris[i + 2], tris[i + 1]);           // bottom (facing -Z)
            m.AddTriangle(n + tris[i], n + tris[i + 1], n + tris[i + 2]); // top
        }
        // side walls: outer is CCW, holes CW -> same rule gives outward normals for both
        int offset = 0;
        AddWalls(profile.Outer.Count);
        foreach (var h in profile.Holes) AddWalls(h.Count);
        return m;

        void AddWalls(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int a = offset + i, b = offset + (i + 1) % count;
                m.AddTriangle(a, b, n + b);
                m.AddTriangle(a, n + b, n + a);
            }
            offset += count;
        }
    }

    /// <summary>Extrusion with draft / twist: the top section is scaled and rotated (degrees).</summary>
    public static Mesh ExtrudeTwisted(Polygon2 section, double height, double twistDegrees, double topScale = 1, int steps = 16)
    {
        var sec = section.AsCounterClockwise();
        var rings = new List<IReadOnlyList<Vec3>>();
        for (int s = 0; s <= steps; s++)
        {
            double t = (double)s / steps;
            double ang = Units.Deg(twistDegrees) * t, sc = 1 + (topScale - 1) * t;
            rings.Add([.. sec.Points.Select(p => (p.Rotate(ang) * sc).ToVec3(height * t))]);
        }
        return LoftRings(rings, capEnds: true);
    }

    /// <summary>
    /// Revolves a profile drawn in the XZ half-plane (X = radius ≥ 0, Y of each point = Z height)
    /// around the Z axis. A full 360° revolve of a closed profile yields a closed solid.
    /// </summary>
    public static Mesh Revolve(IReadOnlyList<Vec2> profile, double angleDegrees = 360, int segments = 0)
    {
        double maxR = profile.Max(p => p.X);
        if (segments <= 0) segments = AutoSegments(maxR) * (int)Math.Ceiling(Math.Abs(angleDegrees) / 360.0);
        bool full = Math.Abs(Math.Abs(angleDegrees) - 360) < 1e-9;
        var poly = new Polygon2(profile);
        // orient so that revolving CCW about Z gives outward normals
        if (!poly.IsCounterClockwise) poly = poly.Reversed();
        int pc = poly.Count;
        int rings = full ? segments : segments + 1;
        var m = new Mesh(pc * rings, pc * segments * 2);
        double total = Units.Deg(angleDegrees);
        for (int s = 0; s < rings; s++)
        {
            double a = total * s / segments;
            double c = Math.Cos(a), sn = Math.Sin(a);
            foreach (var p in poly.Points) m.AddVertex(new(p.X * c, p.X * sn, p.Y));
        }
        for (int s = 0; s < segments; s++)
        {
            int r0 = s * pc, r1 = ((s + 1) % rings) * pc;
            for (int i = 0; i < pc; i++)
            {
                int i1 = (i + 1) % pc;
                m.AddTriangle(r0 + i, r1 + i, r1 + i1);
                m.AddTriangle(r0 + i, r1 + i1, r0 + i1);
            }
        }
        if (!full)
        {
            var (v2, t2) = Triangulator.Triangulate(poly);
            // start cap faces -Y (angle 0), end cap faces along the rotated +Y direction
            int lastRing = (rings - 1) * pc;
            for (int i = 0; i < t2.Count; i += 3)
            {
                m.AddTriangle(t2[i], t2[i + 1], t2[i + 2]);
                m.AddTriangle(lastRing + t2[i], lastRing + t2[i + 2], lastRing + t2[i + 1]);
            }
        }
        m = m.Welded(1e-9);
        return m.Volume < 0 ? m.Flipped() : m;
    }

    /// <summary>Lofts between two sections at z=0 and z=height (they are resampled to the same vertex count).</summary>
    public static Mesh Loft(Polygon2 bottom, Polygon2 top, double height)
    {
        int n = Math.Max(bottom.Count, top.Count);
        var b = Resample(bottom.AsCounterClockwise(), n);
        var t = Resample(top.AsCounterClockwise(), n);
        return LoftRings([b.Select(p => p.ToVec3(0)).ToList(), t.Select(p => p.ToVec3(height)).ToList()], capEnds: true);
    }

    /// <summary>
    /// Sweeps a closed 2D section along a 3D path using rotation-minimising frames.
    /// The section's X/Y axes map to the frame's normal/binormal.
    /// </summary>
    public static Mesh Sweep(Polygon2 section, ICurve3 path, double tolerance = 0.05, bool capEnds = true)
    {
        var pts = path.Tessellate(tolerance);
        if (pts.Count < 2) throw new ArgumentException("Path is too short.");
        var sec = section.AsCounterClockwise();
        var rings = new List<IReadOnlyList<Vec3>>(pts.Count);
        var tan = (pts[1] - pts[0]).Normalized();
        var nrm = tan.AnyPerpendicular();
        for (int i = 0; i < pts.Count; i++)
        {
            var newTan = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).Normalized() : tan;
            if (i > 0)
            {
                var t2 = (tan + newTan).Normalized();
                if (t2.LengthSquared < 0.5) t2 = newTan;
                // double reflection (Wang et al.) for a rotation-minimising frame
                var v1 = pts[i] - pts[i - 1];
                double c1 = Vec3.Dot(v1, v1);
                if (c1 > 1e-20)
                {
                    var rL = nrm - v1 * (2 / c1 * Vec3.Dot(v1, nrm));
                    var tL = tan - v1 * (2 / c1 * Vec3.Dot(v1, tan));
                    var v2 = t2 - tL;
                    double c2 = Vec3.Dot(v2, v2);
                    nrm = c2 > 1e-20 ? (rL - v2 * (2 / c2 * Vec3.Dot(v2, rL))).Normalized() : rL.Normalized();
                }
                tan = t2;
            }
            var bin = Vec3.Cross(tan, nrm);
            var o = pts[i];
            rings.Add([.. sec.Points.Select(p => o + nrm * p.X + bin * p.Y)]);
            if (i < pts.Count - 1) tan = i == 0 ? tan : tan;
        }
        return LoftRings(rings, capEnds);
    }

    /// <summary>Skins a sequence of equally sized vertex rings; optional planar end caps.</summary>
    public static Mesh LoftRings(IReadOnlyList<IReadOnlyList<Vec3>> rings, bool capEnds)
    {
        int n = rings[0].Count;
        var m = new Mesh(n * rings.Count, n * rings.Count * 2);
        foreach (var r in rings) foreach (var p in r) m.AddVertex(p);
        for (int s = 0; s + 1 < rings.Count; s++)
            for (int i = 0; i < n; i++)
            {
                int a = s * n + i, b = s * n + (i + 1) % n, c = (s + 1) * n + (i + 1) % n, d = (s + 1) * n + i;
                m.AddTriangle(a, b, c);
                m.AddTriangle(a, c, d);
            }
        if (capEnds)
        {
            CapRing(m, rings[0], 0, flip: true);
            CapRing(m, rings[^1], (rings.Count - 1) * n, flip: false);
        }
        // fix global orientation if needed
        return m.Volume < 0 ? m.Flipped() : m;
    }

    private static void CapRing(Mesh m, IReadOnlyList<Vec3> ring, int offset, bool flip)
    {
        // Newell normal -> project to best-fit plane and triangulate
        Vec3 nrm = Vec3.Zero;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            nrm += new Vec3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        nrm = nrm.Normalized();
        var u = nrm.AnyPerpendicular(); var v = Vec3.Cross(nrm, u);
        var poly = new Polygon2(ring.Select(p => new Vec2(Vec3.Dot(p, u), Vec3.Dot(p, v))));
        bool reversed = !poly.IsCounterClockwise;
        var (_, tris) = Triangulator.Triangulate(poly.AsCounterClockwise());
        int cnt = ring.Count;
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            if (reversed) { a = cnt - 1 - a; b = cnt - 1 - b; c = cnt - 1 - c; (b, c) = (c, b); }
            if (flip) m.AddTriangle(offset + a, offset + c, offset + b);
            else m.AddTriangle(offset + a, offset + b, offset + c);
        }
    }

    public static List<Vec2> Resample(Polygon2 poly, int count)
    {
        double per = poly.Perimeter;
        var r = new List<Vec2>(count);
        int seg = 0; double acc = 0;
        for (int k = 0; k < count; k++)
        {
            double target = per * k / count;
            while (true)
            {
                double l = Vec2.Distance(poly[seg], poly[seg + 1]);
                if (acc + l >= target - 1e-12 || seg >= poly.Count - 1) { double t = l > 0 ? (target - acc) / l : 0; r.Add(Vec2.Lerp(poly[seg], poly[seg + 1], Math.Clamp(t, 0, 1))); break; }
                acc += l; seg++;
            }
        }
        return r;
    }

    // ---------- mechanical profiles ----------

    /// <summary>Involute spur gear outline (metric module, pressure angle in degrees).</summary>
    public static Polygon2 SpurGearProfile(double module, int teeth, double pressureAngle = 20, int pointsPerFlank = 8)
    {
        double rp = module * teeth / 2;                 // pitch radius
        double rb = rp * Math.Cos(Units.Deg(pressureAngle)); // base radius
        double ra = rp + module;                        // addendum
        double rf = Math.Max(rp - 1.25 * module, rb * 0.9); // dedendum
        double toothAngle = 2 * Math.PI / teeth;
        // involute angle at pitch circle
        double invAlpha = Math.Tan(Units.Deg(pressureAngle)) - Units.Deg(pressureAngle);
        double halfTooth = Math.PI / (2 * teeth) + invAlpha; // half angular thickness at base circle
        double tMax = Math.Sqrt(Math.Max(0, (ra * ra) / (rb * rb) - 1));
        var pts = new List<Vec2>();
        for (int k = 0; k < teeth; k++)
        {
            double c = k * toothAngle;
            // root -> left flank (rising)
            var flank = new List<Vec2>();
            for (int i = 0; i <= pointsPerFlank; i++)
            {
                double t = tMax * i / pointsPerFlank;
                double r = rb * Math.Sqrt(1 + t * t);
                double ang = t - Math.Atan(t);
                flank.Add(Vec2.FromAngle(ang, r));
            }
            pts.Add(Vec2.FromAngle(c - halfTooth - (toothAngle / 2 - halfTooth) * 0.5, rf));
            pts.Add(Vec2.FromAngle(c - halfTooth, Math.Max(rf, Math.Min(rb, rf + 0.01))));
            foreach (var p in flank) pts.Add(Vec2.FromAngle(c - halfTooth + p.Angle, p.Length));
            for (int i = flank.Count - 1; i >= 0; i--) pts.Add(Vec2.FromAngle(c + halfTooth - flank[i].Angle, flank[i].Length));
            pts.Add(Vec2.FromAngle(c + halfTooth, Math.Max(rf, Math.Min(rb, rf + 0.01))));
        }
        return new Polygon2(pts).Simplified(1e-9);
    }

    /// <summary>Extruded spur gear with optional centre bore.</summary>
    public static Mesh SpurGear(double module, int teeth, double thickness, double boreDiameter = 0, double pressureAngle = 20)
    {
        var prof = new Profile2(SpurGearProfile(module, teeth, pressureAngle));
        if (boreDiameter > 0) prof.WithHole(Polygon2.Circle(boreDiameter / 2, 48));
        return Extrude(prof, thickness);
    }

    /// <summary>Hex nut blank (across-flats, thickness, hole diameter).</summary>
    public static Mesh HexNut(double acrossFlats, double thickness, double holeDiameter)
    {
        double R = acrossFlats / Math.Sqrt(3);
        var prof = new Profile2(Polygon2.RegularPolygon(6, R), [Polygon2.Circle(holeDiameter / 2, 36)]);
        return Extrude(prof, thickness);
    }

    internal static int AutoSegments(double radius) => Math.Clamp(CurveMath.ArcSegments(Math.Max(radius, 0.1), 2 * Math.PI, Math.Max(radius * 0.002, 0.01)), 24, 128);
}

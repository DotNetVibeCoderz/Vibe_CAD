namespace Gravicode.CAD.Studio;

/// <summary>A picked solid: its id, the distance along the ray and the hit point (CAD space, Z-up).</summary>
public readonly record struct SolidHit(Guid Id, double Distance, Vec3 Point, Vec3 Normal);

/// <summary>3D selection and gizmo maths shared by the web and desktop viewports (CAD space, Z-up).</summary>
public static class Solid3D
{
    /// <summary>Nearest visible solid hit by a ray (Möller–Trumbore, bounding-box culled), or null.</summary>
    public static SolidHit? Raycast(StudioDocument doc, Vec3 origin, Vec3 direction)
    {
        var d = direction.Normalized();
        if (d.LengthSquared < 0.5) return null;
        SolidHit? best = null;
        foreach (var s in doc.VisibleSolids)
        {
            if (doc.LayerOf(s.Layer).Locked) continue;
            var bb = s.Mesh.Bounds;
            if (!RayBox(origin, d, bb, out double tBox) || (best is { } b0 && tBox > b0.Distance)) continue;
            var pos = s.Mesh.Positions; var idx = s.Mesh.Indices;
            for (int i = 0; i + 2 < idx.Count; i += 3)
                if (RayTriangle(origin, d, pos[idx[i]], pos[idx[i + 1]], pos[idx[i + 2]], out double t) && (best is null || t < best.Value.Distance))
                    best = new SolidHit(s.Id, t, origin + d * t, Vec3.Cross(pos[idx[i + 1]] - pos[idx[i]], pos[idx[i + 2]] - pos[idx[i]]).Normalized());
        }
        return best;
    }

    private static bool RayTriangle(Vec3 o, Vec3 d, Vec3 a, Vec3 b, Vec3 c, out double t)
    {
        t = 0;
        var e1 = b - a; var e2 = c - a;
        var p = Vec3.Cross(d, e2);
        double det = Vec3.Dot(e1, p);
        if (Math.Abs(det) < 1e-14) return false;
        double inv = 1 / det;
        var s = o - a;
        double u = Vec3.Dot(s, p) * inv;
        if (u < 0 || u > 1) return false;
        var q = Vec3.Cross(s, e1);
        double v = Vec3.Dot(d, q) * inv;
        if (v < 0 || u + v > 1) return false;
        t = Vec3.Dot(e2, q) * inv;
        return t > 1e-9;
    }

    /// <summary>Slab test; <paramref name="tNear"/> is the entry distance (0 when the origin is inside).</summary>
    private static bool RayBox(Vec3 o, Vec3 d, BoundingBox bb, out double tNear)
    {
        tNear = 0;
        if (bb.IsEmpty) return false;
        double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity;
        Span<double> oo = [o.X, o.Y, o.Z], dd = [d.X, d.Y, d.Z], lo = [bb.Min.X, bb.Min.Y, bb.Min.Z], hi = [bb.Max.X, bb.Max.Y, bb.Max.Z];
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(dd[i]) < 1e-15) { if (oo[i] < lo[i] || oo[i] > hi[i]) return false; continue; }
            double a = (lo[i] - oo[i]) / dd[i], b = (hi[i] - oo[i]) / dd[i];
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
            if (t0 > t1) return false;
        }
        if (t1 < 0) return false;
        tNear = Math.Max(0, t0);
        return true;
    }

    /// <summary>Bounds of the given solids (the gizmo sits at its centre).</summary>
    public static BoundingBox Bounds(StudioDocument doc, IEnumerable<Guid> ids)
    {
        var set = ids as IReadOnlySet<Guid> ?? ids.ToHashSet();
        var bb = BoundingBox.Empty;
        foreach (var s in doc.Solids) if (set.Contains(s.Id)) bb = bb.Union(s.Mesh.Bounds);
        return bb;
    }

    /// <summary>Applies a rigid transform to the given solids as one undoable edit. Returns how many changed.</summary>
    public static int Transform(StudioDocument doc, IEnumerable<Guid> ids, Mat4 m, string label)
    {
        var set = ids.ToHashSet();
        int n = doc.Solids.Count(s => set.Contains(s.Id));
        if (n == 0) return 0;
        doc.Edit(label, d =>
        {
            for (int i = 0; i < d.Solids.Count; i++)
                if (set.Contains(d.Solids[i].Id))
                {
                    var s = d.Solids[i];
                    d.Solids[i] = new StudioSolid(s.Id, s.Name, s.Layer, s.Mesh.Transformed(m), s.Color, s.Metallic, s.Roughness);
                }
        });
        return n;
    }

    /// <summary>World axis by name: X, Y or Z.</summary>
    public static Vec3 Axis(string name) => name.ToUpperInvariant() switch { "X" => Vec3.UnitX, "Y" => Vec3.UnitY, _ => Vec3.UnitZ };
}

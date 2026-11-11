using System.Globalization;
using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Studio;

/// <summary>One editable number of a feature.</summary>
public sealed record FeatureParam(string Name, double Value);

/// <summary>
/// Parametric history of a solid: a tree of features evaluated into a mesh. Leaves are primitives and sketch-based
/// features (box, cylinder, cone, sphere, torus, gear, extrude, revolve); inner nodes combine (union, subtract,
/// intersect) or place (transform) their children. Records are immutable — editing makes a new tree.
/// </summary>
/// <param name="Loops">Sketch profile for extrude (outer then holes) and revolve (one profile), local 2D.</param>
/// <param name="Matrix">Row-major 4×4 for transform features.</param>
public sealed record Feature(string Kind, IReadOnlyList<FeatureParam> Params, IReadOnlyList<Feature> Children,
    IReadOnlyList<IReadOnlyList<Vec2>>? Loops = null, double[]? Matrix = null)
{
    public double this[string name] => Params.FirstOrDefault(p => p.Name == name)?.Value ?? 0;

    public static Feature Leaf(string kind, params (string Name, double Value)[] ps) =>
        new(kind, [.. ps.Select(p => new FeatureParam(p.Name, p.Value))], []);

    public static Feature Combine(string op, IEnumerable<Feature> children) => new(op, [], [.. children]);

    /// <summary>Wraps <paramref name="child"/> in a placement; consecutive placements merge into one.</summary>
    public static Feature Transform(Mat4 m, Feature child) =>
        child.Kind == "transform" && child.Matrix is { } inner
            ? child with { Matrix = (m * Features.ToMat(inner)).ToArray() }
            : new Feature("transform", [], [child], Matrix: m.ToArray());

    public string Title => Kind switch
    {
        "box" => "Box", "cylinder" => "Cylinder", "cone" => "Cone", "sphere" => "Sphere", "torus" => "Torus", "gear" => "Gear",
        "extrude" => "Extrude", "revolve" => "Revolve", "union" => "Union", "subtract" => "Subtract", "intersect" => "Intersect",
        "transform" => "Placement", _ => Kind
    };
}

/// <summary>Evaluation and editing of <see cref="Feature"/> trees.</summary>
public static class Features
{
    public static Mat4 ToMat(double[] a) => new(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15]);

    /// <summary>Builds the mesh of a feature tree (the same calls the commands use, so results are identical).</summary>
    public static Mesh Evaluate(Feature f) => f.Kind switch
    {
        "box" => Shapes.Box(Pos(f["width"]), Pos(f["depth"]), Pos(f["height"]), false).Translated(f["x"], f["y"], f["z"]),
        "cylinder" => Shapes.Cylinder(Pos(f["radius"]), Pos(f["height"])).Translated(f["x"], f["y"], f["z"]),
        "cone" => Shapes.Cone(Pos(f["radius"]), Math.Max(0, f["top radius"]), Pos(f["height"])).Translated(f["x"], f["y"], f["z"]),
        "sphere" => Shapes.Sphere(Pos(f["radius"])).Translated(f["x"], f["y"], f["z"] + f["radius"]),
        "torus" => Shapes.Torus(Pos(f["radius"]), Math.Min(Pos(f["tube"]), Pos(f["radius"]) * 0.99)).Translated(f["x"], f["y"], f["z"] + f["tube"]),
        "gear" => Shapes.SpurGear(Pos(f["module"]), Math.Clamp((int)Math.Round(f["teeth"]), 6, 400), Pos(f["width"]), Math.Max(0, f["bore"])).Translated(f["x"], f["y"], f["z"]),
        "extrude" => Extrude(f),
        "revolve" => Shapes.Revolve([.. f.Loops![0]], Math.Clamp(f["angle"], 1, 360)),
        "union" => f.Children.Count == 1 ? Evaluate(f.Children[0]) : Csg.UnionAll(f.Children.Select(Evaluate)),
        "intersect" => f.Children.Skip(1).Aggregate(Evaluate(f.Children[0]), (m, c) => Csg.Intersect(m, Evaluate(c))),
        "subtract" => Csg.Subtract(Evaluate(f.Children[0]), f.Children.Count == 2 ? Evaluate(f.Children[1]) : Csg.UnionAll(f.Children.Skip(1).Select(Evaluate))),
        "transform" => Evaluate(f.Children[0]).Transformed(ToMat(f.Matrix!)),
        _ => throw new InvalidOperationException($"Unknown feature \"{f.Kind}\".")
    };

    private static double Pos(double v) => Math.Max(v, 1e-6);

    private static Mesh Extrude(Feature f)
    {
        double h = f["height"];
        var loops = f.Loops!.Select(l => new Polygon2(l)).ToList();
        var profiles = Profile2.FromLoops(loops);
        var meshes = profiles.Select(p => { var m = Shapes.Extrude(p, Math.Max(Math.Abs(h), 1e-6)); return h < 0 ? m.Translated(0, 0, h) : m; }).ToList();
        return meshes.Count == 1 ? meshes[0] : Csg.UnionAll(meshes);
    }

    /// <summary>An editable number found in a tree.</summary>
    public sealed record Entry(Feature Node, int Index, string Label, int Depth)
    {
        public double Value => Node.Params[Index].Value;
        public string Name => Node.Params[Index].Name;
    }

    /// <summary>All editable numbers in tree order, labelled like "Subtract › Cylinder · radius".</summary>
    public static List<Entry> Parameters(Feature root)
    {
        var list = new List<Entry>();
        void Walk(Feature f, string path, int depth)
        {
            var here = path.Length == 0 ? f.Title : $"{path} › {f.Title}";
            for (int i = 0; i < f.Params.Count; i++) list.Add(new Entry(f, i, $"{here} · {f.Params[i].Name}", depth));
            foreach (var c in f.Children) Walk(c, f.Kind == "transform" ? path : here, f.Kind == "transform" ? depth : depth + 1);
        }
        Walk(root, "", 0);
        return list;
    }

    /// <summary>A copy of the tree with one parameter changed (nodes are matched by reference).</summary>
    public static Feature With(Feature root, Feature node, int index, double value)
    {
        if (ReferenceEquals(root, node))
        {
            var ps = root.Params.ToList();
            ps[index] = ps[index] with { Value = value };
            return root with { Params = ps };
        }
        bool changed = false;
        var kids = root.Children.Select(c =>
        {
            var n = With(c, node, index, value);
            if (!ReferenceEquals(n, c)) changed = true;
            return n;
        }).ToList();
        return changed ? root with { Children = kids } : root;
    }

    /// <summary>One-line outline of a tree, e.g. "Subtract(Box, Cylinder)".</summary>
    public static string Describe(Feature f) =>
        f.Children.Count == 0 ? f.Title : $"{f.Title}({string.Join(", ", f.Children.Select(Describe))})";

    public static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

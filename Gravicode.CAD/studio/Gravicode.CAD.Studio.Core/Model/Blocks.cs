namespace Gravicode.CAD.Studio;

/// <summary>One object inside a block definition (geometry relative to the block's base point).</summary>
public sealed record BlockItem(DrawingEntity Geometry, string Layer, string? Color);

/// <summary>Named, reusable group of drawing objects (AutoCAD BLOCK). Definitions are immutable.</summary>
public sealed class BlockDefinition(string name, IEnumerable<BlockItem> items, string? description = null)
{
    public string Name { get; } = name;
    public IReadOnlyList<BlockItem> Items { get; } = [.. items];
    public string? Description { get; } = description;

    /// <summary>Builds a definition from model-space objects, moving <paramref name="basePoint"/> to the origin.</summary>
    public static BlockDefinition From(string name, Vec2 basePoint, IEnumerable<StudioEntity> entities) =>
        new(name, entities.Select(e => new BlockItem(e.Geometry.Transformed(p => p - basePoint), e.Layer, e.Color)));
}

/// <summary>Placed instance of a block (AutoCAD INSERT): position, per-axis scale and rotation.</summary>
public sealed class BlockRefEntity(BlockDefinition block, Vec2 position, double scaleX = 1, double scaleY = 1, double rotation = 0) : DrawingEntity
{
    public BlockDefinition Block { get; } = block;
    public Vec2 Position { get; } = position;
    public double ScaleX { get; } = scaleX;
    public double ScaleY { get; } = scaleY;
    /// <summary>Radians, counter-clockwise.</summary>
    public double Rotation { get; } = rotation;

    public Vec2 Map(Vec2 p) => Position + new Vec2(p.X * ScaleX, p.Y * ScaleY).Rotate(Rotation);

    private List<BlockItem>? _children;

    /// <summary>The block's objects placed in model space.</summary>
    public IReadOnlyList<BlockItem> Children() =>
        _children ??= [.. Block.Items.Select(i => i with { Geometry = i.Geometry.Transformed(Map) })];

    public override IReadOnlyList<Vec2> Tessellate(double chordTolerance = 0.05)
    {
        var pts = Children().SelectMany(c => c.Geometry.Tessellate(chordTolerance)).ToList();
        if (pts.Count == 0) pts.Add(Position);
        return pts;
    }

    public override DrawingEntity Transformed(Func<Vec2, Vec2> map)
    {
        var o = map(Position);
        var ux = map(Position + Vec2.FromAngle(Rotation, 1)) - o;
        var uy = map(Position + Vec2.FromAngle(Rotation + Math.PI / 2, 1)) - o;
        double flip = Vec2.Cross(ux, uy) < 0 ? -1 : 1;       // mirroring flips the block's Y axis
        return new BlockRefEntity(Block, o, ScaleX * ux.Length, ScaleY * uy.Length * flip, ux.Angle) { Layer = Layer, Color = Color };
    }
}

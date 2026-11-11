namespace Gravicode.CAD.Studio.Commands;

public enum PromptKind { Point, Distance, Integer, Text, Select, Keyword, Face }

/// <summary>What a running command asks the user for next.</summary>
public sealed record Prompt(PromptKind Kind, string Message)
{
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Reference point for relative input (@dx,dy), rubber-banding and ortho.</summary>
    public Vec2? Base { get; init; }
    /// <summary>Temporary geometry to draw for the current cursor position.</summary>
    public Func<Vec2, IEnumerable<DrawingEntity>>? Preview { get; init; }
    /// <summary>Enter / right-click finishes instead of being an error.</summary>
    public bool AllowEnd { get; init; }
    public double? Default { get; init; }
    /// <summary>Selection prompts: one pick completes the prompt.</summary>
    public bool Single { get; init; }
    /// <summary>Point prompts that pick a spot on an object (TRIM, FILLET…): object and grid snap are off so the
    /// pick stays where the user clicked.</summary>
    public bool NoSnap { get; init; }

    public string Display
    {
        get
        {
            var s = Message;
            if (Keywords.Count > 0) s += $" [{string.Join("/", Keywords)}]";
            if (Default is { } d) s += $" <{d:0.###}>";
            return s + ":";
        }
    }

    public static Prompt Point(string msg, Vec2? from = null, Func<Vec2, IEnumerable<DrawingEntity>>? preview = null, bool allowEnd = false, params string[] keywords) =>
        new(PromptKind.Point, msg) { Base = from, Preview = preview, AllowEnd = allowEnd, Keywords = keywords };
    public static Prompt Distance(string msg, Vec2? from = null, double? def = null, Func<Vec2, IEnumerable<DrawingEntity>>? preview = null) =>
        new(PromptKind.Distance, msg) { Base = from, Default = def, Preview = preview };
    /// <summary>Pick a spot on an object (no snapping); Enter finishes.</summary>
    public static Prompt Pick(string msg, Func<Vec2, IEnumerable<DrawingEntity>>? preview = null, params string[] keywords) =>
        new(PromptKind.Point, msg) { NoSnap = true, AllowEnd = true, Keywords = keywords, Preview = preview };
    /// <summary>Click a face of a solid in a 3D view.</summary>
    public static Prompt Face(string msg) => new(PromptKind.Face, msg) { AllowEnd = true };
    public static Prompt Integer(string msg, int def) => new(PromptKind.Integer, msg) { Default = def };
    public static Prompt Text(string msg) => new(PromptKind.Text, msg);
    public static Prompt Select(string msg = "Select objects") => new(PromptKind.Select, msg) { AllowEnd = true };
    public static Prompt Keyword(string msg, params string[] keywords) => new(PromptKind.Keyword, msg) { Keywords = keywords, AllowEnd = true };
}

/// <summary>User response to a prompt.</summary>
public abstract record Input
{
    public sealed record PointInput(Vec2 Value) : Input;
    public sealed record NumberInput(double Value) : Input;
    public sealed record TextInput(string Value) : Input;
    public sealed record KeywordInput(string Value) : Input;
    /// <summary>A face picked in a 3D view: the solid, the hit point and the outward normal (CAD space).</summary>
    public sealed record FaceInput(Guid Solid, Vec3 Point, Vec3 Normal) : Input;
    /// <summary>Enter / right click.</summary>
    public sealed record EndInput : Input;
}

namespace Gravicode.CAD.Studio.Commands;

public enum PromptKind { Point, Distance, Integer, Text, Select, Keyword }

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
    /// <summary>Enter / right click.</summary>
    public sealed record EndInput : Input;
}

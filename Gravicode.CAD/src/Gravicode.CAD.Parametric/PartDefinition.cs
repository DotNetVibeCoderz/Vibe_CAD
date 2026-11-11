using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gravicode.CAD.Geometry;

namespace Gravicode.CAD.Parametric;

/// <summary>
/// A parametric part: named parameters plus an ordered feature tree. Rebuilding evaluates the
/// parameters, builds each feature and combines it into its target body. The whole definition is
/// JSON-serialisable, which is also the format the AI module asks language models to produce.
/// </summary>
public sealed class PartDefinition
{
    public const string DefaultBody = "Part";

    public string Name { get; set; } = "Part";
    public string Units { get; set; } = "mm";
    public string? Description { get; set; }
    public Dictionary<string, Expr> Parameters { get; set; } = [];
    public List<Feature> Features { get; set; } = [];
    /// <summary>Chordal tolerance for curved features.</summary>
    public double Tolerance { get; set; } = 0.05;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static PartDefinition FromJson(string json) =>
        JsonSerializer.Deserialize<PartDefinition>(json, JsonOptions) ?? throw new JsonException("Empty part definition.");

    public Dictionary<string, double> ResolveParameters(IReadOnlyDictionary<string, double>? overrides = null)
    {
        var set = new ParameterSet();
        foreach (var (k, v) in Parameters) set.Set(k, v);
        if (overrides != null) foreach (var (k, v) in overrides) set.Set(k, v);
        return set.Resolve();
    }

    /// <summary>Rebuilds the feature tree into a <see cref="CadModel"/>.</summary>
    public RebuildResult Rebuild(IReadOnlyDictionary<string, double>? overrides = null)
    {
        var sw = Stopwatch.StartNew();
        var vars = ResolveParameters(overrides);
        var ctx = new FeatureContext(vars, Tolerance);
        var bodies = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        var colors = new Dictionary<string, string>();
        var order = new List<string>();
        var log = new List<FeatureLog>();
        ctx.Bodies = bodies;

        for (int i = 0; i < Features.Count; i++)
        {
            var f = Features[i];
            var id = f.Id ?? $"{f.GetType().Name.Replace("Feature", "").ToLowerInvariant()}{i + 1}";
            if (f.Suppressed) { log.Add(new(id, "suppressed", 0, null)); continue; }
            var t0 = sw.Elapsed;
            try
            {
                var name = f.Body ?? DefaultBody;
                var solid = f.Build(ctx);
                var op = f is PatternFeature { Source: { } src } ? src.Operation : f.Operation;
                if (!bodies.TryGetValue(name, out var current))
                {
                    if (op == FeatureOperation.Add) { bodies[name] = solid; order.Add(name); }
                    else log.Add(new(id, "skipped", 0, $"'{op}' with no existing body '{name}'"));
                }
                else
                {
                    bodies[name] = op switch
                    {
                        FeatureOperation.Subtract => Csg.Subtract(current, solid),
                        FeatureOperation.Intersect => Csg.Intersect(current, solid),
                        _ => Csg.Union(current, solid)
                    };
                }
                if (f.Color != null) colors[name] = f.Color;
                log.Add(new(id, "ok", (sw.Elapsed - t0).TotalMilliseconds, null));
            }
            catch (Exception ex)
            {
                log.Add(new(id, "error", (sw.Elapsed - t0).TotalMilliseconds, ex.Message));
            }
        }

        var model = new CadModel(Name) { Units = Units.ToLowerInvariant() switch { "cm" => LengthUnit.Centimeter, "m" => LengthUnit.Meter, "in" or "inch" => LengthUnit.Inch, _ => LengthUnit.Millimeter } };
        int ci = 0;
        CadColor[] palette = [CadColor.Steel, CadColor.Blueprint, CadColor.Orange, CadColor.Brass, CadColor.Copper, CadColor.Aluminum];
        foreach (var n in order)
        {
            var color = colors.TryGetValue(n, out var hex) ? CadColor.FromHex(hex) : palette[ci++ % palette.Length];
            model.AddBody(n, bodies[n], color);
        }
        foreach (var (k, v) in vars) model.Metadata[$"param.{k}"] = v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        if (Description != null) model.Metadata["description"] = Description;
        foreach (var l in log.Where(l => l.Status == "error")) model.Warnings.Add($"Feature {l.Id}: {l.Message}");
        return new RebuildResult(model, log, sw.Elapsed);
    }

    public CadModel Build(IReadOnlyDictionary<string, double>? overrides = null) => Rebuild(overrides).Model;
}

public sealed record FeatureLog(string Id, string Status, double Milliseconds, string? Message);
public sealed record RebuildResult(CadModel Model, IReadOnlyList<FeatureLog> Log, TimeSpan Elapsed)
{
    public bool Success => Log.All(l => l.Status != "error");
}

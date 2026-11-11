using System.Diagnostics;
using System.Text.RegularExpressions;
using Gravicode.CAD.IO;
using Gravicode.CAD.Parametric;

namespace Gravicode.CAD.AI;

/// <summary>Result of a text-to-CAD request.</summary>
public sealed class TextToCadResult
{
    public required string Prompt { get; init; }
    public required PartDefinition Definition { get; init; }
    public required CadModel Model { get; init; }
    public required string Provider { get; init; }
    public string? Explanation { get; init; }
    public int Attempts { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<FeatureLog> Log { get; init; } = [];
    public bool Success => Model.Bodies.Count > 0 && Log.All(l => l.Status != "error");

    /// <summary>Writes the part as CAD model JSON, STEP, STL, GLB and a PDF + SVG drawing. Returns the written paths.</summary>
    public IReadOnlyList<string> ExportAll(string directory, string? baseName = null)
    {
        Directory.CreateDirectory(directory);
        var name = baseName ?? Sanitize(Definition.Name);
        var files = new List<string>();
        var json = Path.Combine(directory, name + ".gcad.json");
        File.WriteAllText(json, Definition.ToJson());
        files.Add(json);
        foreach (var ext in new[] { "step", "stl", "glb", "pdf", "svg" })
        {
            var p = Path.Combine(directory, $"{name}.{ext}");
            Model.Save(p, new ExportOptions { Title = Definition.Name });
            files.Add(p);
        }
        return files;
    }

    private static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}

/// <summary>
/// Text-to-CAD agent: asks a language model for a <see cref="PartDefinition"/>, rebuilds it with the parametric kernel,
/// and feeds rebuild errors back to the model for repair. Without a model it uses the offline <see cref="RuleBasedCadInterpreter"/>.
/// </summary>
public sealed partial class TextToCadAgent(ICadLanguageModel? model = null)
{
    public ICadLanguageModel? LanguageModel { get; } = model;
    public int MaxRepairs { get; init; } = 2;

    public async Task<TextToCadResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        CadIO.RegisterAll();
        var sw = Stopwatch.StartNew();
        if (LanguageModel is null)
        {
            var def = RuleBasedCadInterpreter.Interpret(prompt);
            var rb = def.Rebuild();
            return new TextToCadResult { Prompt = prompt, Definition = def, Model = rb.Model, Provider = "Rule-based (offline)", Log = rb.Log, Attempts = 1, Elapsed = sw.Elapsed, Explanation = def.Description };
        }

        var convo = new List<(string Role, string Text)> { ("user", prompt) };
        PartDefinition? last = null; RebuildResult? lastBuild = null; string? explanation = null;
        int attempt = 0;
        for (; attempt <= MaxRepairs; attempt++)
        {
            var reply = await LanguageModel.CompleteAsync(CadPrompts.System, convo, ct);
            convo.Add(("assistant", reply));
            var problems = new List<string>();
            try
            {
                var (json, rest) = ExtractJson(reply);
                explanation = string.IsNullOrWhiteSpace(rest) ? null : rest.Trim();
                last = PartDefinition.FromJson(json);
                lastBuild = last.Rebuild();
                problems.AddRange(lastBuild.Log.Where(l => l.Status is "error" or "skipped").Select(l => $"feature '{l.Id}': {l.Status} {l.Message}"));
                if (lastBuild.Model.Bodies.Count == 0) problems.Add("no solid body was produced");
                else if (lastBuild.Model.ToMesh().Volume <= 0) problems.Add("the resulting solid has no volume");
            }
            catch (Exception ex) { problems.Add($"invalid JSON or definition: {ex.Message}"); }
            if (problems.Count == 0) break;
            if (attempt == MaxRepairs) break;
            convo.Add(("user", CadPrompts.Repair(problems)));
        }

        if (last is null || lastBuild is null)
        {
            // the model never produced a usable definition: degrade gracefully
            var def = RuleBasedCadInterpreter.Interpret(prompt);
            var rb = def.Rebuild();
            rb.Model.Warnings.Add($"{LanguageModel.Name} did not return a valid part; used the offline interpreter instead.");
            return new TextToCadResult { Prompt = prompt, Definition = def, Model = rb.Model, Provider = "Rule-based (fallback)", Log = rb.Log, Attempts = attempt + 1, Elapsed = sw.Elapsed };
        }
        return new TextToCadResult
        {
            Prompt = prompt, Definition = last, Model = lastBuild.Model, Provider = LanguageModel.Name,
            Explanation = explanation, Log = lastBuild.Log, Attempts = Math.Min(attempt + 1, MaxRepairs + 1), Elapsed = sw.Elapsed
        };
    }

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex FencedJson();

    /// <summary>Extracts the JSON object (fenced or bare) and returns the remaining prose.</summary>
    public static (string Json, string Prose) ExtractJson(string reply)
    {
        var m = FencedJson().Match(reply);
        if (m.Success) return (m.Groups[1].Value, reply.Remove(m.Index, m.Length));
        int s = reply.IndexOf('{'), e = reply.LastIndexOf('}');
        if (s < 0 || e <= s) throw new FormatException("No JSON object found in the model reply.");
        return (reply[s..(e + 1)], reply[..s] + reply[(e + 1)..]);
    }
}

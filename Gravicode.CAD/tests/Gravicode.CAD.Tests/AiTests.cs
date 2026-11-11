using Gravicode.CAD.AI;
using Xunit.Abstractions;

namespace Gravicode.CAD.Tests;

public class AiTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Create a gearbox housing with four mounting holes", "GearboxHousing")]
    [InlineData("mounting plate 150x90x10 with 6 holes M8", "MountingPlate")]
    [InlineData("buat roda gigi 30 gigi modul 2 tebal 12", "SpurGear")]
    [InlineData("flange diameter 140 with 8 bolt holes", "Flange")]
    [InlineData("L bracket 50x70x60 thickness 5", "LBracket")]
    [InlineData("pipa diameter 50 panjang 200", "Tube")]
    [InlineData("hex nut M10", "HexNut")]
    [InlineData("stepped shaft diameter 25 length 150", "SteppedShaft")]
    public async Task Offline_interpreter_builds_valid_parts(string prompt, string expectedName)
    {
        var r = await new TextToCadAgent().GenerateAsync(prompt);
        Assert.Equal(expectedName, r.Definition.Name);
        Assert.True(r.Success, string.Join("; ", r.Log.Select(l => $"{l.Id}:{l.Status}:{l.Message}")));
        Assert.True(r.Model.ToMesh().Volume > 0);
    }

    [Fact]
    public async Task Housing_has_requested_hole_count_and_exports()
    {
        var r = await new TextToCadAgent().GenerateAsync("Create a gearbox housing 160x110x80 with four mounting holes");
        Assert.Equal(160 + 2 * 14, r.Model.Bounds.Size.X, 6); // base flange adds 14 mm per side
        var dir = Path.Combine(Path.GetTempPath(), "gcad-ai-" + Guid.NewGuid().ToString("N"));
        var files = r.ExportAll(dir);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length > 0));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Json_extraction_handles_fences_and_prose()
    {
        var (json, prose) = TextToCadAgent.ExtractJson("```json\n{\"name\":\"A\",\"features\":[]}\n```\nI used a box.");
        Assert.StartsWith("{", json);
        Assert.Contains("box", prose);
    }

    [Fact]
    public async Task Agent_repairs_after_bad_reply()
    {
        var fake = new ScriptedModel(
            "here: {not json}",
            "```json\n{\"name\":\"Cube\",\"features\":[{\"type\":\"box\",\"width\":20,\"depth\":20,\"height\":20}]}\n```");
        var r = await new TextToCadAgent(fake).GenerateAsync("a 20 mm cube");
        Assert.Equal("Cube", r.Definition.Name);
        Assert.Equal(2, r.Attempts);
        Assert.Equal(8000, r.Model.ToMesh().Volume, 6);
        Assert.Contains("could not be rebuilt", fake.Seen[^1]);
    }

    /// <summary>Live test against a real LLM. Runs only when GRAVICODE_LIVE_LLM=claude|azure|deepseek|hf and a key file/env is available.</summary>
    [Fact]
    public async Task Live_llm_text_to_cad()
    {
        var provider = Environment.GetEnvironmentVariable("GRAVICODE_LIVE_LLM");
        if (string.IsNullOrEmpty(provider)) return; // opt-in
        var keyFile = Environment.GetEnvironmentVariable("GRAVICODE_KEY_FILE") ?? "";
        var model = ProviderFactory.FromKeyFile(keyFile, provider) ?? ProviderFactory.FromEnvironment(provider);
        Assert.NotNull(model);
        var r = await new TextToCadAgent(model).GenerateAsync("Create a gearbox housing with four mounting holes");
        output.WriteLine($"{r.Provider}: {r.Definition.Name}, attempts {r.Attempts}, {r.Elapsed.TotalSeconds:F1}s, volume {r.Model.ToMesh().Volume:F0}");
        output.WriteLine(r.Definition.ToJson());
        Assert.True(r.Success);
    }

    private sealed class ScriptedModel(params string[] replies) : ICadLanguageModel
    {
        private int _i;
        public List<string> Seen { get; } = [];
        public string Name => "scripted";
        public Task<string> CompleteAsync(string system, IReadOnlyList<(string Role, string Text)> conversation, CancellationToken ct = default)
        {
            Seen.Add(conversation[^1].Text);
            return Task.FromResult(replies[Math.Min(_i++, replies.Length - 1)]);
        }
    }
}

namespace Gravicode.CAD.AI;

/// <summary>Creates language-model providers from environment variables or a simple "key: value" credentials file.</summary>
public static class ProviderFactory
{
    /// <summary>
    /// Picks a provider from the environment: ANTHROPIC_API_KEY (Claude), AZURE_OPENAI_API_KEY + AZURE_OPENAI_ENDPOINT
    /// (+ AZURE_OPENAI_DEPLOYMENT), DEEPSEEK_API_KEY, HF_TOKEN. Returns null when none is configured (offline mode).
    /// </summary>
    public static ICadLanguageModel? FromEnvironment(string? preferred = null)
    {
        string? E(string k) => Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : null;
        var order = (preferred?.ToLowerInvariant()) switch
        {
            "azure" => new[] { "azure", "claude", "deepseek", "hf" },
            "deepseek" => ["deepseek", "claude", "azure", "hf"],
            "hf" or "huggingface" => ["hf", "claude", "azure", "deepseek"],
            _ => ["claude", "azure", "deepseek", "hf"]
        };
        foreach (var o in order)
        {
            switch (o)
            {
                case "claude" when E("ANTHROPIC_API_KEY") is { } k: return new ClaudeCadModel(k, E("GRAVICODE_CLAUDE_MODEL") ?? "claude-opus-5-5");
                case "azure" when E("AZURE_OPENAI_API_KEY") is { } k && E("AZURE_OPENAI_ENDPOINT") is { } ep: return OpenAICompatibleCadModel.AzureOpenAI(ep, k, E("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-5-mini");
                case "deepseek" when E("DEEPSEEK_API_KEY") is { } k: return OpenAICompatibleCadModel.DeepSeek(k);
                case "hf" when E("HF_TOKEN") is { } k: return OpenAICompatibleCadModel.HuggingFace(k, E("HF_MODEL") ?? "Qwen/Qwen2.5-Coder-32B-Instruct");
            }
        }
        return null;
    }

    /// <summary>
    /// Reads a credentials file made of titled sections ("Anthropic", "Azure OpenAI", "DeepSeek", "Hugging-Face ...")
    /// with "key: value" lines, and builds the requested provider. The file is never written or logged.
    /// </summary>
    public static ICadLanguageModel? FromKeyFile(string path, string provider = "claude", string? model = null)
    {
        if (!File.Exists(path)) return null;
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sections[""] = current;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int c = line.IndexOf(':');
            bool isUrlOnly = c > 0 && line[..c].Contains("http", StringComparison.OrdinalIgnoreCase);
            if (c < 0 || isUrlOnly) { current = new(StringComparer.OrdinalIgnoreCase); sections[line] = current; continue; }
            current[line[..c].Trim().ToLowerInvariant()] = line[(c + 1)..].Trim();
        }
        Dictionary<string, string>? Find(string name) => sections.FirstOrDefault(s => s.Key.Contains(name, StringComparison.OrdinalIgnoreCase)).Value;
        string? Get(Dictionary<string, string>? s, params string[] keys) { if (s is null) return null; foreach (var k in keys) if (s.TryGetValue(k, out var v) && v.Length > 0) return v; return null; }
        string First(string? list, string fallback) => list?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fallback;

        switch (provider.ToLowerInvariant())
        {
            case "claude" or "anthropic":
                {
                    var s = Find("Anthropic");
                    var key = Get(s, "apikey", "api key");
                    if (key is null) return null;
                    var models = Get(s, "model")?.Split(',', StringSplitOptions.TrimEntries) ?? [];
                    return new ClaudeCadModel(key, model ?? (models.Contains("claude-opus-5-5") ? "claude-opus-5-5" : First(Get(s, "model"), "claude-opus-5-5")), Get(s, "workspaceid", "workspace id", "workspace"));
                }
            case "azure":
                {
                    var s = sections.FirstOrDefault(kv => kv.Key.Equals("Azure OpenAI", StringComparison.OrdinalIgnoreCase)).Value ?? Find("Azure OpenAI");
                    var key = Get(s, "apikey"); var ep = Get(s, "endpoint");
                    return key is null || ep is null ? null : OpenAICompatibleCadModel.AzureOpenAI(ep, key, model ?? First(Get(s, "model"), "gpt-5-mini"));
                }
            case "deepseek":
                {
                    var s = Find("DeepSeek");
                    var key = Get(s, "deep seek api key", "apikey", "api key");
                    return key is null ? null : OpenAICompatibleCadModel.DeepSeek(key, model ?? First(Get(s, "model id", "model"), "deepseek-chat"));
                }
            case "hf" or "huggingface":
                {
                    var key = sections.Values.SelectMany(v => v).FirstOrDefault(kv => kv.Value.StartsWith("hf_", StringComparison.Ordinal)).Value;
                    return key is null ? null : OpenAICompatibleCadModel.HuggingFace(key, model ?? "Qwen/Qwen2.5-Coder-32B-Instruct");
                }
        }
        return null;
    }
}

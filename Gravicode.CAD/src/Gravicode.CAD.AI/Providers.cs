using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace Gravicode.CAD.AI;

/// <summary>A text-completion backend used by the text-to-CAD agent.</summary>
public interface ICadLanguageModel
{
    string Name { get; }
    /// <summary>Sends a system prompt plus a conversation (alternating user/assistant, starting with user) and returns the reply text.</summary>
    Task<string> CompleteAsync(string system, IReadOnlyList<(string Role, string Text)> conversation, CancellationToken ct = default);
}

/// <summary>
/// Claude through the official Anthropic .NET SDK. Defaults to <c>claude-opus-5-5</c> with adaptive thinking.
/// Server-side refusal fallbacks are enabled by default (beta <c>server-side-fallback-2026-07-01</c>,
/// <c>fallbacks: "default"</c>) so a request declined by a safety classifier is re-served by a fallback model.
/// </summary>
public sealed class ClaudeCadModel : ICadLanguageModel
{
    private readonly AnthropicClient _client;
    public string Model { get; }
    public string Effort { get; init; } = "medium";
    public bool EnableFallbacks { get; init; } = true;
    public string Name => $"Claude ({Model})";

    /// <param name="apiKey">API key; when null the SDK resolves credentials itself (ANTHROPIC_API_KEY, profiles, ...).</param>
    /// <param name="model">Claude model id (default claude-opus-5-5).</param>
    /// <param name="workspaceId">Workspace id for API keys that are not scoped to a workspace (sent as <c>anthropic-workspace-id</c>).</param>
    public ClaudeCadModel(string? apiKey = null, string model = "claude-opus-5-5", string? workspaceId = null)
    {
        workspaceId ??= Environment.GetEnvironmentVariable("ANTHROPIC_WORKSPACE_ID");
        HttpClient? http = null;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.Add("anthropic-workspace-id", workspaceId);
        }
        _client = (apiKey, http) switch
        {
            (null, null) => new AnthropicClient(),
            (null, { } h) => new AnthropicClient { HttpClient = h },
            ({ } k, null) => new AnthropicClient { ApiKey = k },
            ({ } k, { } h) => new AnthropicClient { ApiKey = k, HttpClient = h },
        };
        Model = model;
    }

    public async Task<string> CompleteAsync(string system, IReadOnlyList<(string Role, string Text)> conversation, CancellationToken ct = default)
    {
        var messages = new List<BetaMessageParam>();
        foreach (var (role, text) in conversation)
            messages.Add(role == "assistant" ? new BetaAssistantMessageParam { Content = text } : new BetaUserMessageParam { Content = text });

        var p = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            System = system,
            Thinking = new BetaThinkingConfigAdaptive(),
            OutputConfig = new BetaOutputConfig { Effort = Effort switch { "low" => Anthropic.Models.Beta.Messages.Effort.Low, "high" => Anthropic.Models.Beta.Messages.Effort.High, "max" => Anthropic.Models.Beta.Messages.Effort.Max, _ => Anthropic.Models.Beta.Messages.Effort.Medium } },
            Messages = messages,
        };
        if (EnableFallbacks)
            p = p with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() };

        BetaMessage resp = await _client.Beta.Messages.Create(p, ct);
        if (resp.StopReason == "refusal")
            throw new InvalidOperationException($"The model declined the request ({resp.StopDetails?.Category}): {resp.StopDetails?.Explanation}");
        var sb = new System.Text.StringBuilder();
        foreach (var block in resp.Content)
            if (block.TryPickText(out BetaTextBlock? t)) sb.Append(t.Text);
        return sb.ToString();
    }
}

/// <summary>
/// Any OpenAI-compatible chat-completions endpoint: Azure OpenAI (v1 API), DeepSeek, the Hugging Face router
/// (https://router.huggingface.co/v1), Ollama, LM Studio, vLLM...
/// </summary>
public sealed class OpenAICompatibleCadModel(HttpClient http, string baseUrl, string apiKey, string model, bool azureApiKeyHeader = false) : ICadLanguageModel
{
    public string Name => $"{model} @ {new Uri(baseUrl).Host}";

    public static OpenAICompatibleCadModel AzureOpenAI(string endpoint, string apiKey, string deployment) =>
        new(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, endpoint.TrimEnd('/') + "/openai/v1", apiKey, deployment, azureApiKeyHeader: true);
    public static OpenAICompatibleCadModel DeepSeek(string apiKey, string model = "deepseek-chat") =>
        new(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, "https://api.deepseek.com/v1", apiKey, model);
    public static OpenAICompatibleCadModel HuggingFace(string token, string model = "Qwen/Qwen2.5-Coder-32B-Instruct") =>
        new(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, "https://router.huggingface.co/v1", token, model);

    public async Task<string> CompleteAsync(string system, IReadOnlyList<(string Role, string Text)> conversation, CancellationToken ct = default)
    {
        var msgs = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var (role, text) in conversation) msgs.Add(new JsonObject { ["role"] = role, ["content"] = text });
        var body = new JsonObject { ["model"] = model, ["messages"] = msgs };
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions") { Content = JsonContent.Create(body) };
        if (azureApiKeyHeader) req.Headers.Add("api-key", apiKey); else req.Headers.Authorization = new("Bearer", apiKey);
        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{Name} returned {(int)res.StatusCode}: {json[..Math.Min(json.Length, 400)]}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}

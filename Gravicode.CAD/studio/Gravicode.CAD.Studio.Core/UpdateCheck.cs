using System.Net.Http.Headers;
using System.Text.Json;

namespace Gravicode.CAD.Studio;

/// <summary>A newer Studio release on GitHub.</summary>
public sealed record StudioUpdate(Version Version, string Tag, string PageUrl);

/// <summary>
/// Looks for a newer Gravicode CAD Studio release on GitHub. Anonymous request to the public releases API with a
/// generic user agent — nothing about the user or the machine is sent. Disable with GCAD_NO_UPDATE_CHECK=1.
/// </summary>
public static class UpdateCheck
{
    public const string ReleasesApi = "https://api.github.com/repos/DotNetVibeCoderz/Vibe_CAD/releases?per_page=30";
    public const string TagPrefix = "gravicode-cad-studio-v";

    public static bool Disabled => Environment.GetEnvironmentVariable("GCAD_NO_UPDATE_CHECK") is "1" or "true";

    public static Version Current =>
        typeof(UpdateCheck).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(0, 0, 0);

    /// <summary>The newest published (non-draft, non-prerelease) Studio release newer than <paramref name="current"/>, or null.</summary>
    public static async Task<StudioUpdate?> FindNewerAsync(Version current, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GravicodeCadStudio", current.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var stream = await http.GetStreamAsync(ReleasesApi, ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Newest(json.RootElement, current);
    }

    /// <summary>Picks the newest Studio release from a GitHub releases array (separate for tests).</summary>
    public static StudioUpdate? Newest(JsonElement releases, Version current)
    {
        StudioUpdate? best = null;
        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
            if (r.TryGetProperty("prerelease", out var p) && p.GetBoolean()) continue;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Version.TryParse(tag[TagPrefix.Length..], out var v)) continue;
            if (v > current && (best is null || v > best.Version))
                best = new StudioUpdate(v, tag, r.GetProperty("html_url").GetString() ?? "https://github.com/DotNetVibeCoderz/Vibe_CAD/releases");
        }
        return best;
    }
}

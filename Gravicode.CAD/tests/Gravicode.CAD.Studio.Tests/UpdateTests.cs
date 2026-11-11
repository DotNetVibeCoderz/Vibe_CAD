using System.Text.Json;

namespace Gravicode.CAD.Studio.Tests;

public class UpdateTests
{
    private const string Releases = """
        [
          { "tag_name": "gravicode-cad-v1.1.0", "draft": false, "prerelease": false, "html_url": "lib" },
          { "tag_name": "gravicode-cad-studio-v1.2.0", "draft": true, "prerelease": false, "html_url": "draft" },
          { "tag_name": "gravicode-cad-studio-v1.1.5", "draft": false, "prerelease": true, "html_url": "pre" },
          { "tag_name": "gravicode-cad-studio-v1.1.0", "draft": false, "prerelease": false, "html_url": "https://example/1.1.0" },
          { "tag_name": "gravicode-cad-studio-v1.0.0", "draft": false, "prerelease": false, "html_url": "https://example/1.0.0" }
        ]
        """;

    [Fact]
    public void Picks_the_newest_published_studio_release()
    {
        var json = JsonDocument.Parse(Releases).RootElement;
        var u = UpdateCheck.Newest(json, new Version(1, 0, 0));
        Assert.NotNull(u);
        Assert.Equal(new Version(1, 1, 0), u.Version);                 // not the draft, the pre-release or the library tag
        Assert.Equal("https://example/1.1.0", u.PageUrl);
        Assert.Null(UpdateCheck.Newest(json, new Version(1, 1, 0)));   // up to date
    }
}

using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Services;

public class UpdateServiceTests
{
    [Fact]
    public void ParseVersion_WithVPrefix_ReturnsParsedVersion()
    {
        var result = UpdateService.ParseVersion("v1.2.3");

        Assert.Equal(new Version(1, 2, 3), result);
    }

    [Fact]
    public void ParseVersion_WithoutVPrefix_ReturnsParsedVersion()
    {
        var result = UpdateService.ParseVersion("1.0.0");

        Assert.Equal(new Version(1, 0, 0), result);
    }

    [Fact]
    public void IsNewerVersion_RemoteHigherThanLocal_ReturnsTrue()
    {
        var result = UpdateService.IsNewerVersion("2.0.0", new Version(1, 0, 0));

        Assert.True(result);
    }

    [Fact]
    public void IsNewerVersion_RemoteSameAsLocal_ReturnsFalse()
    {
        var result = UpdateService.IsNewerVersion("1.0.0", new Version(1, 0, 0));

        Assert.False(result);
    }

    [Fact]
    public void IsNewerVersion_RemoteLowerThanLocal_ReturnsFalse()
    {
        var result = UpdateService.IsNewerVersion("0.9.0", new Version(1, 0, 0));

        Assert.False(result);
    }

    [Fact]
    public void GitHubRelease_Deserializes_JsonWithTagNameAndHtmlUrl()
    {
        var json = """
            {
                "tag_name": "v2.1.0",
                "html_url": "https://github.com/daniel-mielke/ccInfoWin/releases/tag/v2.1.0",
                "prerelease": false
            }
            """;

        var release = JsonSerializer.Deserialize<GitHubRelease>(json);

        Assert.NotNull(release);
        Assert.Equal("v2.1.0", release.TagName);
        Assert.Equal("https://github.com/daniel-mielke/ccInfoWin/releases/tag/v2.1.0", release.HtmlUrl);
        Assert.False(release.Prerelease);
    }
}

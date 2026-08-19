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

    [Theory]
    [InlineData("https://github.com/daniel-mielke/ccInfoWin/releases/tag/v2.1.0", true)]
    [InlineData("https://GITHUB.COM/daniel-mielke/ccInfoWin", true)]
    [InlineData("https://github.com.evil.example/daniel-mielke/ccInfoWin", false)]
    [InlineData("https://githubXcom/daniel-mielke/ccInfoWin", false)]
    [InlineData("http://github.com/daniel-mielke/ccInfoWin", false)]
    [InlineData("https://user@evil.example/https://github.com/", false)]
    // Userinfo, not authority: the shared rule compares Uri.Host, so the "github.com@" prefix cannot
    // smuggle the launch past the allow-list.
    [InlineData("https://github.com@evil.example/daniel-mielke/ccInfoWin", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void IsReleasePageUrl_AcceptsOnlyHttpsUrlsOnTheGitHubHost(string url, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsReleasePageUrl(url));
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

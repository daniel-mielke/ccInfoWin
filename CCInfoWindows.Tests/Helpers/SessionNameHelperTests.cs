using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class SessionNameHelperTests
{
    [Theory]
    [InlineData(@"D:\myProjects\ccInfoWin", "ccInfoWin")]
    [InlineData("/home/user/project", "project")]
    [InlineData("/var/www/html/my-app", "my-app")]
    [InlineData(@"C:\Users\Test\Documents\my-project", "my-project")]
    public void GetDisplayName_ValidCwd_ReturnsLastSegment(string cwd, string expected)
    {
        var result = SessionNameHelper.GetDisplayName(cwd);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetDisplayName_NullCwdNoFallback_ReturnsNull(string? cwd)
    {
        var result = SessionNameHelper.GetDisplayName(cwd);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null, "D--myProjects-ccInfoWin", "ccInfoWin")]
    [InlineData("", "C--Users-DanielMielke--claude-mem-observer-sessions", "sessions")]
    [InlineData(null, "D--SAP-Testo-testo-frontend-nextjs", "nextjs")]
    public void GetDisplayName_NullCwdWithFallback_DecodesDirName(string? cwd, string fallback, string expected)
    {
        var result = SessionNameHelper.GetDisplayName(cwd, fallback);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDisplayName_CwdTakesPrecedenceOverFallback()
    {
        var result = SessionNameHelper.GetDisplayName("/home/user/myProject", "D--other-fallback");

        Assert.Equal("myProject", result);
    }

    [Theory]
    [InlineData("D--myProjects-ccInfoWin", "ccInfoWin")]
    [InlineData("home-user-project", "project")]
    [InlineData("singlename", "singlename")]
    [InlineData("C--Users-DanielMielke--claude-mem-observer-sessions", "sessions")]
    public void DecodeProjectDirectory_EncodedName_ReturnsLastSegment(string encodedName, string expected)
    {
        var result = SessionNameHelper.DecodeProjectDirectory(encodedName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DecodeProjectDirectory_NullOrEmpty_ReturnsNull(string? encodedName)
    {
        var result = SessionNameHelper.DecodeProjectDirectory(encodedName);

        Assert.Null(result);
    }
}

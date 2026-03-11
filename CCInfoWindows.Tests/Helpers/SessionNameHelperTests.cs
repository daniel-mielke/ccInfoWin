using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class SessionNameHelperTests
{
    [Theory]
    [InlineData(@"D:\myProjects\ccInfoWin", "ccInfoWin")]
    [InlineData("/home/user/project", "project")]
    [InlineData("/var/www/html/my-app", "my-app")]
    [InlineData(@"C:\Users\Test\Documents\my-project", "my-project")]
    public void GetDisplayName_ValidPath_ReturnsLastSegment(string cwd, string expected)
    {
        var result = SessionNameHelper.GetDisplayName(cwd);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetDisplayName_NullOrEmpty_ReturnsUnbekanntesProjekt(string? cwd)
    {
        var result = SessionNameHelper.GetDisplayName(cwd);

        Assert.Equal("Unbekanntes Projekt", result);
    }

    [Theory]
    [InlineData("D--myProjects-ccInfoWin", "ccInfoWin")]
    [InlineData("home-user-project", "project")]
    [InlineData("singlename", "singlename")]
    public void DecodeProjectDirectory_EncodedName_ReturnsLastSegment(string encodedName, string expected)
    {
        var result = SessionNameHelper.DecodeProjectDirectory(encodedName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DecodeProjectDirectory_NullOrEmpty_ReturnsUnbekanntesProjekt(string? encodedName)
    {
        var result = SessionNameHelper.DecodeProjectDirectory(encodedName);

        Assert.Equal("Unbekanntes Projekt", result);
    }
}

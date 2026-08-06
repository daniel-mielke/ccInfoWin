using CCInfoWindows.ViewModels;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Review finding 39: the post-login gate ran a bare StartsWith on "https://claude.ai", so a
/// lookalike authority such as https://claude.ai.evil.example/ passed and could have become the
/// bridge's script host. These cases pin the parsed-host contract, including the pre-auth paths
/// that must stay excluded.
/// </summary>
public class LoginViewModelUrlTests
{
    [Theory]
    [InlineData("https://claude.ai/chats", true)]
    [InlineData("https://claude.ai/new", true)]
    [InlineData("https://claude.ai/chat/abc-123?tab=1", true)]
    [InlineData("https://claude.ai", true)]
    [InlineData("https://CLAUDE.AI/chats", true)]
    [InlineData("https://claude.ai/login", false)]
    [InlineData("https://claude.ai/login?returnTo=%2Fnew", false)]
    [InlineData("https://claude.ai/signup", false)]
    [InlineData("https://claude.ai/oauth/authorize", false)]
    [InlineData("https://claude.ai/auth/callback", false)]
    [InlineData("https://claude.ai.evil.example/chats", false)]
    [InlineData("https://claude.aievil.example/chats", false)]
    [InlineData("https://www.claude.ai/chats", false)]
    [InlineData("http://claude.ai/chats", false)]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    public void IsPostLoginUrl_OnlyAcceptsHttpsClaudeAiOutsideTheLoginFlow(string url, bool expected)
    {
        Assert.Equal(expected, LoginViewModel.IsPostLoginUrl(url));
    }

    [Theory]
    [InlineData("https://claude.ai/login", true)]
    [InlineData("https://claude.ai/login?returnTo=%2Fnew", true)]
    [InlineData("https://claude.ai/chats", false)]
    [InlineData("https://claude.ai.evil.example/login", false)]
    [InlineData("http://claude.ai/login", false)]
    public void IsLoginPageUrl_GatesTheLoadingOverlayOnTheRealLoginPage(string url, bool expected)
    {
        Assert.Equal(expected, LoginViewModel.IsLoginPageUrl(url));
    }
}

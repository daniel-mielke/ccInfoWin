using CCInfoWindows.Helpers;
using Xunit;

namespace CCInfoWindows.Tests.Helpers;

public class SessionNameSanitizerTests
{
    [Fact]
    public void Strip_NullReturnsEmpty()
    {
        Assert.Equal(string.Empty, SessionNameSanitizer.Strip(null));
    }

    [Fact]
    public void Strip_EmptyReturnsEmpty()
    {
        Assert.Equal(string.Empty, SessionNameSanitizer.Strip(""));
    }

    [Theory]
    [InlineData("Hello World", "Hello World")]
    [InlineData("space U+0020 ok", "space U+0020 ok")]
    public void Strip_PreservesNormalText(string input, string expected)
    {
        Assert.Equal(expected, SessionNameSanitizer.Strip(input));
    }

    [Fact]
    public void Strip_RemovesC0ControlCharacters()
    {
        // U+0001 (SOH), U+0002 (STX), U+001F (US) — all in C0 range U+0000..U+001F
        var input = "BadInput";
        Assert.Equal("BadInput", SessionNameSanitizer.Strip(input));
    }

    [Fact]
    public void Strip_RemovesTabCharacter()
    {
        // U+0009 (HT) lies in U+0000..U+001F
        Assert.Equal("Tabhere", SessionNameSanitizer.Strip("Tab\there"));
    }

    [Fact]
    public void Strip_RemovesNewlineCharacter()
    {
        // U+000A (LF) lies in U+0000..U+001F
        Assert.Equal("Newlinehere", SessionNameSanitizer.Strip("Newline\nhere"));
    }

    [Fact]
    public void Strip_RemovesDelButPreservesSpace()
    {
        // U+007F (DEL) is the sole upper carve-out; U+0020 (space) must survive
        var input = "DELafter";
        Assert.Equal("DELafter", SessionNameSanitizer.Strip(input));
    }

    [Fact]
    public void Strip_PreservesEmojiAndCjk()
    {
        // A2-P2: emoji, CJK, and slash must survive
        var input = "Émoji \U0001F680 / Backup";
        Assert.Equal(input, SessionNameSanitizer.Strip(input));
    }

    [Fact]
    public void Strip_PreservesCjkCharacters()
    {
        Assert.Equal("中文テスト", SessionNameSanitizer.Strip("中文テスト"));
    }

    [Fact]
    public void Strip_DoesNotStripBidiCodepoints()
    {
        // O-02: Bidi-control codepoints (U+202A..U+202E) are intentionally NOT stripped —
        // same scope as macOS reference. This test documents the known gap.
        var input = "BiDi‪‮ok";
        Assert.Equal(input, SessionNameSanitizer.Strip(input));
    }
}

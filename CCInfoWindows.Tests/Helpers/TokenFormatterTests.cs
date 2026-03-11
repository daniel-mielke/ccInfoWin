using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class TokenFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(567, "567")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1.0K")]
    [InlineData(1_234, "1.2K")]
    [InlineData(9_999, "10.0K")]
    [InlineData(999_999, "1000.0K")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(1_234_567, "1.2M")]
    [InlineData(10_000_000, "10.0M")]
    public void FormatTokenCount_ReturnsExpectedString(long tokens, string expected)
    {
        var result = TokenFormatter.FormatTokenCount(tokens);

        Assert.Equal(expected, result);
    }
}

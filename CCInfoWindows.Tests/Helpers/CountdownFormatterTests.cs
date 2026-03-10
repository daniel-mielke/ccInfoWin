using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class CountdownFormatterTests
{
    [Fact]
    public void FormatCountdown_Null_ReturnsDash()
    {
        Assert.Equal("--", CountdownFormatter.FormatCountdown(null));
    }

    [Fact]
    public void FormatCountdown_PastTime_ReturnsDash()
    {
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        Assert.Equal("--", CountdownFormatter.FormatCountdown(pastTime));
    }

    [Fact]
    public void FormatCountdown_TwoHoursFourteenMinutes_ReturnsFormatted()
    {
        var future = DateTimeOffset.UtcNow.AddHours(2).AddMinutes(14).AddSeconds(30);
        var result = CountdownFormatter.FormatCountdown(future);
        Assert.Equal("2h 14min", result);
    }

    [Fact]
    public void FormatCountdown_FortyFiveMinutes_ReturnsMinutesOnly()
    {
        var future = DateTimeOffset.UtcNow.AddMinutes(45).AddSeconds(30);
        var result = CountdownFormatter.FormatCountdown(future);
        Assert.Equal("45min", result);
    }

    [Fact]
    public void FormatCountdown_LessThanOneMinute_ReturnsDash()
    {
        var future = DateTimeOffset.UtcNow.AddSeconds(30);
        Assert.Equal("--", CountdownFormatter.FormatCountdown(future));
    }

    [Fact]
    public void FormatResetDate_Null_ReturnsDash()
    {
        Assert.Equal("--", CountdownFormatter.FormatResetDate(null));
    }

    [Fact]
    public void FormatResetDate_ValidDate_ReturnsGermanLocaleString()
    {
        // Friday, Feb 27, 2026 at 10:00 UTC
        var date = new DateTimeOffset(2026, 2, 27, 10, 0, 0, TimeSpan.Zero);
        var result = CountdownFormatter.FormatResetDate(date);

        // Should contain German abbreviated day name and "dd.MM.," pattern
        Assert.Contains("27.02.", result);
        Assert.Matches(@"\w+\.?\s\d{2}\.\d{2}\.,\s\d{2}:\d{2}", result);
    }
}

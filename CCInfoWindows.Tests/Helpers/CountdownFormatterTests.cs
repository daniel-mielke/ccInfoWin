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
    public void FormatCountdown_ThreeDays22Hours_ReturnsDaysHoursFormat()
    {
        var future = DateTimeOffset.UtcNow.AddDays(3).AddHours(22).AddMinutes(15).AddSeconds(30);
        var result = CountdownFormatter.FormatCountdown(future);
        Assert.Equal("3d 22h", result);
    }

    [Fact]
    public void FormatCountdown_ExactlyOneDay_ReturnsDaysHoursFormat()
    {
        var future = DateTimeOffset.UtcNow.AddHours(24).AddSeconds(30);
        var result = CountdownFormatter.FormatCountdown(future);
        Assert.Equal("1d 0h", result);
    }

    [Fact]
    public void FormatCountdown_OneDayZeroMinutes_ReturnsDaysHoursFormat()
    {
        var future = DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30).AddSeconds(30);
        var result = CountdownFormatter.FormatCountdown(future);
        Assert.Equal("1d 0h", result);
    }

    [Fact]
    public void FormatCountdown_SevenDays_ReturnsDaysHoursFormat()
    {
        var future = DateTimeOffset.UtcNow.AddDays(7).AddSeconds(30);
        var result = CountdownFormatter.FormatCountdown(future);
        Assert.Equal("7d 0h", result);
    }

    [Fact]
    public void FormatCountdown_JustUnder24Hours_ReturnsHoursMinutes()
    {
        var future = DateTimeOffset.UtcNow.AddHours(23).AddMinutes(59).AddSeconds(30);
        var result = CountdownFormatter.FormatCountdown(future);
        Assert.Equal("23h 59min", result);
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

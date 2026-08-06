using System.Globalization;
using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class CountdownFormatterTests
{
    private static readonly CultureInfo German = new("de-DE");
    private static readonly CultureInfo English = new("en-US");

    // The shipped WeeklyResetDatePattern values. ResourceCoverageTests asserts the resw side; here
    // they are inputs, so the formatter can be asserted without a WinUI3Localizer host.
    private const string GermanPattern = "ddd dd.MM., HH:mm";
    private const string EnglishPattern = "ddd, MMM d, HH:mm";

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
    public void FormatResetDate_German_KeepsTheDayMonthOrder()
    {
        var result = CountdownFormatter.FormatResetDate(LocalTenOnFeb27(), GermanPattern, German);

        Assert.StartsWith("Fr", result);
        Assert.Contains("27.02.", result);
        Assert.EndsWith("10:00", result);
    }

    [Fact]
    public void FormatResetDate_English_DoesNotRenderTheGermanDayMonthOrder()
    {
        // Finding 21: the formatter hardcoded de-DE, so an English user read "06.08." as June 8th.
        var result = CountdownFormatter.FormatResetDate(LocalTenOnFeb27(), EnglishPattern, English);

        Assert.StartsWith("Fri", result);
        Assert.Contains("Feb 27", result);
        Assert.DoesNotContain("27.02.", result);
        Assert.EndsWith("10:00", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatResetDate_WithoutAPattern_FallsBackToTheCultureDefault(string? pattern)
    {
        // The resource lookup returns nothing before the localizer is built; rendering the raw uid
        // as if it were a date, or throwing into the caller's UI update, are both worse than this.
        var resetsAt = LocalTenOnFeb27();

        var result = CountdownFormatter.FormatResetDate(resetsAt, pattern, German);

        var expected = resetsAt.ToLocalTime().ToString(
            CountdownFormatter.CultureDefaultPattern(German), German);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatResetDate_WithAMalformedPattern_FallsBackInsteadOfThrowing()
    {
        // An unterminated quoted literal is the realistic translator error, and FormatException out
        // of here would abort the whole usage-data update, not just this one label.
        var resetsAt = LocalTenOnFeb27();

        var result = CountdownFormatter.FormatResetDate(resetsAt, "ddd 'unterminated", German);

        var expected = resetsAt.ToLocalTime().ToString(
            CountdownFormatter.CultureDefaultPattern(German), German);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CultureDefaultPattern_RendersARealDateForBothShippedCultures()
    {
        var resetsAt = LocalTenOnFeb27();

        foreach (var culture in new[] { German, English })
        {
            var pattern = CountdownFormatter.CultureDefaultPattern(culture);
            var rendered = resetsAt.ToLocalTime().ToString(pattern, culture);

            Assert.NotEqual(pattern, rendered);
            Assert.Contains("27", rendered);
        }
    }

    /// <summary>
    /// Friday, 27 Feb 2026, 10:00 in the machine's own time zone, so ToLocalTime() is a no-op and the
    /// assertions hold in every time zone the suite runs in.
    /// </summary>
    private static DateTimeOffset LocalTenOnFeb27()
    {
        var wallClock = new DateTime(2026, 2, 27, 10, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, TimeZoneInfo.Local.GetUtcOffset(wallClock));
    }
}

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

    /// <summary>
    /// The countdown truncates, so every offset carries the same +30s guard: it keeps the target off
    /// a band edge that the clock could cross between the Add chain and the format call. Written once
    /// here rather than re-typed per case, which is how a copy without it turns flaky.
    /// </summary>
    [Theory]
    [InlineData(0, 0, -10, "--")]           // already past
    [InlineData(0, 0, 0, "--")]             // under a minute left, only the +30s guard
    [InlineData(0, 0, 45, "45min")]
    [InlineData(0, 2, 14, "2h 14min")]
    [InlineData(0, 23, 59, "23h 59min")]    // last minute below the days band
    [InlineData(0, 24, 0, "1d 0h")]         // first minute inside the days band
    [InlineData(1, 0, 30, "1d 0h")]         // same output, reached from a different offset
    [InlineData(3, 22, 15, "3d 22h")]
    [InlineData(7, 0, 0, "7d 0h")]
    public void FormatCountdown_RendersTheRemainingTimeInItsBand(
        int days, int hours, int minutes, string expected)
    {
        var target = DateTimeOffset.UtcNow
            .AddDays(days).AddHours(hours).AddMinutes(minutes).AddSeconds(30);

        Assert.Equal(expected, CountdownFormatter.FormatCountdown(target));
    }

    [Fact]
    public void FormatResetDate_Null_ReturnsDash()
    {
        Assert.Equal("--", CountdownFormatter.FormatResetDate(null));
    }

    [Fact]
    public void FormatWithPattern_German_KeepsTheDayMonthOrder()
    {
        var result = FormatResetDateWith(GermanPattern, German);

        Assert.StartsWith("Fr", result);
        Assert.Contains("27.02.", result);
        Assert.EndsWith("10:00", result);
    }

    [Fact]
    public void FormatWithPattern_English_DoesNotRenderTheGermanDayMonthOrder()
    {
        // Finding 21: the formatter hardcoded de-DE, so an English user read "06.08." as June 8th.
        var result = FormatResetDateWith(EnglishPattern, English);

        Assert.StartsWith("Fri", result);
        Assert.Contains("Feb 27", result);
        Assert.DoesNotContain("27.02.", result);
        Assert.EndsWith("10:00", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatWithPattern_WithoutAPattern_FallsBackToTheCultureDefault(string? pattern)
    {
        // The resource lookup returns nothing before the localizer is built; rendering the raw uid
        // as if it were a date, or throwing into the caller's UI update, are both worse than this.
        Assert.Equal(CultureDefaultRendering(German), FormatResetDateWith(pattern, German));
    }

    [Fact]
    public void FormatWithPattern_WithAMalformedPattern_FallsBackInsteadOfThrowing()
    {
        // An unterminated quoted literal is the realistic translator error, and FormatException out
        // of here would abort the whole usage-data update, not just this one label.
        Assert.Equal(CultureDefaultRendering(German), FormatResetDateWith("ddd 'unterminated", German));
    }

    [Fact]
    public void FormatWithPattern_UsesThePatternUidOnlyForTheLogEntry_NotTheOutput()
    {
        // The uid names the offending resw entry in app.log. It must never reach ToString, or an
        // unresolved pattern would render as a plausible-looking wrong date.
        var rendered = CountdownFormatter.FormatWithPattern(
            LocalTenOnFeb27(), pattern: null, patternUid: "ddd", culture: German);

        Assert.Equal(CultureDefaultRendering(German), rendered);
    }

    [Fact]
    public void FormatWithLocalePattern_WithoutALocalizerHost_FallsBackToTheCultureDefault()
    {
        // The production entry point: xUnit can never build a WinUI3Localizer host, so the pattern
        // lookup always fails here — and the label still has to be a date.
        var rendered = CountdownFormatter.FormatWithLocalePattern(
            LocalTenOnFeb27(), CountdownFormatter.ResetDatePatternUid, German);

        Assert.Equal(CultureDefaultRendering(German), rendered);
    }

    [Fact]
    public void FormatResetDate_WithoutALocalizerHost_StillRendersADate()
    {
        // Same degradation reached through the public nullable entry point, so the "--" branch cannot
        // start swallowing a present value.
        var rendered = CountdownFormatter.FormatResetDate(LocalTenOnFeb27());

        Assert.Equal(CultureDefaultRendering(CultureInfo.CurrentUICulture), rendered);
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

    private static string FormatResetDateWith(string? pattern, CultureInfo culture) =>
        CountdownFormatter.FormatWithPattern(
            LocalTenOnFeb27(), pattern, CountdownFormatter.ResetDatePatternUid, culture);

    private static string CultureDefaultRendering(CultureInfo culture) =>
        LocalTenOnFeb27().ToLocalTime().ToString(
            CountdownFormatter.CultureDefaultPattern(culture), culture);

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

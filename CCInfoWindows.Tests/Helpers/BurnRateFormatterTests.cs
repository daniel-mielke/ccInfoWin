using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class BurnRateFormatterTests
{
    [Theory]
    [InlineData(1, 0, 1, (int)TimeFormat.MinutesOnly)]
    [InlineData(33, 0, 33, (int)TimeFormat.MinutesOnly)]
    [InlineData(59, 0, 59, (int)TimeFormat.MinutesOnly)]   // last minute below the hours branch
    [InlineData(60, 1, 0, (int)TimeFormat.HoursOnly)]      // first minute inside it
    [InlineData(61, 1, 1, (int)TimeFormat.HoursMinutes)]
    [InlineData(93, 1, 33, (int)TimeFormat.HoursMinutes)]
    [InlineData(120, 2, 0, (int)TimeFormat.HoursOnly)]
    public void ParseTime_SplitsMinutesIntoTheDisplayedFormat(
        int totalMinutes, int expectedHours, int expectedMinutes, int expectedFormat)
    {
        var (hours, minutes, format) = BurnRateFormatter.ParseTime(totalMinutes);

        Assert.Equal(expectedHours, hours);
        Assert.Equal(expectedMinutes, minutes);
        // TimeFormat is internal, so the row carries its int value: a public [Theory] parameter
        // cannot be less accessible than the method (CS0051).
        Assert.Equal((TimeFormat)expectedFormat, format);
    }
}

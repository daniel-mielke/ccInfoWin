using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class BurnRateFormatterTests
{
    [Fact]
    public void ParseTime_MinutesOnly_ReturnsMinutesOnly()
    {
        var (hours, minutes, format) = BurnRateFormatter.ParseTime(33);

        Assert.Equal(0, hours);
        Assert.Equal(33, minutes);
        Assert.Equal(TimeFormat.MinutesOnly, format);
    }

    [Fact]
    public void ParseTime_HoursAndMinutes_ReturnsHoursMinutes()
    {
        var (hours, minutes, format) = BurnRateFormatter.ParseTime(93);

        Assert.Equal(1, hours);
        Assert.Equal(33, minutes);
        Assert.Equal(TimeFormat.HoursMinutes, format);
    }

    [Fact]
    public void ParseTime_ExactHours_ReturnsHoursOnly()
    {
        var (hours, minutes, format) = BurnRateFormatter.ParseTime(120);

        Assert.Equal(2, hours);
        Assert.Equal(0, minutes);
        Assert.Equal(TimeFormat.HoursOnly, format);
    }

    [Fact]
    public void ParseTime_OneMinute_ReturnsMinutesOnly()
    {
        var (hours, minutes, format) = BurnRateFormatter.ParseTime(1);

        Assert.Equal(0, hours);
        Assert.Equal(1, minutes);
        Assert.Equal(TimeFormat.MinutesOnly, format);
    }

    [Fact]
    public void ParseTime_SixtyMinutes_ReturnsHoursOnly()
    {
        var (hours, minutes, format) = BurnRateFormatter.ParseTime(60);

        Assert.Equal(1, hours);
        Assert.Equal(0, minutes);
        Assert.Equal(TimeFormat.HoursOnly, format);
    }
}

using System.Text.Json;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Models;

public class UsageDataTests
{
    [Fact]
    public void Deserialize_FullJson_AllWindowsPopulated()
    {
        var json = """
        {
            "five_hour": { "utilization": 0.35, "resets_at": "2026-03-10T15:00:00Z" },
            "seven_day": { "utilization": 0.72, "resets_at": "2026-03-14T00:00:00Z" },
            "seven_day_opus": { "utilization": 0.10, "resets_at": "2026-03-14T00:00:00Z" },
            "seven_day_sonnet": { "utilization": 0.50, "resets_at": "2026-03-14T00:00:00Z" }
        }
        """;

        var result = JsonSerializer.Deserialize<UsageResponse>(json);

        Assert.NotNull(result);
        Assert.NotNull(result.FiveHour);
        Assert.Equal(0.35, result.FiveHour.Utilization);
        Assert.Equal(DateTimeOffset.Parse("2026-03-10T15:00:00Z"), result.FiveHour.ResetsAt);

        Assert.NotNull(result.SevenDay);
        Assert.Equal(0.72, result.SevenDay.Utilization);

        Assert.NotNull(result.SevenDayOpus);
        Assert.Equal(0.10, result.SevenDayOpus.Utilization);

        Assert.NotNull(result.SevenDaySonnet);
        Assert.Equal(0.50, result.SevenDaySonnet.Utilization);
    }

    [Fact]
    public void Deserialize_PartialJson_NullableWindowsAreNull()
    {
        var json = """
        {
            "five_hour": { "utilization": 0.35, "resets_at": "2026-03-10T15:00:00Z" },
            "seven_day": { "utilization": 0.72, "resets_at": "2026-03-14T00:00:00Z" }
        }
        """;

        var result = JsonSerializer.Deserialize<UsageResponse>(json);

        Assert.NotNull(result);
        Assert.NotNull(result.FiveHour);
        Assert.NotNull(result.SevenDay);
        Assert.Null(result.SevenDayOpus);
        Assert.Null(result.SevenDaySonnet);
    }

    [Fact]
    public void Deserialize_EmptyJson_AllWindowsNull()
    {
        var json = "{}";

        var result = JsonSerializer.Deserialize<UsageResponse>(json);

        Assert.NotNull(result);
        Assert.Null(result.FiveHour);
        Assert.Null(result.SevenDay);
        Assert.Null(result.SevenDayOpus);
        Assert.Null(result.SevenDaySonnet);
    }
}

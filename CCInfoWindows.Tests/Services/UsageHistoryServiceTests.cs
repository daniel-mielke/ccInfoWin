using CCInfoWindows.Models;
using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Services;

public sealed class UsageHistoryServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly UsageHistoryService _sut;

    public UsageHistoryServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _sut = new UsageHistoryService(_tempDirectory);
    }

    [Fact]
    public void LoadHistory_WhenFileDoesNotExist_ReturnsEmptyDefaults()
    {
        var result = _sut.LoadHistory();

        Assert.NotNull(result);
        Assert.Null(result.ResetsAt);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void LoadHistory_WhenFileContainsCorruptJson_ReturnsEmptyDefaults()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "usage-history.json"), "{ not valid json !!!");

        var result = _sut.LoadHistory();

        Assert.NotNull(result);
        Assert.Null(result.ResetsAt);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void SaveThenLoad_RoundTrip_PreservesAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new UsageHistory
        {
            ResetsAt = now.AddHours(3),
            Points =
            [
                new UsageHistoryPoint { Timestamp = now.AddMinutes(-10), Utilization = 0.25 },
                new UsageHistoryPoint { Timestamp = now.AddMinutes(-5),  Utilization = 0.50 },
                new UsageHistoryPoint { Timestamp = now,                 Utilization = 0.75 }
            ]
        };

        _sut.SaveHistory(history);
        var loaded = _sut.LoadHistory();

        Assert.Equal(history.ResetsAt, loaded.ResetsAt);
        Assert.Equal(3, loaded.Points.Count);
        Assert.Equal(0.25, loaded.Points[0].Utilization);
        Assert.Equal(0.50, loaded.Points[1].Utilization);
        Assert.Equal(0.75, loaded.Points[2].Utilization);
        Assert.Equal(history.Points[0].Timestamp, loaded.Points[0].Timestamp);
        Assert.Equal(history.Points[2].Timestamp, loaded.Points[2].Timestamp);
    }

    [Fact]
    public void ClearHistory_DeletesFile_SubsequentLoadReturnsEmpty()
    {
        var history = new UsageHistory
        {
            ResetsAt = DateTimeOffset.UtcNow.AddHours(1),
            Points = [new UsageHistoryPoint { Timestamp = DateTimeOffset.UtcNow, Utilization = 0.5 }]
        };
        _sut.SaveHistory(history);

        _sut.ClearHistory();
        var result = _sut.LoadHistory();

        Assert.Empty(result.Points);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void SaveHistory_CreatesDirectoryIfNotExists()
    {
        Assert.False(Directory.Exists(_tempDirectory));

        _sut.SaveHistory(new UsageHistory());

        Assert.True(Directory.Exists(_tempDirectory));
    }

    [Fact]
    public void SaveAndLoad_With300DataPoints_RoundTripsCorrectly()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var points = Enumerable.Range(0, 300)
            .Select(i => new UsageHistoryPoint
            {
                Timestamp = baseTime.AddSeconds(i * 60),
                Utilization = (i % 100) / 100.0
            })
            .ToList();

        var history = new UsageHistory
        {
            ResetsAt = baseTime.AddHours(5),
            Points = points
        };

        _sut.SaveHistory(history);
        var loaded = _sut.LoadHistory();

        Assert.Equal(300, loaded.Points.Count);
        Assert.Equal(points[0].Utilization, loaded.Points[0].Utilization);
        Assert.Equal(points[299].Timestamp, loaded.Points[299].Timestamp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

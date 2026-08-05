using CCInfoWindows.Models;
using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Services;

public class NotificationStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ccinfo_notif_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyState()
    {
        var state = new NotificationStateStore(_dir).Load();

        Assert.Null(state.FiveHour.WindowId);
        Assert.Null(state.Weekly.WindowId);
    }

    [Fact]
    public void SaveThenLoad_FromAFreshInstance_RoundTripsEveryField()
    {
        // A fresh instance is the point: the flags have to survive an app restart, which is what
        // makes "no refire on startup" work without any extra logic.
        var resetsAt = new DateTimeOffset(2026, 8, 6, 0, 20, 0, TimeSpan.Zero);
        new NotificationStateStore(_dir).Save(new NotificationState
        {
            FiveHour = new WindowNotificationState
            {
                WindowId = "id-5h",
                ResetsAt = resetsAt,
                Notified80 = true,
                Notified95 = false,
                NotifiedReset = true,
                PeakUtilization = 87.5
            },
            Weekly = new WindowNotificationState { WindowId = "id-weekly", PeakUtilization = 12 }
        });

        var loaded = new NotificationStateStore(_dir).Load();

        Assert.Equal("id-5h", loaded.FiveHour.WindowId);
        Assert.Equal(resetsAt, loaded.FiveHour.ResetsAt);
        Assert.True(loaded.FiveHour.Notified80);
        Assert.False(loaded.FiveHour.Notified95);
        Assert.True(loaded.FiveHour.NotifiedReset);
        Assert.Equal(87.5, loaded.FiveHour.PeakUtilization);
        Assert.Equal("id-weekly", loaded.Weekly.WindowId);
        Assert.Equal(12.0, loaded.Weekly.PeakUtilization);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyStateInsteadOfThrowing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "notification-state.json"), "{ not json");

        var state = new NotificationStateStore(_dir).Load();

        Assert.Null(state.FiveHour.WindowId);
    }

    [Fact]
    public void Save_CreatesTheDirectory()
    {
        new NotificationStateStore(_dir).Save(new NotificationState());

        Assert.True(File.Exists(Path.Combine(_dir, "notification-state.json")));
    }

    [Fact]
    public void For_MapsTheKindToTheRightWindow()
    {
        var state = new NotificationState
        {
            FiveHour = new WindowNotificationState { WindowId = "a" },
            Weekly = new WindowNotificationState { WindowId = "b" }
        };

        Assert.Equal("a", state.For(UsageWindowKind.FiveHour).WindowId);
        Assert.Equal("b", state.For(UsageWindowKind.Weekly).WindowId);
    }
}

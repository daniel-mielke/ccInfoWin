using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Tests.TestSupport;
using CCInfoWindows.ViewModels;

namespace CCInfoWindows.Tests.Services;

public sealed class UsageHistoryServiceTests : IDisposable
{
    private const string HistoryFileName = "usage-history.json";
    private const string TempFileName = HistoryFileName + ".tmp";

    /// <summary>
    /// Big enough that File.WriteAllTextAsync cannot plausibly complete before it is awaited, which
    /// is what puts a continuation in flight for the deadlock regression test. A payload that did
    /// complete synchronously would make that test pass vacuously — never fail wrongly.
    /// </summary>
    private const int PointsLargeEnoughToYield = 20_000;

    private readonly TempDirectory _temp = new("ccinfo-history-");
    private readonly UsageHistoryService _sut;

    public UsageHistoryServiceTests()
    {
        _sut = new UsageHistoryService(_temp.Path);
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
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(Path.Combine(_temp.Path, "usage-history.json"), "{ not valid json !!!");

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
        // The fixture root exists from construction, so the missing directory has to be one level
        // below it. Its content is deleted with the root, so nothing extra to tear down.
        var missingDirectory = Path.Combine(_temp.Path, "not-created-yet");
        var sut = new UsageHistoryService(missingDirectory);

        Assert.False(Directory.Exists(missingDirectory));

        sut.SaveHistory(new UsageHistory());

        Assert.True(Directory.Exists(missingDirectory));
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

    // --- New tests for Phase 21 hardening (HIST-02..HIST-05, D-04, D-05, D-07, D-12, D-13) ---

    [Fact]
    public async Task SaveHistoryAsync_RoundTrip_PreservesAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new UsageHistory
        {
            ResetsAt = now.AddHours(3),
            Points =
            {
                new UsageHistoryPoint { Timestamp = now.AddMinutes(-5), Utilization = 0.42 },
                new UsageHistoryPoint { Timestamp = now,                Utilization = 0.55 }
            }
        };

        await _sut.SaveHistoryAsync(history);
        var loaded = _sut.LoadHistory();

        Assert.Equal(history.ResetsAt, loaded.ResetsAt);
        Assert.Equal(2, loaded.Points.Count);
        Assert.Equal(0.42, loaded.Points[0].Utilization);
        Assert.Equal(0.55, loaded.Points[1].Utilization);
    }

    [Fact]
    public async Task SaveSync_VS_SaveAsync_ProducesByteIdenticalJson()
    {
        // Both writers get their own directory under the fixture root, so the fixture teardown
        // reclaims them and this test needs no try/finally of its own.
        var dirSync = Path.Combine(_temp.Path, "sync");
        var dirAsync = Path.Combine(_temp.Path, "async");

        var sutSync = new UsageHistoryService(dirSync);
        var sutAsync = new UsageHistoryService(dirAsync);

        var history = new UsageHistory
        {
            ResetsAt = DateTimeOffset.Parse("2026-05-06T18:00:00Z"),
            Points =
            {
                new UsageHistoryPoint { Timestamp = DateTimeOffset.Parse("2026-05-06T13:00:00Z"), Utilization = 0.42 },
                new UsageHistoryPoint { Timestamp = DateTimeOffset.Parse("2026-05-06T13:05:00Z"), Utilization = 0.43 }
            }
        };

        sutSync.SaveHistory(history);
        await sutAsync.SaveHistoryAsync(history);

        var bytesSync  = File.ReadAllBytes(Path.Combine(dirSync, "usage-history.json"));
        var bytesAsync = File.ReadAllBytes(Path.Combine(dirAsync, "usage-history.json"));

        Assert.Equal(bytesSync, bytesAsync);
    }

    [Fact]
    public void PeekLastSnapshot_BeforeAnySave_ReturnsNull()
    {
        Assert.Null(_sut.PeekLastSnapshot());
    }

    [Fact]
    public void PeekLastSnapshot_AfterSave_ReturnsLastSavedHistory()
    {
        var history = new UsageHistory { ResetsAt = DateTimeOffset.UtcNow.AddHours(2) };
        _sut.SaveHistory(history);

        Assert.Same(history, _sut.PeekLastSnapshot());
    }

    [Fact]
    public void PeekLastSnapshot_AfterClear_ReturnsNull()
    {
        _sut.SaveHistory(new UsageHistory { ResetsAt = DateTimeOffset.UtcNow });
        _sut.ClearHistory();

        Assert.Null(_sut.PeekLastSnapshot());
    }

    // Finding 8: this one proves the semaphore serializes the two writers, but it cannot catch the
    // self-deadlock -- Task.Run has no SynchronizationContext, so there is no dispatcher for the
    // release continuation to be captured onto. See
    // SaveHistoryAsync_WhileTheUiThreadBlocksInSaveHistory_CompletesWithoutTheDispatcher.
    [Fact]
    public async Task ConcurrentSyncAndAsyncWrites_DoNotInterleave()
    {
        var h1 = new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T10:00:00Z") };
        var h2 = new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T20:00:00Z") };

        var asyncTask = _sut.SaveHistoryAsync(h2);
        var syncTask  = Task.Run(() => _sut.SaveHistory(h1));

        await Task.WhenAll(syncTask, asyncTask);

        var content = File.ReadAllText(Path.Combine(_temp.Path, "usage-history.json"));
        // File ends in EITHER h1's JSON OR h2's JSON -- never partial / interleaved
        var matchesH1 = content.Contains("2026-05-06T10:00:00");
        var matchesH2 = content.Contains("2026-05-06T20:00:00");
        Assert.True(matchesH1 ^ matchesH2, $"Expected exactly one history serialized, got: matchesH1={matchesH1} matchesH2={matchesH2}");
    }

    [Fact]
    public void WriteFails_DoesNotUpdateSnapshot()
    {
        // Force Directory.CreateDirectory to fail by pointing the service at a path whose parent
        // already exists as a FILE. It lives under the fixture root, so the fixture teardown removes
        // it and this test needs no try/finally of its own.
        var blockingFile = Path.Combine(_temp.Path, "blocker");
        File.WriteAllText(blockingFile, "blocker");

        var failSut = new UsageHistoryService(Path.Combine(blockingFile, "history"));

        failSut.SaveHistory(new UsageHistory { ResetsAt = DateTimeOffset.UtcNow });

        Assert.Null(failSut.PeekLastSnapshot());
    }

    [Fact]
    public void WindowReset_ClearsPointsAndPersists()
    {
        var t0 = DateTimeOffset.Parse("2026-05-06T10:00:00Z");
        _sut.SaveHistory(new UsageHistory
        {
            ResetsAt = t0,
            Points =
            {
                new UsageHistoryPoint { Timestamp = t0.AddMinutes(-30), Utilization = 0.6 },
                new UsageHistoryPoint { Timestamp = t0.AddMinutes(-15), Utilization = 0.8 }
            }
        });

        // Simulate the reset action that AppendHistoryPoint performs after IsWindowReset == true:
        var fresh = new UsageHistory { ResetsAt = t0.AddMinutes(3) };
        _sut.SaveHistory(fresh);

        var loaded = _sut.LoadHistory();
        Assert.Empty(loaded.Points);
        Assert.Equal(t0.AddMinutes(3), loaded.ResetsAt);
    }

    [Fact]
    public void FirstPoll_AfterAppStart_DoesNotEraseHistory()
    {
        // D-12: When _previousResetsAt == null (first poll after app start), no clear happens.
        // Verify by exercising the IsWindowReset static directly (promoted to internal for test access).
        var apiResetsAt = DateTimeOffset.UtcNow.AddHours(5);

        Assert.False(MainViewModel.IsWindowReset(null, apiResetsAt));
        Assert.False(MainViewModel.IsWindowReset(apiResetsAt, null));
        Assert.False(MainViewModel.IsWindowReset(null, null));
    }

    // --- Finding 8: sync-over-async self-deadlock (SaveHistory runs on the UI thread) ---

    [Fact]
    public void SaveHistoryAsync_WhileTheUiThreadBlocksInSaveHistory_CompletesWithoutTheDispatcher()
    {
        var closingSnapshot = new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T23:00:00Z") };

        // Models MainWindow.OnClosing: the thread that owns the dispatcher blocks in the synchronous
        // writer. Without ConfigureAwait(false) the _writeLock.Release() continuation is queued to a
        // pump nobody can drain and this never returns. The scaffold lives in TestPump, so this
        // service and the session-name store cannot drift apart on the rule they both assert.
        TestPump.AssertAsyncWriteSurvivesABlockingWrite(
            () => _sut.SaveHistoryAsync(BuildHistory(PointsLargeEnoughToYield)),
            () => _sut.SaveHistory(closingSnapshot));

        Assert.Same(closingSnapshot, _sut.PeekLastSnapshot());
    }

    // --- Finding 35: atomic publish (tmp + File.Move) ---

    [Fact]
    public void SaveHistory_PublishesViaATempFileAndLeavesNoLitter()
    {
        _sut.SaveHistory(BuildHistory(pointCount: 3));

        Assert.True(File.Exists(Path.Combine(_temp.Path, HistoryFileName)));
        Assert.False(File.Exists(Path.Combine(_temp.Path, TempFileName)));
    }

    [Fact]
    public async Task SaveHistoryAsync_PublishesViaATempFileAndLeavesNoLitter()
    {
        await _sut.SaveHistoryAsync(BuildHistory(pointCount: 3));

        Assert.True(File.Exists(Path.Combine(_temp.Path, HistoryFileName)));
        Assert.False(File.Exists(Path.Combine(_temp.Path, TempFileName)));
    }

    [Fact]
    public void SaveHistory_WhenThePublishFails_KeepsThePreviousCompleteFile()
    {
        var persisted = new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T10:00:00Z") };
        _sut.SaveHistory(persisted);

        // A locked destination fails the File.Move, which is exactly the window where the old
        // truncate-in-place write would have left a half-written file behind.
        using (File.Open(Path.Combine(_temp.Path, HistoryFileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _sut.SaveHistory(new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T20:00:00Z") });
        }

        Assert.Equal(persisted.ResetsAt, _sut.LoadHistory().ResetsAt);
        Assert.Same(persisted, _sut.PeekLastSnapshot());
        Assert.False(File.Exists(Path.Combine(_temp.Path, TempFileName)));
    }

    private static UsageHistory BuildHistory(int pointCount)
    {
        var baseTime = DateTimeOffset.Parse("2026-05-06T13:00:00Z");
        return new UsageHistory
        {
            ResetsAt = baseTime.AddHours(5),
            Points = Enumerable.Range(0, pointCount)
                .Select(i => new UsageHistoryPoint
                {
                    Timestamp = baseTime.AddSeconds(i),
                    Utilization = (i % 100) / 100.0
                })
                .ToList()
        };
    }

    public void Dispose() => _temp.Dispose();
}

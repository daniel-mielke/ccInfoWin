using CCInfoWindows.Models;
using CCInfoWindows.Services;
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

    private static readonly TimeSpan PendingWriteTimeout = TimeSpan.FromSeconds(10);

    private readonly string _tempDirectory;
    private readonly UsageHistoryService _sut;

    public UsageHistoryServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _sut = new UsageHistoryService(_tempDirectory);
    }

    /// <summary>
    /// Models the WinUI dispatcher: a captured continuation is Posted to a queue that only the
    /// owning thread drains. This one is never drained — that is the point, because the thread that
    /// would drain it is the one blocked inside the synchronous writer.
    /// </summary>
    private sealed class RecordingPumpContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _postCount);

        public override void Send(SendOrPostCallback d, object? state) => d(state);
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
        var dirSync = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dirAsync = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
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
        finally
        {
            if (Directory.Exists(dirSync))  Directory.Delete(dirSync, recursive: true);
            if (Directory.Exists(dirAsync)) Directory.Delete(dirAsync, recursive: true);
        }
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

        var content = File.ReadAllText(Path.Combine(_tempDirectory, "usage-history.json"));
        // File ends in EITHER h1's JSON OR h2's JSON -- never partial / interleaved
        var matchesH1 = content.Contains("2026-05-06T10:00:00");
        var matchesH2 = content.Contains("2026-05-06T20:00:00");
        Assert.True(matchesH1 ^ matchesH2, $"Expected exactly one history serialized, got: matchesH1={matchesH1} matchesH2={matchesH2}");
    }

    [Fact]
    public void WriteFails_DoesNotUpdateSnapshot()
    {
        // Force Directory.CreateDirectory to fail by pointing service at a path
        // where the parent already exists as a FILE (not a directory).
        var blockingFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        File.WriteAllText(blockingFile, "blocker");
        try
        {
            var failingSubdir = Path.Combine(blockingFile, "history");
            var failSut = new UsageHistoryService(failingSubdir);

            failSut.SaveHistory(new UsageHistory { ResetsAt = DateTimeOffset.UtcNow });

            Assert.Null(failSut.PeekLastSnapshot());
        }
        finally
        {
            File.Delete(blockingFile);
        }
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
        var pump = new RecordingPumpContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(pump);
        try
        {
            var pending = _sut.SaveHistoryAsync(BuildHistory(PointsLargeEnoughToYield));
            var closingSnapshot = new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T23:00:00Z") };

            // Models MainWindow.OnClosing: the thread that owns the dispatcher blocks in the
            // synchronous writer. Without ConfigureAwait(false) the _writeLock.Release()
            // continuation is queued to a pump nobody can drain and this never returns.
            _sut.SaveHistory(closingSnapshot);

            // xUnit1031 (no blocking task operations) cannot be honoured here: this test method must stay
            // synchronous. It installs a SynchronizationContext that is deliberately never drained, so awaiting
            // while that context is current would hang the test itself, and awaiting with ConfigureAwait(false)
            // would resume the finally block on a pooled thread -- leaving the undrainable pump installed on the
            // xUnit worker thread for whatever test runs there next. A bounded Wait is the only correct join.
#pragma warning disable xUnit1031
            Assert.True(pending.Wait(PendingWriteTimeout), "the async write never completed");
#pragma warning restore xUnit1031
            Assert.Equal(0, pump.PostCount);
            Assert.Same(closingSnapshot, _sut.PeekLastSnapshot());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // --- Finding 35: atomic publish (tmp + File.Move) ---

    [Fact]
    public void SaveHistory_PublishesViaATempFileAndLeavesNoLitter()
    {
        _sut.SaveHistory(BuildHistory(pointCount: 3));

        Assert.True(File.Exists(Path.Combine(_tempDirectory, HistoryFileName)));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, TempFileName)));
    }

    [Fact]
    public async Task SaveHistoryAsync_PublishesViaATempFileAndLeavesNoLitter()
    {
        await _sut.SaveHistoryAsync(BuildHistory(pointCount: 3));

        Assert.True(File.Exists(Path.Combine(_tempDirectory, HistoryFileName)));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, TempFileName)));
    }

    [Fact]
    public void SaveHistory_WhenThePublishFails_KeepsThePreviousCompleteFile()
    {
        var persisted = new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T10:00:00Z") };
        _sut.SaveHistory(persisted);

        // A locked destination fails the File.Move, which is exactly the window where the old
        // truncate-in-place write would have left a half-written file behind.
        using (File.Open(Path.Combine(_tempDirectory, HistoryFileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _sut.SaveHistory(new UsageHistory { ResetsAt = DateTimeOffset.Parse("2026-05-06T20:00:00Z") });
        }

        Assert.Equal(persisted.ResetsAt, _sut.LoadHistory().ResetsAt);
        Assert.Same(persisted, _sut.PeekLastSnapshot());
        Assert.False(File.Exists(Path.Combine(_tempDirectory, TempFileName)));
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

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (IOException) { /* another handle still open on a temp file; the OS reclaims it */ }
    }
}

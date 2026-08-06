using CCInfoWindows.Models;
using CCInfoWindows.Services;
using System.Text.Json;
using Xunit;

namespace CCInfoWindows.Tests.Services;

public class SessionNameStoreTests : IDisposable
{
    private const string StoreFileName = "session-names.json";
    private const string TempFileName = StoreFileName + ".tmp";

    /// <summary>
    /// Big enough that File.WriteAllTextAsync cannot plausibly finish before it is awaited, so the
    /// deadlock regression test really does have a continuation in flight.
    /// </summary>
    private const int NamesLargeEnoughToYield = 20_000;

    private static readonly TimeSpan PendingWriteTimeout = TimeSpan.FromSeconds(10);

    private readonly string _tempDir;

    public SessionNameStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ccinfo-rename-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* another handle still open on a temp file; the OS reclaims it */ }
    }

    /// <summary>Records dispatcher Posts without ever running them — see the history-service twin.</summary>
    private sealed class RecordingPumpContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _postCount);

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    [Fact]
    public void LoadFromDisk_FileMissing_ReturnsEmptyState()
    {
        var store = new SessionNameStore(_tempDir);
        Assert.Null(store.GetCustomName("any-id"));
    }

    [Fact]
    public void SetCustomName_UpdatesInMemoryMap()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("id1", "Alpha");
        Assert.Equal("Alpha", store.GetCustomName("id1"));
    }

    [Fact]
    public void SetCustomName_StripsControlChars()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("id1", "Bad\x01\x1FX");
        Assert.Equal("BadX", store.GetCustomName("id1"));
    }

    [Fact]
    public void SetCustomName_EmptyClears()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("id1", "Alpha");
        store.SetCustomName("id1", "");
        Assert.Null(store.GetCustomName("id1"));
    }

    [Fact]
    public void SetCustomName_RaisesNameChanged()
    {
        var store = new SessionNameStore(_tempDir);
        SessionNameChangedEventArgs? received = null;
        store.NameChanged += (_, args) => received = args;

        store.SetCustomName("id1", "Alpha");

        Assert.NotNull(received);
        Assert.Equal("id1", received!.SessionId);
    }

    [Fact]
    public void ClearCustomName_RemovesEntryAndRaisesEvent()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("id1", "Alpha");

        SessionNameChangedEventArgs? received = null;
        store.NameChanged += (_, args) => received = args;

        store.ClearCustomName("id1");

        Assert.Null(store.GetCustomName("id1"));
        Assert.NotNull(received);
        Assert.Equal("id1", received!.SessionId);
    }

    [Fact]
    public async Task SaveAsync_PersistsToTempThenMoves()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("id1", "Alpha");

        await store.SaveAsync();

        var tmpPath = Path.Combine(_tempDir, "session-names.json.tmp");
        var finalPath = Path.Combine(_tempDir, "session-names.json");

        Assert.False(File.Exists(tmpPath));
        Assert.True(File.Exists(finalPath));
    }

    [Fact]
    public async Task SaveAsync_RoundTrip_ReadsBackSameData()
    {
        var storeA = new SessionNameStore(_tempDir);
        storeA.SetCustomName("a", "Alpha");
        storeA.SetCustomName("b", "Beta");
        await storeA.SaveAsync();

        var storeB = new SessionNameStore(_tempDir);
        Assert.Equal("Alpha", storeB.GetCustomName("a"));
        Assert.Equal("Beta", storeB.GetCustomName("b"));
    }

    [Fact]
    public async Task Save_And_SaveAsync_ProduceByteIdenticalJson()
    {
        var dirA = Path.Combine(_tempDir, "a");
        var dirB = Path.Combine(_tempDir, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        var storeA = new SessionNameStore(dirA);
        storeA.SetCustomName("key1", "Name1");
        storeA.SetCustomName("key2", "Name2");
        storeA.Save();

        var storeB = new SessionNameStore(dirB);
        storeB.SetCustomName("key1", "Name1");
        storeB.SetCustomName("key2", "Name2");
        await storeB.SaveAsync();

        var bytesA = File.ReadAllBytes(Path.Combine(dirA, "session-names.json"));
        var bytesB = File.ReadAllBytes(Path.Combine(dirB, "session-names.json"));

        Assert.True(bytesA.SequenceEqual(bytesB));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentCallers_NoCorruption()
    {
        var store = new SessionNameStore(_tempDir);

        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            store.SetCustomName($"id{i}", $"Name{i}");
            await store.SaveAsync();
        }));

        await Task.WhenAll(tasks);

        var finalPath = Path.Combine(_tempDir, "session-names.json");
        Assert.True(File.Exists(finalPath));

        var json = File.ReadAllText(finalPath);
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.NotNull(deserialized);
    }

    [Fact]
    public async Task OrphanEntriesArePreserved()
    {
        var storeA = new SessionNameStore(_tempDir);
        storeA.SetCustomName("deleted-session", "Custom");
        await storeA.SaveAsync();

        var storeB = new SessionNameStore(_tempDir);
        Assert.Equal("Custom", storeB.GetCustomName("deleted-session"));
    }

    [Fact]
    public async Task SyncSave_ReleasesLockOnException()
    {
        // Point at a read-only scenario: use a file-as-directory trick to force an IOException.
        var blockerPath = Path.Combine(_tempDir, "blocker");
        File.WriteAllText(blockerPath, "I am a file, not a directory");

        var store = new SessionNameStore(blockerPath);
        store.SetCustomName("id1", "Alpha");

        // Save should fail gracefully (best-effort, returns false)
        var result = store.Save();
        Assert.False(result);

        // Semaphore must be released — a subsequent SaveAsync must not deadlock
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await store.SaveAsync(cts.Token);
    }

    [Fact]
    public async Task LastSavedSnapshot_NullBeforeFirstWrite_MatchesAfterSave()
    {
        var store = new SessionNameStore(_tempDir);

        // Before any write, snapshot is null
        Assert.Null(store.PeekLastSnapshot());

        store.SetCustomName("id1", "Alpha");
        await store.SaveAsync();

        var snapshot = store.PeekLastSnapshot();
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.ContainsKey("id1"));
        Assert.Equal("Alpha", snapshot["id1"]);
    }

    [Fact]
    public async Task LastSavedSnapshot_OnAStoreBuiltOverAnExistingFile_IsTheFileContent()
    {
        // The rollback target has to be what is actually on disk, not "empty until we write once".
        var writer = new SessionNameStore(_tempDir);
        writer.SetCustomName("id1", "Alpha");
        await writer.SaveAsync();

        var snapshot = new SessionNameStore(_tempDir).PeekLastSnapshot();

        Assert.NotNull(snapshot);
        Assert.Equal("Alpha", snapshot!["id1"]);
    }

    // --- Finding 25: a failed write must not leave the UI showing an unpersisted name ---

    [Fact]
    public async Task SaveAsync_WhenThePublishFails_RevertsToThePersistedNameAndRaisesNameChanged()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("id1", "Alpha");
        Assert.True(await store.SaveAsync());

        var raised = new List<string>();
        store.NameChanged += (_, args) => raised.Add(args.SessionId);
        store.SetCustomName("id1", "Renamed");

        bool saved;
        using (File.Open(Path.Combine(_tempDir, StoreFileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saved = await store.SaveAsync();
        }

        Assert.False(saved);
        Assert.Equal("Alpha", store.GetCustomName("id1"));
        Assert.Equal(new[] { "id1", "id1" }, raised);   // once for the edit, once for the rollback
        Assert.False(File.Exists(Path.Combine(_tempDir, TempFileName)));
    }

    [Fact]
    public async Task SaveAsync_WhenThePublishFails_AndNothingWasEverPersisted_DropsTheName()
    {
        var blockerPath = Path.Combine(_tempDir, "blocker");
        File.WriteAllText(blockerPath, "I am a file, not a directory");
        var store = new SessionNameStore(blockerPath);
        store.SetCustomName("id1", "Alpha");

        Assert.False(await store.SaveAsync());
        Assert.Null(store.GetCustomName("id1"));
    }

    [Fact]
    public async Task SaveAsync_WhenThePublishFails_RestoresANameWhoseRemovalNeverReachedDisk()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("id1", "Alpha");
        Assert.True(await store.SaveAsync());

        store.ClearCustomName("id1");

        bool saved;
        using (File.Open(Path.Combine(_tempDir, StoreFileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saved = await store.SaveAsync();
        }

        Assert.False(saved);
        Assert.Equal("Alpha", store.GetCustomName("id1"));
    }

    // --- Finding 30 / Finding 8 ---

    [Fact]
    public void GetKnownSessionIds_ReturnsEveryKeyIncludingOrphans()
    {
        var store = new SessionNameStore(_tempDir);
        store.SetCustomName("live", "Alpha");
        store.SetCustomName("deleted-session", "Beta");
        store.SetCustomName("cleared", "Gamma");
        store.ClearCustomName("cleared");

        Assert.Equal(
            new[] { "deleted-session", "live" },
            store.GetKnownSessionIds().OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void SaveAsync_WhileTheCallingThreadBlocksInSave_CompletesWithoutTheDispatcher()
    {
        var store = new SessionNameStore(_tempDir);
        for (var i = 0; i < NamesLargeEnoughToYield; i++)
        {
            store.SetCustomName($"id{i}", $"Name{i}");
        }

        var pump = new RecordingPumpContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(pump);
        try
        {
            var pending = store.SaveAsync();

            // Without ConfigureAwait(false) the release continuation is queued to this pump, which
            // the blocked thread below can never drain.
            Assert.True(store.Save());

            Assert.True(pending.Wait(PendingWriteTimeout), "the async write never completed");
            Assert.Equal(0, pump.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

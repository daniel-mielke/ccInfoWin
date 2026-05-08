using CCInfoWindows.Models;
using CCInfoWindows.Services;
using System.Text.Json;
using Xunit;

namespace CCInfoWindows.Tests.Services;

public class SessionNameStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SessionNameStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ccinfo-rename-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
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
}

using CCInfoWindows.Services;
using CCInfoWindows.Tests.TestSupport;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Integration tests proving FileSystemWatcher in JsonlService correctly detects
/// file-level .jsonl changes in subdirectories (SESW-01 regression lock).
/// </summary>
[Trait("Category", "Integration")]
public class JsonlServiceWatcherTests : JsonlServiceTestBase, IAsyncDisposable
{
    private const int PositiveTimeoutSeconds = 5;
    private const int NegativeTimeoutSeconds = 4;

    private JsonlService? _service;

    public JsonlServiceWatcherTests() : base("jsonl-watcher-")
    {
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseServices();
        _service = null;

        // Brief delay to allow watcher handles to release before deleting the directory
        await Task.Delay(200);

        Dispose();
    }

    [Fact]
    public async Task WatcherDetectsNewJsonlFileInSubdirectory()
    {
        _service = BuildService();
        await _service.InitializeAsync();

        var dataUpdatedFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DataUpdated += (_, _) => dataUpdatedFired.TrySetResult(true);

        var projectSubDir = Path.Combine(ProjectsDir, "test-project");
        Directory.CreateDirectory(projectSubDir);
        await File.WriteAllTextAsync(Path.Combine(projectSubDir, "session.jsonl"), "{}\n");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(PositiveTimeoutSeconds));
        cts.Token.Register(() => dataUpdatedFired.TrySetCanceled());

        var eventFired = await dataUpdatedFired.Task;
        Assert.True(eventFired, "DataUpdated should fire when a .jsonl file is created in a subdirectory.");
    }

    [Fact]
    public async Task WatcherDetectsModifiedJsonlFile()
    {
        _service = BuildService();
        await _service.InitializeAsync();

        var projectSubDir = Path.Combine(ProjectsDir, "test-project-modify");
        Directory.CreateDirectory(projectSubDir);
        var filePath = Path.Combine(projectSubDir, "session.jsonl");
        await File.WriteAllTextAsync(filePath, "{}\n");

        // Wait for the first DataUpdated (creation)
        var firstUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DataUpdated += (_, _) => firstUpdate.TrySetResult(true);

        using var firstCts = new CancellationTokenSource(TimeSpan.FromSeconds(PositiveTimeoutSeconds));
        firstCts.Token.Register(() => firstUpdate.TrySetCanceled());

        await firstUpdate.Task;

        // Now subscribe for the second DataUpdated (modification)
        var secondUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DataUpdated += (_, _) => secondUpdate.TrySetResult(true);

        await File.AppendAllTextAsync(filePath, "{\"extra\": true}\n");

        using var secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(PositiveTimeoutSeconds));
        secondCts.Token.Register(() => secondUpdate.TrySetCanceled());

        var eventFired = await secondUpdate.Task;
        Assert.True(eventFired, "DataUpdated should fire when an existing .jsonl file is modified.");
    }

    [Fact]
    public async Task WatcherIgnoresNonJsonlFiles()
    {
        _service = BuildService();
        await _service.InitializeAsync();

        var dataUpdatedFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DataUpdated += (_, _) => dataUpdatedFired.TrySetResult(true);

        await File.WriteAllTextAsync(Path.Combine(ProjectsDir, "irrelevant.txt"), "not jsonl\n");

        var delayTask = Task.Delay(TimeSpan.FromSeconds(NegativeTimeoutSeconds));
        var completedFirst = await Task.WhenAny(dataUpdatedFired.Task, delayTask);

        Assert.Same(delayTask, completedFirst);
    }

    /// <summary>
    /// Deletion was the one file event the watcher never subscribed, so a removed session file left
    /// its cached read position and the per-project newest-file pointer behind. A Deleted event is
    /// the only thing that can fire here — no Changed event matches a vanished *.jsonl — so a
    /// DataUpdated within the window proves the subscription exists and its handler ran to the end.
    /// </summary>
    [Fact]
    public async Task WatcherDetectsDeletedJsonlFile()
    {
        var projectSubDir = Path.Combine(ProjectsDir, "test-project-delete");
        Directory.CreateDirectory(projectSubDir);
        var filePath = Path.Combine(projectSubDir, "session.jsonl");
        await File.WriteAllTextAsync(filePath, "{}\n");

        _service = BuildService();
        await _service.InitializeAsync();

        var deletionSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DataUpdated += (_, _) => deletionSeen.TrySetResult(true);

        File.Delete(filePath);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(PositiveTimeoutSeconds));
        cts.Token.Register(() => deletionSeen.TrySetCanceled());

        var eventFired = await deletionSeen.Task;
        Assert.True(eventFired, "DataUpdated should fire when a watched .jsonl file is deleted.");
    }

}

using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;


/// <summary>
/// Contract for reading and watching Claude Code JSONL session files.
///
/// Lifecycle belongs to the application host: App.StartBackgroundServices calls
/// <see cref="InitializeAsync"/> once per process and MainWindow.OnClosing calls <see cref="Stop"/>.
/// Everything else — ViewModels above all — is a reader: subscribe to <see cref="DataUpdated"/> and
/// read <see cref="Sessions"/>. A transient ViewModel driving Start/Stop from a page's visual-tree
/// membership disposed the watcher on every Settings round-trip and re-scanned the whole corpus on the
/// way back (finding 29).
/// </summary>
public interface IJsonlService
{
    /// <summary>
    /// All discovered sessions. Each read returns an immutable snapshot that is replaced wholesale
    /// when a scan or a file-change batch completes, so a held reference never changes underneath the
    /// caller — re-read the property after <see cref="DataUpdated"/> to see new data.
    /// </summary>
    IReadOnlyList<SessionInfo> Sessions { get; }

    /// <summary>True while the initial directory scan is in progress.</summary>
    bool IsScanning { get; }

    /// <summary>Raised whenever any JSONL data changes (new entries or new files).</summary>
    event EventHandler? DataUpdated;

    /// <summary>
    /// Returns aggregated context window state for the given session, including one entry per
    /// currently active subagent. Returns <see cref="ContextWindowData.Empty"/> when the session is
    /// unknown or its newest JSONL file cannot be read.
    /// </summary>
    ContextWindowData GetContextWindow(string sessionId);

    /// <summary>
    /// Performs the initial directory scan and starts the file watcher. Returns immediately when a scan
    /// is already in progress. A scan cancelled by <see cref="Stop"/> publishes nothing and leaves the
    /// previous snapshot in place.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Stops the file watcher, cancels a scan still in flight, and releases resources.
    /// </summary>
    void Stop();

    /// <summary>
    /// Returns aggregated token counts, cost, and burn rate data for the given time period.
    /// For Session: pass the sessionId (project directory name).
    /// For Today/Week/Month: sessionId is ignored; all projects are aggregated.
    /// </summary>
    StatisticsSummary GetStatistics(TimePeriod period, string? sessionId = null);
}

using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Contract for reading and watching Claude Code JSONL session files.
/// </summary>
public interface IJsonlService
{
    /// <summary>All discovered sessions, updated as files are scanned.</summary>
    IReadOnlyList<SessionInfo> Sessions { get; }

    /// <summary>True while the initial directory scan is in progress.</summary>
    bool IsScanning { get; }

    /// <summary>Raised whenever any JSONL data changes (new entries or new files).</summary>
    event EventHandler? DataUpdated;

    /// <summary>Returns aggregated context window state for the given session.</summary>
    ContextWindowData GetContextWindow(string sessionId);

    /// <summary>Returns context window state for a specific subagent within a session.</summary>
    ContextWindowData GetSubagentContext(string sessionId, string agentId);

    /// <summary>Returns aggregated token counts for the given session.</summary>
    TokenSummary GetTokenSummary(string sessionId);

    /// <summary>Performs initial directory scan and starts the file watcher.</summary>
    Task InitializeAsync();

    /// <summary>Stops the file watcher and releases resources.</summary>
    void Stop();
}

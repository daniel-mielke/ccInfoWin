using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Persists usage history points across app restarts for chart rendering.
///
/// All writes are best-effort: a failure is logged via AppLog and swallowed, because losing a
/// history flush must never take the dashboard down (the next poll rewrites the file).
/// </summary>
public interface IUsageHistoryService
{
    UsageHistory LoadHistory();

    /// <summary>
    /// Synchronous flush for the termination path, which cannot await. Blocks only for a bounded
    /// time and skips the flush if the write lock is still held, so it can never hang the UI thread.
    /// </summary>
    void SaveHistory(UsageHistory history);

    /// <summary>Deletes the persisted history. Bounded like SaveHistory.</summary>
    void ClearHistory();

    // D-06: async sibling of SaveHistory for the poll-cycle path (HIST-02, HIST-03)
    Task SaveHistoryAsync(UsageHistory history);

    // D-04: live-snapshot accessor for the termination hook (HIST-01 -- consumed by Plan 21-02)
    UsageHistory? PeekLastSnapshot();
}

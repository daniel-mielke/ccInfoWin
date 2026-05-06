using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Persists usage history points across app restarts for chart rendering.
/// </summary>
public interface IUsageHistoryService
{
    UsageHistory LoadHistory();
    void SaveHistory(UsageHistory history);
    void ClearHistory();

    // D-06: async sibling of SaveHistory for the poll-cycle path (HIST-02, HIST-03)
    Task SaveHistoryAsync(UsageHistory history);

    // D-04: live-snapshot accessor for the termination hook (HIST-01 -- consumed by Plan 21-02)
    UsageHistory? PeekLastSnapshot();
}

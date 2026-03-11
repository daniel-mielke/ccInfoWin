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
}

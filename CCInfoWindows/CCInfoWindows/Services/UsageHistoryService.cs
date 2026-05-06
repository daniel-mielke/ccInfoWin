using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Reads/writes usage-history.json in %LOCALAPPDATA%\CCInfoWindows\.
/// Handles missing or corrupt files gracefully by returning empty defaults.
/// </summary>
public class UsageHistoryService : IUsageHistoryService
{
    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyDirectory;
    private string HistoryFilePath => Path.Combine(_historyDirectory, "usage-history.json");

    // D-05: SemaphoreSlim serializes sync and async writes -- never use lock keyword (cannot hold across await)
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // D-04: updated AFTER each successful write; null after ClearHistory
    private UsageHistory? _lastSavedSnapshot;

    public UsageHistoryService() : this(DefaultDirectory)
    {
    }

    public UsageHistoryService(string directoryOverride)
    {
        _historyDirectory = directoryOverride;
    }

    public UsageHistory LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryFilePath))
            {
                return new UsageHistory();
            }

            var json = File.ReadAllText(HistoryFilePath);
            return JsonSerializer.Deserialize<UsageHistory>(json, JsonOptions) ?? new UsageHistory();
        }
        catch
        {
            return new UsageHistory();
        }
    }

    public void SaveHistory(UsageHistory history)
    {
        _writeLock.Wait();
        try
        {
            try
            {
                Directory.CreateDirectory(_historyDirectory);
                var json = JsonSerializer.Serialize(history, JsonOptions);
                File.WriteAllText(HistoryFilePath, json);
                _lastSavedSnapshot = history;   // AFTER successful write -- RESEARCH Pitfall 2
            }
            catch
            {
                // Best-effort save -- don't crash the app
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveHistoryAsync(UsageHistory history)
    {
        await _writeLock.WaitAsync();
        try
        {
            try
            {
                Directory.CreateDirectory(_historyDirectory);
                var json = JsonSerializer.Serialize(history, JsonOptions);
                await File.WriteAllTextAsync(HistoryFilePath, json);
                _lastSavedSnapshot = history;   // AFTER successful write -- RESEARCH Pitfall 2
            }
            catch
            {
                // Best-effort save -- don't crash the app
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void ClearHistory()
    {
        _writeLock.Wait();
        try
        {
            try
            {
                _lastSavedSnapshot = null;      // 1. Invalidate cache FIRST (D-13)
                File.Delete(HistoryFilePath);   // 2. Then delete on disk
            }
            catch
            {
                // No-op if file not found
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // D-04: atomic reference read; producer side is locked via _writeLock
    public UsageHistory? PeekLastSnapshot() => _lastSavedSnapshot;
}

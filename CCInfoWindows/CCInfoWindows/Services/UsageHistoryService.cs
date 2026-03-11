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
        try
        {
            Directory.CreateDirectory(_historyDirectory);
            var json = JsonSerializer.Serialize(history, JsonOptions);
            File.WriteAllText(HistoryFilePath, json);
        }
        catch
        {
            // Best-effort save -- don't crash the app
        }
    }

    public void ClearHistory()
    {
        try
        {
            File.Delete(HistoryFilePath);
        }
        catch
        {
            // No-op if file not found
        }
    }
}

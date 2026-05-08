using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Persists session custom names to %LOCALAPPDATA%\CCInfoWindows\session-names.json (RENAME-03, RENAME-07).
/// Mirrors UsageHistoryService G-2 pattern with one delta: writes use atomic rename (tmp + File.Move)
/// per PITFALLS A2-P1 so concurrent readers without the semaphore never see a partial file.
///
/// Threading: SemaphoreSlim _writeLock serializes all I/O. NameChanged is raised inside the lock
/// AFTER the in-memory map is mutated but BEFORE persistence completes — handlers must marshal to
/// the UI thread via IDispatcherQueue per G-1 (not enforced here; consumer's responsibility).
/// </summary>
public class SessionNameStore : ISessionNameStore
{
    private const string FileName = "session-names.json";

    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCInfoWindows");

    // UnsafeRelaxedJsonEscaping: keep emoji/CJK readable in the file (PITFALLS A2-P2)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _directory;
    private string FilePath => Path.Combine(_directory, FileName);
    private string TempFilePath => Path.Combine(_directory, FileName + ".tmp");

    // G-2: SemaphoreSlim — never use lock keyword (cannot hold across await)
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // In-memory authoritative state. ConcurrentDictionary protects reads from background SaveAsync producers.
    private readonly ConcurrentDictionary<string, string> _names;

    // _lastSavedSnapshot for crash-safety / read-without-disk-hit (G-2 invariant)
    private Dictionary<string, string>? _lastSavedSnapshot;

    public event EventHandler<SessionNameChangedEventArgs>? NameChanged;

    public SessionNameStore() : this(DefaultDirectory) { }

    public SessionNameStore(string directoryOverride)
    {
        _directory = directoryOverride;
        _names = new ConcurrentDictionary<string, string>(LoadFromDisk());
    }

    private Dictionary<string, string> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Dictionary<string, string>();
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return loaded ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public string? GetCustomName(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        return _names.TryGetValue(sessionId, out var v) && !string.IsNullOrEmpty(v) ? v : null;
    }

    public void SetCustomName(string sessionId, string customName)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        // D-07: belt-and-suspenders — strip control chars even if the caller already sanitized.
        var clean = SessionNameSanitizer.Strip(customName);

        if (string.IsNullOrEmpty(clean))
        {
            _names.TryRemove(sessionId, out _);
        }
        else
        {
            _names[sessionId] = clean;
        }

        NameChanged?.Invoke(this, new SessionNameChangedEventArgs { SessionId = sessionId });
    }

    public void ClearCustomName(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _names.TryRemove(sessionId, out _);
        NameChanged?.Invoke(this, new SessionNameChangedEventArgs { SessionId = sessionId });
    }

    public bool Save()
    {
        _writeLock.Wait();
        try
        {
            return WriteToDisk(snapshot: _names.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            return await WriteToDiskAsync(
                snapshot: _names.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                ct);
        }
        finally { _writeLock.Release(); }
    }

    // PITFALLS A2-P1: atomic rename — write to .tmp then File.Move(overwrite:true)
    private bool WriteToDisk(Dictionary<string, string> snapshot)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(TempFilePath, json);
            File.Move(TempFilePath, FilePath, overwrite: true);
            _lastSavedSnapshot = snapshot;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> WriteToDiskAsync(Dictionary<string, string> snapshot, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(TempFilePath, json, ct);
            File.Move(TempFilePath, FilePath, overwrite: true);
            _lastSavedSnapshot = snapshot;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // For test introspection (G-2 _lastSavedSnapshot invariant)
    internal IReadOnlyDictionary<string, string>? PeekLastSnapshot() => _lastSavedSnapshot;
}

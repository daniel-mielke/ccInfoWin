using System.Diagnostics;
using System.Text.Json;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Services;

/// <summary>
/// Reads Claude Code JSONL session files, maintains an in-memory session index,
/// and uses a FileSystemWatcher for live updates.
/// </summary>
public sealed class JsonlService : IJsonlService, IDisposable
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const int TailWindowBytes = 1_048_576; // 1 MB
    private const int WatcherInternalBufferSize = 65_536; // 64 KB
    private const int DebounceMilliseconds = 300;
    private const string CacheFileName = "jsonl-cache.json";
    private const string SubagentsDirectoryName = "subagents";
    private const string AgentFilePattern = "agent-*.jsonl";
    private const string JsonlFilePattern = "*.jsonl";

    // -------------------------------------------------------------------------
    // Internal per-session aggregation
    // -------------------------------------------------------------------------

    private sealed class SessionData
    {
        public string? Cwd { get; set; }
        public string? ModelName { get; set; }
        public DateTimeOffset LastActivity { get; set; }
        public JsonlEntry? LastAssistantEntry { get; set; }
        public long TotalInputTokens { get; set; }
        public long TotalOutputTokens { get; set; }
        public HashSet<string> SeenIds { get; } = [];
        public List<string> SubagentFiles { get; } = [];
    }

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly string _projectsDirectory;
    private readonly string _cacheDirectory;
    private readonly Lock _sessionsLock = new();
    private readonly object _debounceLock = new();

    private List<SessionInfo> _sessions = [];
    private Dictionary<string, SessionData> _sessionData = [];
    private Dictionary<string, FilePositionMarker> _filePositions = [];
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private bool _isScanning;
    private int _watcherRestartCount;

    private const int MaxWatcherRestarts = 5;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <param name="projectsDirectoryOverride">Override for test isolation. Defaults to %USERPROFILE%\.claude\projects.</param>
    /// <param name="cacheDirectoryOverride">Override for test isolation. Defaults to %LOCALAPPDATA%\CCInfoWindows.</param>
    public JsonlService(string? projectsDirectoryOverride = null, string? cacheDirectoryOverride = null)
    {
        _projectsDirectory = projectsDirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        _cacheDirectory = cacheDirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCInfoWindows");
    }

    // -------------------------------------------------------------------------
    // IJsonlService
    // -------------------------------------------------------------------------

    public IReadOnlyList<SessionInfo> Sessions
    {
        get
        {
            lock (_sessionsLock)
                return _sessions.AsReadOnly();
        }
    }

    public bool IsScanning => _isScanning;

    public event EventHandler? DataUpdated;

    public ContextWindowData GetContextWindow(string sessionId)
    {
        lock (_sessionsLock)
        {
            if (!_sessionData.TryGetValue(sessionId, out var data))
                return ContextWindowData.Empty;

            var entry = data.LastAssistantEntry;
            if (entry is null)
                return ContextWindowData.Empty;

            var totalTokens = ComputeContextTokens(entry);
            var modelName = entry.Message?.Model;
            var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName);
            var subagents = BuildSubagentContext(data.SubagentFiles);

            return new ContextWindowData
            {
                TotalTokens = totalTokens,
                MaxTokens = maxTokens,
                ModelName = modelName,
                ShouldWarnAutocompact = ModelContextLimits.ShouldWarnAutocompact(totalTokens, maxTokens),
                Subagents = subagents
            };
        }
    }

    public ContextWindowData GetSubagentContext(string sessionId, string agentId)
    {
        lock (_sessionsLock)
        {
            if (!_sessionData.TryGetValue(sessionId, out var data))
                return ContextWindowData.Empty;

            var subagents = BuildSubagentContext(data.SubagentFiles);
            var agent = subagents.FirstOrDefault(a => a.AgentId == agentId);
            if (agent is null)
                return ContextWindowData.Empty;

            return new ContextWindowData
            {
                TotalTokens = agent.TotalTokens,
                MaxTokens = agent.MaxTokens,
                ModelName = agent.ModelName,
                ShouldWarnAutocompact = ModelContextLimits.ShouldWarnAutocompact(agent.TotalTokens, agent.MaxTokens),
                Subagents = []
            };
        }
    }

    public TokenSummary GetTokenSummary(string sessionId)
    {
        lock (_sessionsLock)
        {
            if (!_sessionData.TryGetValue(sessionId, out var data))
                return TokenSummary.Empty;

            return new TokenSummary
            {
                InputTokens = data.TotalInputTokens,
                OutputTokens = data.TotalOutputTokens
            };
        }
    }

    public async Task InitializeAsync()
    {
        _isScanning = true;

        LoadCache();
        SeedSessionDataFromCache();
        RaiseDataUpdated();

        await Task.Run(DiscoverSessions);

        _isScanning = false;
        SaveCache();
        RaiseDataUpdated();

        StartWatching();
    }

    public void Stop()
    {
        DisposeWatcher();
        DisposeDebounceTimer();
    }

    public void Dispose() => Stop();

    // -------------------------------------------------------------------------
    // File reading — public for testability
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads only the last TailWindowBytes of the file.
    /// Discards the first partial line when the seek position is > 0.
    /// Opens with FileShare.ReadWrite to avoid locking conflicts with Claude Code.
    /// </summary>
    public static IEnumerable<string> ReadTailLines(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var seekPosition = Math.Max(0L, stream.Length - TailWindowBytes);
        stream.Seek(seekPosition, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);

        if (seekPosition > 0)
            reader.ReadLine(); // discard first partial line

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    /// <summary>
    /// Reads only lines added after startPosition.
    /// Returns the new file position for the next incremental read.
    /// </summary>
    public static (List<string> Lines, long NewPosition) ReadIncrementalLines(string filePath, long startPosition)
    {
        var lines = new List<string>();

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (startPosition >= stream.Length)
            return (lines, stream.Length);

        stream.Seek(startPosition, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return (lines, stream.Length);
    }

    /// <summary>
    /// Deserializes JSONL lines into JsonlEntry records, skipping malformed lines.
    /// </summary>
    public static IEnumerable<JsonlEntry> ParseJsonlEntries(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            JsonlEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<JsonlEntry>(line, JsonlEntry.DefaultOptions);
            }
            catch
            {
                // Skip malformed JSONL lines — tolerant parsing is a must-have
            }

            if (entry is not null)
                yield return entry;
        }
    }

    // -------------------------------------------------------------------------
    // Session discovery
    // -------------------------------------------------------------------------

    private void DiscoverSessions()
    {
        if (!Directory.Exists(_projectsDirectory))
            return;

        var allFiles = Directory.GetFiles(_projectsDirectory, JsonlFilePattern, SearchOption.AllDirectories);
        var mainSessionFiles = allFiles
            .Where(f => !IsSubagentFile(f))
            .ToArray();

        lock (_sessionsLock)
        {
            foreach (var file in mainSessionFiles)
                ParseFile(file);

            foreach (var (sessionId, data) in _sessionData)
                data.SubagentFiles.Clear();

            // Discover subagent files and associate with sessions
            foreach (var sessionId in _sessionData.Keys)
            {
                var subagentFiles = FindSubagentFiles(sessionId);
                _sessionData[sessionId].SubagentFiles.AddRange(subagentFiles);
            }

            RebuildSessionsList();
        }
    }

    private void ParseFile(string filePath)
    {
        var isIncremental = _filePositions.TryGetValue(filePath, out var marker);
        IEnumerable<string> lines;
        long newPosition;

        if (isIncremental && marker is not null)
        {
            var (incrementalLines, pos) = ReadIncrementalLines(filePath, marker.LastReadPosition);
            lines = incrementalLines;
            newPosition = pos;
        }
        else
        {
            var tailLines = ReadTailLines(filePath).ToList();
            lines = tailLines;
            newPosition = new FileInfo(filePath).Length;
        }

        var entries = ParseJsonlEntries(lines).ToList();
        if (entries.Count == 0)
        {
            UpdateFilePosition(filePath, newPosition);
            return;
        }

        var firstEntry = entries[0];
        var sessionId = firstEntry.SessionId ?? ExtractSessionIdFromPath(filePath);

        if (string.IsNullOrEmpty(sessionId))
        {
            UpdateFilePosition(filePath, newPosition);
            return;
        }

        if (!_sessionData.TryGetValue(sessionId, out var data))
        {
            data = new SessionData();
            _sessionData[sessionId] = data;
        }

        // Populate session metadata from first entry if not yet set
        if (string.IsNullOrEmpty(data.Cwd))
            data.Cwd = firstEntry.Cwd;

        foreach (var entry in entries)
            ApplyEntryToSessionData(entry, data);

        UpdateFilePosition(filePath, newPosition);
    }

    private static void ApplyEntryToSessionData(JsonlEntry entry, SessionData data)
    {
        if (entry.Timestamp.HasValue && entry.Timestamp > data.LastActivity)
            data.LastActivity = entry.Timestamp.Value;

        if (!IsRelevantAssistantEntry(entry))
            return;

        var deduplicationKey = BuildDeduplicationKey(entry);

        if (!string.IsNullOrEmpty(deduplicationKey) && !data.SeenIds.Add(deduplicationKey))
            return; // Already counted this entry

        // Track last assistant entry for context window (replaces previous)
        data.LastAssistantEntry = entry;

        var usage = entry.Message?.Usage;
        if (usage is null)
            return;

        data.TotalInputTokens += usage.InputTokens ?? 0;
        data.TotalOutputTokens += usage.OutputTokens ?? 0;

        var model = entry.Message?.Model;
        if (!string.IsNullOrEmpty(model))
            data.ModelName = model;
    }

    private static bool IsRelevantAssistantEntry(JsonlEntry entry) =>
        string.Equals(entry.Type, "assistant", StringComparison.OrdinalIgnoreCase)
        && !entry.IsSidechain;

    private static string BuildDeduplicationKey(JsonlEntry entry) =>
        $"{entry.Uuid}|{entry.RequestId}";

    private static bool IsSubagentFile(string filePath) =>
        filePath.Contains(Path.DirectorySeparatorChar + SubagentsDirectoryName + Path.DirectorySeparatorChar)
        || filePath.Contains('/' + SubagentsDirectoryName + '/');

    private static string ExtractSessionIdFromPath(string filePath) =>
        Path.GetFileNameWithoutExtension(filePath);

    private List<string> FindSubagentFiles(string sessionId)
    {
        var result = new List<string>();

        if (!Directory.Exists(_projectsDirectory))
            return result;

        foreach (var projectDir in Directory.GetDirectories(_projectsDirectory))
        {
            var subagentDir = Path.Combine(projectDir, sessionId, SubagentsDirectoryName);
            if (!Directory.Exists(subagentDir))
                continue;

            result.AddRange(Directory.GetFiles(subagentDir, AgentFilePattern));
        }

        // Also check direct subagent directories under project folders
        foreach (var projectDir in Directory.GetDirectories(_projectsDirectory))
        {
            var subagentDir = Path.Combine(projectDir, SubagentsDirectoryName);
            if (Directory.Exists(subagentDir))
                result.AddRange(Directory.GetFiles(subagentDir, AgentFilePattern));
        }

        return result.Distinct().ToList();
    }

    private static IReadOnlyList<SubagentContextData> BuildSubagentContext(List<string> subagentFiles)
    {
        var result = new List<SubagentContextData>();

        foreach (var file in subagentFiles)
        {
            try
            {
                var lines = ReadTailLines(file);
                var entries = ParseJsonlEntries(lines)
                    .Where(e => IsRelevantAssistantEntry(e))
                    .ToList();

                if (entries.Count == 0)
                    continue;

                var lastEntry = entries[^1];
                var totalTokens = ComputeContextTokens(lastEntry);
                var modelName = lastEntry.Message?.Model;
                var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName);
                var agentId = ExtractAgentId(file);

                result.Add(new SubagentContextData
                {
                    AgentId = agentId,
                    TotalTokens = totalTokens,
                    MaxTokens = maxTokens,
                    ModelName = modelName
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JsonlService] Failed to parse subagent file {file}: {ex.Message}");
            }
        }

        return result;
    }

    private static string ExtractAgentId(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.StartsWith("agent-", StringComparison.OrdinalIgnoreCase)
            ? fileName["agent-".Length..]
            : fileName;
    }

    private static long ComputeContextTokens(JsonlEntry entry)
    {
        var usage = entry.Message?.Usage;
        if (usage is null)
            return 0L;

        return (usage.InputTokens ?? 0)
            + (usage.CacheReadInputTokens ?? 0)
            + (usage.CacheCreationInputTokens ?? 0);
    }

    private void RebuildSessionsList()
    {
        _sessions = _sessionData
            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
            .Select(kvp => new SessionInfo
            {
                Id = kvp.Key,
                Cwd = kvp.Value.Cwd ?? string.Empty,
                DisplayName = SessionNameHelper.GetDisplayName(kvp.Value.Cwd),
                LastActivity = kvp.Value.LastActivity,
                ModelName = kvp.Value.ModelName
            })
            .OrderByDescending(s => s.LastActivity)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // FileSystemWatcher
    // -------------------------------------------------------------------------

    private void StartWatching()
    {
        if (!Directory.Exists(_projectsDirectory))
            Directory.CreateDirectory(_projectsDirectory);

        var watcher = new FileSystemWatcher(_projectsDirectory)
        {
            Filter = JsonlFilePattern,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            InternalBufferSize = WatcherInternalBufferSize
        };

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;

        _watcher = watcher;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(
                _ => ProcessFileChangeAsync(e.FullPath),
                state: null,
                dueTime: DebounceMilliseconds,
                period: System.Threading.Timeout.Infinite);
        }
    }

    private void ProcessFileChangeAsync(string filePath)
    {
        try
        {
            lock (_sessionsLock)
            {
                ParseFile(filePath);

                // Re-discover subagent files for updated sessions
                var sessionId = ExtractSessionIdFromPath(filePath);
                if (_sessionData.TryGetValue(sessionId, out var data))
                {
                    data.SubagentFiles.Clear();
                    data.SubagentFiles.AddRange(FindSubagentFiles(sessionId));
                }

                RebuildSessionsList();
            }

            SaveCache();
            RaiseDataUpdated();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JsonlService] Error processing file change for {filePath}: {ex.Message}");
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"[JsonlService] Watcher error: {e.GetException()?.Message}");

        if (_watcherRestartCount >= MaxWatcherRestarts)
        {
            Debug.WriteLine("[JsonlService] Max watcher restarts reached — giving up.");
            return;
        }

        _watcherRestartCount++;
        DisposeWatcher();

        try
        {
            StartWatching();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JsonlService] Failed to restart watcher: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Cache
    // -------------------------------------------------------------------------

    private void LoadCache()
    {
        var cacheFile = CacheFilePath();

        if (!File.Exists(cacheFile))
            return;

        try
        {
            var json = File.ReadAllText(cacheFile);
            var cache = JsonSerializer.Deserialize<JsonlCache>(json);

            if (cache is null)
                return;

            _filePositions = cache.FilePositions ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JsonlService] Failed to load cache: {ex.Message}");
        }
    }

    private void SeedSessionDataFromCache()
    {
        // The cache file positions allow incremental reads.
        // Session data will be rebuilt from the actual files during DiscoverSessions.
    }

    private void SaveCache()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                Directory.CreateDirectory(_cacheDirectory);

            var cache = new JsonlCache
            {
                FilePositions = _filePositions
            };

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(CacheFilePath(), json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JsonlService] Failed to save cache: {ex.Message}");
        }
    }

    private string CacheFilePath() => Path.Combine(_cacheDirectory, CacheFileName);

    private void UpdateFilePosition(string filePath, long newPosition)
    {
        _filePositions[filePath] = new FilePositionMarker
        {
            LastReadPosition = newPosition,
            FileSize = newPosition,
            LastWriteTime = DateTimeOffset.UtcNow
        };
    }

    // -------------------------------------------------------------------------
    // Event helpers
    // -------------------------------------------------------------------------

    private void RaiseDataUpdated() =>
        DataUpdated?.Invoke(this, EventArgs.Empty);

    // -------------------------------------------------------------------------
    // Dispose helpers
    // -------------------------------------------------------------------------

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void DisposeDebounceTimer()
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}

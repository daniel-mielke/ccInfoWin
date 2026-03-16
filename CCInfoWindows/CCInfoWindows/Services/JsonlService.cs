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
    // Internal per-project aggregation (keyed by project directory name)
    // -------------------------------------------------------------------------

    private sealed class ProjectData
    {
        public string ProjectDirName { get; set; } = string.Empty;
        public string? Cwd { get; set; }
        public string? ModelName { get; set; }
        public DateTimeOffset LastActivity { get; set; }
        public JsonlEntry? LastAssistantEntry { get; set; }
        public long TotalInputTokens { get; set; }
        public long TotalOutputTokens { get; set; }
        public long TotalCacheCreationTokens { get; set; }
        public long TotalCacheReadTokens { get; set; }
        public HashSet<string> SeenIds { get; } = [];
        public string? NewestSessionFile { get; set; }
        public DateTimeOffset NewestSessionModTime { get; set; }

        /// <summary>
        /// Compact per-entry log for time-period filtering.
        /// Stores token breakdown, cost, and model per assistant entry.
        /// Roughly 120 bytes per entry — keeps time-period aggregation in memory.
        /// </summary>
        public List<EntryLogItem> EntryLog { get; } = [];
    }

    /// <summary>
    /// Compact record of a single JSONL assistant entry for time-period aggregation.
    /// </summary>
    private sealed class EntryLogItem
    {
        public DateTimeOffset Timestamp { get; init; }
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
        public long CacheCreationTokens { get; init; }
        public long CacheReadTokens { get; init; }
        public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;
        public decimal? CostUsd { get; init; }
        public string? ModelName { get; init; }
        public string DeduplicationKey { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
    }

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly string _projectsDirectory;
    private readonly string _cacheDirectory;
    private readonly IPricingService _pricingService;
    private readonly Lock _sessionsLock = new();
    private readonly object _debounceLock = new();

    private List<SessionInfo> _sessions = [];
    private Dictionary<string, ProjectData> _projectData = [];
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
    /// <param name="pricingService">Pricing service for cost calculation. Required for GetStatistics.</param>
    public JsonlService(
        string? projectsDirectoryOverride = null,
        string? cacheDirectoryOverride = null,
        IPricingService? pricingService = null)
    {
        _projectsDirectory = projectsDirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        _cacheDirectory = cacheDirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCInfoWindows");

        _pricingService = pricingService ?? new NullPricingService();
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

    public ContextWindowData GetContextWindow(string projectDirName)
    {
        lock (_sessionsLock)
        {
            if (!_projectData.TryGetValue(projectDirName, out var data))
                return ContextWindowData.Empty;

            var entry = data.LastAssistantEntry;
            if (entry is null)
                return ContextWindowData.Empty;

            var totalTokens = ComputeContextTokens(entry);
            var modelName = entry.Message?.Model;
            var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName);
            var subagentFiles = FindSubagentFilesForNewestSession(data);
            var subagents = BuildSubagentContext(subagentFiles);

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

    public ContextWindowData GetSubagentContext(string projectDirName, string agentId)
    {
        lock (_sessionsLock)
        {
            if (!_projectData.TryGetValue(projectDirName, out var data))
                return ContextWindowData.Empty;

            var subagentFiles = FindSubagentFilesForNewestSession(data);
            var subagents = BuildSubagentContext(subagentFiles);
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

    public TokenSummary GetTokenSummary(string projectDirName)
    {
        lock (_sessionsLock)
        {
            if (!_projectData.TryGetValue(projectDirName, out var data))
                return TokenSummary.Empty;

            return new TokenSummary
            {
                InputTokens = data.TotalInputTokens,
                OutputTokens = data.TotalOutputTokens
            };
        }
    }

    public StatisticsSummary GetStatistics(TimePeriod period, string? sessionId = null)
    {
        lock (_sessionsLock)
        {
            return period == TimePeriod.Session
                ? BuildSessionStatistics(sessionId)
                : BuildTimePeriodStatistics(period);
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
    // Statistics aggregation
    // -------------------------------------------------------------------------

    private StatisticsSummary BuildSessionStatistics(string? sessionId)
    {
        if (sessionId is null || !_projectData.TryGetValue(sessionId, out var data))
            return StatisticsSummary.Empty;

        // Session = start of current hour (matches macOS reference app)
        var now = DateTimeOffset.Now;
        var hourStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var cutoff = hourStart.ToUniversalTime();

        var entries = data.EntryLog.Where(e => e.Timestamp >= cutoff);

        return AggregateEntryLog(entries);
    }

    private StatisticsSummary BuildTimePeriodStatistics(TimePeriod period)
    {
        var now = DateTimeOffset.Now;
        var cutoff = period switch
        {
            TimePeriod.Today => new DateTimeOffset(now.Date, now.Offset).ToUniversalTime(),
            TimePeriod.Week => StartOfWeek(now).ToUniversalTime(),
            TimePeriod.Month => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).ToUniversalTime(),
            _ => DateTimeOffset.MinValue
        };

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var filtered = new List<EntryLogItem>();

        foreach (var data in _projectData.Values)
        {
            foreach (var logEntry in data.EntryLog)
            {
                if (logEntry.Timestamp < cutoff)
                    continue;

                // Deduplicate by uuid+requestId across projects (TOKS-04)
                if (!string.IsNullOrEmpty(logEntry.DeduplicationKey)
                    && !seenIds.Add(logEntry.DeduplicationKey))
                {
                    continue;
                }

                filtered.Add(logEntry);
            }
        }

        return AggregateEntryLog(filtered);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return new DateTimeOffset(date.Date.AddDays(-diff), date.Offset);
    }

    private const long TierBreakpointTokens = 200_000;

    private StatisticsSummary AggregateEntryLog(IEnumerable<EntryLogItem> entries)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        long cacheCreation = 0;
        long cacheRead = 0;
        decimal totalCost = 0m;
        bool hasEstimated = false;
        var modelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cumulative input tracker per model for tiered pricing (matches macOS reference)
        var cumulativeInputByModel = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var logEntry in entries)
        {
            inputTokens += logEntry.InputTokens;
            outputTokens += logEntry.OutputTokens;
            cacheCreation += logEntry.CacheCreationTokens;
            cacheRead += logEntry.CacheReadTokens;

            if (logEntry.ModelName is not null)
                modelSet.Add(logEntry.ModelName);

            if (logEntry.CostUsd is > 0m)
            {
                totalCost += logEntry.CostUsd.Value;
            }
            else
            {
                var modelKey = logEntry.ModelName ?? "unknown";
                var pricing = logEntry.ModelName is not null
                    ? _pricingService.GetPrice(logEntry.ModelName)
                    : null;

                if (pricing is not null)
                {
                    // Track cumulative input for tiered pricing
                    cumulativeInputByModel.TryGetValue(modelKey, out var cumulativeBefore);
                    var entryInput = logEntry.InputTokens + logEntry.CacheCreationTokens;
                    cumulativeInputByModel[modelKey] = cumulativeBefore + entryInput;

                    var useExtended = cumulativeBefore >= TierBreakpointTokens;

                    var inputPrice = useExtended && pricing.InputCostAbove200k.HasValue
                        ? pricing.InputCostAbove200k.Value
                        : pricing.InputCostPerToken;
                    var outputPrice = pricing.OutputCostPerToken;
                    var cacheCreatePrice = useExtended && pricing.CacheCreationCostAbove200k.HasValue
                        ? pricing.CacheCreationCostAbove200k.Value
                        : pricing.CacheCreationCost ?? 0.0;
                    var cacheReadPrice = useExtended && pricing.CacheReadCostAbove200k.HasValue
                        ? pricing.CacheReadCostAbove200k.Value
                        : pricing.CacheReadCost ?? 0.0;

                    var entryCost = (logEntry.InputTokens * inputPrice)
                                  + (logEntry.OutputTokens * outputPrice)
                                  + (logEntry.CacheCreationTokens * cacheCreatePrice)
                                  + (logEntry.CacheReadTokens * cacheReadPrice);
                    totalCost += (decimal)entryCost;
                }
                else
                {
                    hasEstimated = true;
                }
            }
        }

        return new StatisticsSummary
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheCreationTokens = cacheCreation,
            CacheReadTokens = cacheRead,
            TotalCostUsd = totalCost,
            HasEstimatedCosts = hasEstimated,
            Models = modelSet.ToList()
        };
    }

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
    /// Reads all lines from a JSONL file. Used for initial session discovery
    /// where we need the complete file content for accurate statistics.
    /// </summary>
    private static List<string> ReadAllLines(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }
        return lines;
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

        lock (_sessionsLock)
        {
            foreach (var projectDir in Directory.GetDirectories(_projectsDirectory))
            {
                var projectDirName = Path.GetFileName(projectDir);
                var jsonlFiles = Directory.GetFiles(projectDir, JsonlFilePattern)
                    .Where(f => !IsSubagentFile(f))
                    .ToArray();

                if (jsonlFiles.Length == 0)
                    continue;

                if (!_projectData.TryGetValue(projectDirName, out var data))
                {
                    data = new ProjectData { ProjectDirName = projectDirName };
                    _projectData[projectDirName] = data;
                }

                foreach (var file in jsonlFiles)
                {
                    // Always do a full read on startup to rebuild _projectData from scratch.
                    // Cache positions are only useful for live file-watcher incremental updates.
                    ParseFileIntoProject(file, data, forceFullRead: true);

                    // Track the newest session file for subagent discovery
                    var modTime = File.GetLastWriteTimeUtc(file);
                    if (modTime > data.NewestSessionModTime)
                    {
                        data.NewestSessionModTime = new DateTimeOffset(modTime, TimeSpan.Zero);
                        data.NewestSessionFile = file;
                    }
                }
            }

            RebuildSessionsList();
        }
    }

    private void ParseFileIntoProject(string filePath, ProjectData data, bool forceFullRead = false)
    {
        _filePositions.TryGetValue(filePath, out var marker);
        var isIncremental = !forceFullRead && marker is not null;
        IEnumerable<string> lines;
        long newPosition;

        if (isIncremental && marker is not null)
        {
            var (incrementalLines, pos) = ReadIncrementalLines(filePath, marker.LastReadPosition);
            lines = incrementalLines;
            newPosition = pos;
        }
        else if (forceFullRead)
        {
            var allLines = ReadAllLines(filePath);
            lines = allLines;
            newPosition = new FileInfo(filePath).Length;
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

        // Populate project cwd from first entry if not yet set
        var firstEntry = entries[0];
        if (string.IsNullOrEmpty(data.Cwd))
            data.Cwd = firstEntry.Cwd;

        foreach (var entry in entries)
            ApplyEntryToProjectData(entry, data, filePath);

        UpdateFilePosition(filePath, newPosition);
    }

    private static void ApplyEntryToProjectData(JsonlEntry entry, ProjectData data, string sourceFile = "")
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

        var inputTokens = usage.InputTokens ?? 0;
        var outputTokens = usage.OutputTokens ?? 0;
        var cacheCreation = usage.CacheCreationInputTokens ?? 0;
        var cacheRead = usage.CacheReadInputTokens ?? 0;

        data.TotalInputTokens += inputTokens;
        data.TotalOutputTokens += outputTokens;
        data.TotalCacheCreationTokens += cacheCreation;
        data.TotalCacheReadTokens += cacheRead;

        var model = entry.Message?.Model;
        if (!string.IsNullOrEmpty(model))
            data.ModelName = model;

        data.EntryLog.Add(new EntryLogItem
        {
            Timestamp = entry.Timestamp ?? DateTimeOffset.MinValue,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheCreationTokens = cacheCreation,
            CacheReadTokens = cacheRead,
            CostUsd = entry.CostUsd,
            ModelName = model,
            DeduplicationKey = BuildDeduplicationKey(entry),
            SourceFile = sourceFile
        });
    }

    private static bool IsRelevantAssistantEntry(JsonlEntry entry) =>
        string.Equals(entry.Type, "assistant", StringComparison.OrdinalIgnoreCase)
        && !entry.IsSidechain;

    private static string BuildDeduplicationKey(JsonlEntry entry) =>
        entry.UniqueHash ?? string.Empty;

    private static bool IsSubagentFile(string filePath) =>
        filePath.Contains(Path.DirectorySeparatorChar + SubagentsDirectoryName + Path.DirectorySeparatorChar)
        || filePath.Contains('/' + SubagentsDirectoryName + '/');


    private List<string> FindSubagentFilesForNewestSession(ProjectData data)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(data.NewestSessionFile))
            return result;

        // Primary: {sessionUUID}/subagents/agent-*.jsonl
        var sessionDir = Path.ChangeExtension(data.NewestSessionFile, null);
        var subagentDir = Path.Combine(sessionDir, SubagentsDirectoryName);
        if (Directory.Exists(subagentDir))
            result.AddRange(Directory.GetFiles(subagentDir, AgentFilePattern));

        // Fallback: project dir level agent files
        if (result.Count == 0)
        {
            var projectDir = Path.GetDirectoryName(data.NewestSessionFile);
            if (projectDir != null)
            {
                var projectSubagentDir = Path.Combine(projectDir, SubagentsDirectoryName);
                if (Directory.Exists(projectSubagentDir))
                    result.AddRange(Directory.GetFiles(projectSubagentDir, AgentFilePattern));
            }
        }

        return result;
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
        _sessions = _projectData
            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
            .Select(kvp =>
            {
                var displayName = SessionNameHelper.GetDisplayName(kvp.Value.Cwd, kvp.Key);
                if (displayName is null)
                    return null;

                return new SessionInfo
                {
                    Id = kvp.Key,
                    Cwd = kvp.Value.Cwd ?? string.Empty,
                    DisplayName = displayName,
                    LastActivity = kvp.Value.LastActivity,
                    ModelName = kvp.Value.ModelName
                };
            })
            .Where(s => s is not null)
            .OrderByDescending(s => s!.LastActivity)
            .ToList()!;
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
                var projectDir = Path.GetDirectoryName(filePath);
                var projectDirName = projectDir != null ? Path.GetFileName(projectDir) : null;

                if (string.IsNullOrEmpty(projectDirName))
                    return;

                if (!_projectData.TryGetValue(projectDirName, out var data))
                {
                    data = new ProjectData { ProjectDirName = projectDirName };
                    _projectData[projectDirName] = data;
                }

                ParseFileIntoProject(filePath, data);

                var modTime = File.GetLastWriteTimeUtc(filePath);
                if (modTime > data.NewestSessionModTime)
                {
                    data.NewestSessionModTime = new DateTimeOffset(modTime, TimeSpan.Zero);
                    data.NewestSessionFile = filePath;
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

    // -------------------------------------------------------------------------
    // Null pricing service (used when no pricing service is injected)
    // -------------------------------------------------------------------------

    private sealed class NullPricingService : IPricingService
    {
        public ModelPricing? GetPrice(string modelName) => null;
        public PricingSource Source => PricingSource.Unknown;
        public DateTimeOffset? LastFetch => null;
        public Task EnsurePricesLoadedAsync() => Task.CompletedTask;
    }
}

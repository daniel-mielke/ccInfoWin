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
    private const int DebounceMilliseconds = 2_000;
    private const string CacheFileName = "jsonl-cache.json";
    private const string SubagentsDirectoryName = "subagents";
    private const string AgentFilePattern = "agent-*.jsonl";
    private const string JsonlFilePattern = "*.jsonl";
    private const long TierBreakpointTokens = 200_000;
    private const int MaxWatcherRestarts = 5;
    private const long MaxCacheFileSizeBytes = 10 * 1_048_576; // 10 MB
    private const int SubagentActivityWindowSeconds = 30; // Only show subagents active within this window

    private static readonly JsonSerializerOptions CacheSerializerOptions = new() { WriteIndented = false };

    // -------------------------------------------------------------------------
    // Internal per-project aggregation (keyed by project directory name)
    // -------------------------------------------------------------------------

    private sealed class ProjectData
    {
        public string ProjectDirName { get; set; } = string.Empty;
        public string? Cwd { get; set; }
        public string? ModelName { get; set; }
        public DateTimeOffset LastActivity { get; set; }

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
    private readonly ISettingsService? _settingsService;
    private readonly Lock _sessionsLock = new();
    private readonly object _debounceLock = new();
    private readonly HashSet<string> _pendingChangedFiles = new(StringComparer.OrdinalIgnoreCase);

    private List<SessionInfo> _sessions = [];
    private Dictionary<string, ProjectData> _projectData = [];
    private Dictionary<string, FilePositionMarker> _filePositions = [];
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private int _isScanning; // 0 = idle, 1 = scanning; use Interlocked for atomic CAS
    private int _watcherRestartCount;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <param name="projectsDirectoryOverride">Override for test isolation. Defaults to %USERPROFILE%\.claude\projects.</param>
    /// <param name="cacheDirectoryOverride">Override for test isolation. Defaults to %LOCALAPPDATA%\CCInfoWindows.</param>
    /// <param name="pricingService">Pricing service for cost calculation. Required for GetStatistics.</param>
    /// <param name="settingsService">Settings service for reading user preferences. Optional; defaults to null (uses DefaultContextLimit).</param>
    public JsonlService(
        string? projectsDirectoryOverride = null,
        string? cacheDirectoryOverride = null,
        IPricingService? pricingService = null,
        ISettingsService? settingsService = null)
    {
        _projectsDirectory = projectsDirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        _cacheDirectory = cacheDirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCInfoWindows");

        _pricingService = pricingService ?? new NullPricingService();
        _settingsService = settingsService;
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

    public bool IsScanning => Interlocked.CompareExchange(ref _isScanning, 0, 0) == 1;

    public event EventHandler? DataUpdated;

    public ContextWindowData GetContextWindow(string projectDirName)
    {
        lock (_sessionsLock)
        {
            if (!_projectData.TryGetValue(projectDirName, out var data))
                return ContextWindowData.Empty;

            if (string.IsNullOrEmpty(data.NewestSessionFile))
                return ContextWindowData.Empty;

            var entry = ReadLastAssistantEntryFromFile(data.NewestSessionFile);
            if (entry is null)
                return ContextWindowData.Empty;

            var totalTokens = ComputeContextTokens(entry);
            var modelName = ResolveModelName(data.NewestSessionFile, entry);
            var sonnetContextSize = _settingsService?.LoadSettings().SonnetContextSize
                ?? ModelContextLimits.DefaultContextLimit;
            var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize);
            var subagentFiles = FindSubagentFilesForNewestSession(data);
            var subagents = BuildSubagentContext(subagentFiles, sonnetContextSize);

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

            var sonnetContextSize = _settingsService?.LoadSettings().SonnetContextSize
                ?? ModelContextLimits.DefaultContextLimit;
            var subagentFiles = FindSubagentFilesForNewestSession(data);
            var subagents = BuildSubagentContext(subagentFiles, sonnetContextSize);
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
        // Atomic CAS: only one scan at a time (prevents double-init race)
        if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
            return;

        try
        {
            LoadCache();
            RaiseDataUpdated();

            await Task.Run(DiscoverSessions);

            lock (_sessionsLock)
            {
                SaveCache();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isScanning, 0);
            RaiseDataUpdated();
        }

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

    private StatisticsSummary AggregateEntryLog(IEnumerable<EntryLogItem> entries)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        long cacheCreation = 0;
        long cacheRead = 0;
        decimal totalCost = 0m;
        bool hasEstimated = false;
        var modelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                var (cost, estimated) = CalculateEntryCost(logEntry, cumulativeInputByModel);
                totalCost += cost;
                hasEstimated |= estimated;
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

    private (decimal Cost, bool Estimated) CalculateEntryCost(
        EntryLogItem entry,
        Dictionary<string, long> cumulativeInputByModel)
    {
        var pricing = entry.ModelName is not null
            ? _pricingService.GetPrice(entry.ModelName)
            : null;

        if (pricing is null)
            return (0m, true);

        var modelKey = entry.ModelName!;
        cumulativeInputByModel.TryGetValue(modelKey, out var cumulativeBefore);
        var entryInput = entry.InputTokens + entry.CacheCreationTokens;
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

        var cost = (entry.InputTokens * inputPrice)
                 + (entry.OutputTokens * outputPrice)
                 + (entry.CacheCreationTokens * cacheCreatePrice)
                 + (entry.CacheReadTokens * cacheReadPrice);

        return ((decimal)cost, false);
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
    /// Returns lines and the stream end position for consistent file position tracking.
    /// </summary>
    private static (List<string> Lines, long EndPosition) ReadAllLines(string filePath)
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
        return (lines, stream.Length);
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
            catch (JsonException)
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
            var (allLines, endPos) = ReadAllLines(filePath);
            lines = allLines;
            newPosition = endPos;
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
            DeduplicationKey = deduplicationKey,
            SourceFile = sourceFile
        });
    }

    private static bool IsRelevantAssistantEntry(JsonlEntry entry) =>
        string.Equals(entry.Type, "assistant", StringComparison.OrdinalIgnoreCase)
        && !entry.IsSidechain;

    private static bool IsSyntheticModel(string? modelName) =>
        string.Equals(modelName, "<synthetic>", StringComparison.OrdinalIgnoreCase)
        || string.Equals(modelName, "synthetic", StringComparison.OrdinalIgnoreCase);

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

    private static IReadOnlyList<SubagentContextData> BuildSubagentContext(List<string> subagentFiles, long sonnetContextSize)
    {
        var result = new List<SubagentContextData>();
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-SubagentActivityWindowSeconds);

        foreach (var file in subagentFiles)
        {
            try
            {
                var lines = ReadTailLines(file);
                // Subagent files have isSidechain=true on all entries by design —
                // do not apply the sidechain filter here.
                var entries = ParseJsonlEntries(lines)
                    .Where(e => string.Equals(e.Type, "assistant", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (entries.Count == 0)
                    continue;

                var lastEntry = entries[^1];
                var lastActivity = lastEntry.Timestamp ?? DateTimeOffset.MinValue;

                if (lastActivity < cutoff)
                    continue;

                var totalTokens = ComputeContextTokens(lastEntry);
                var modelName = lastEntry.Message?.Model;
                var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize);
                var agentId = ExtractAgentId(file);

                result.Add(new SubagentContextData
                {
                    AgentId = agentId,
                    TotalTokens = totalTokens,
                    MaxTokens = maxTokens,
                    ModelName = modelName,
                    LastActivity = lastActivity
                });
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[JsonlService] Failed to parse subagent file {file}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"[JsonlService] Access denied for subagent file {file}: {ex.Message}");
            }
        }

        return result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList();
    }

    private static string ExtractAgentId(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.StartsWith("agent-", StringComparison.OrdinalIgnoreCase)
            ? fileName["agent-".Length..]
            : fileName;
    }

    private static JsonlEntry? ReadLastAssistantEntryFromFile(string filePath)
    {
        var lines = ReadTailLines(filePath);
        return ParseJsonlEntries(lines)
            .Where(IsRelevantAssistantEntry)
            .LastOrDefault();
    }

    private static string? ResolveModelName(string filePath, JsonlEntry lastEntry)
    {
        var candidate = lastEntry.Message?.Model;
        if (!IsSyntheticModel(candidate))
            return candidate;

        var lines = ReadTailLines(filePath);
        return ParseJsonlEntries(lines)
            .Where(IsRelevantAssistantEntry)
            .Select(e => e.Message?.Model)
            .LastOrDefault(m => !IsSyntheticModel(m));
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

    private static bool IsValidProjectDirectory(string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return false;
        if (!Path.IsPathRooted(cwd))
            return false;
        // UNC paths (\\server\share or //server/share) cause Directory.Exists to hang
        // when the server is unreachable — short-circuit before filesystem call
        if (cwd.StartsWith(@"\\", StringComparison.Ordinal) || cwd.StartsWith("//", StringComparison.Ordinal))
            return false;
        return Directory.Exists(cwd);
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
            .Where(s => s is not null && IsValidProjectDirectory(s.Cwd))
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
            _pendingChangedFiles.Add(e.FullPath);

            if (_debounceTimer is null)
            {
                _debounceTimer = new System.Threading.Timer(
                    _ => ProcessPendingFileChanges(),
                    state: null,
                    dueTime: DebounceMilliseconds,
                    period: System.Threading.Timeout.Infinite);
            }
            else
            {
                try
                {
                    _debounceTimer.Change(DebounceMilliseconds, System.Threading.Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // Timer was disposed between null-check and Change() — safe to ignore
                }
            }
        }
    }

    private void ProcessPendingFileChanges()
    {
        // Skip if initial scan is still running — avoid double-processing
        if (Interlocked.CompareExchange(ref _isScanning, 0, 0) == 1)
            return;

        List<string> filesToProcess;
        lock (_debounceLock)
        {
            filesToProcess = [.. _pendingChangedFiles];
            _pendingChangedFiles.Clear();
        }

        try
        {
            lock (_sessionsLock)
            {
                foreach (var filePath in filesToProcess)
                {
                    ProcessSingleFile(filePath);
                }

                RebuildSessionsList();
                SaveCache();
            }

            RaiseDataUpdated();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JsonlService] Error processing pending file changes: {ex.Message}");
        }
    }

    private void ProcessSingleFile(string filePath)
    {
        if (!IsPathWithinProjectsDirectory(filePath))
            return;

        var isSubagent = IsSubagentFile(filePath);

        // Subagent files live at: {projectDir}/{sessionUUID}/subagents/agent-*.jsonl
        // Non-subagent session files live at: {projectDir}/{sessionUUID}.jsonl
        // We need to walk up to the actual project directory in both cases.
        string? projectDirName;
        if (isSubagent)
        {
            // Walk up: subagents/ -> sessionDir -> projectDir
            var subagentsDir = Path.GetDirectoryName(filePath);
            var sessionDir = subagentsDir != null ? Path.GetDirectoryName(subagentsDir) : null;
            projectDirName = sessionDir != null ? Path.GetFileName(sessionDir) : null;
        }
        else
        {
            var projectDir = Path.GetDirectoryName(filePath);
            projectDirName = projectDir != null ? Path.GetFileName(projectDir) : null;
        }

        if (string.IsNullOrEmpty(projectDirName))
            return;

        if (!_projectData.TryGetValue(projectDirName, out var data))
        {
            data = new ProjectData { ProjectDirName = projectDirName };
            _projectData[projectDirName] = data;
        }

        if (!isSubagent)
        {
            ParseFileIntoProject(filePath, data);

            var modTime = File.GetLastWriteTimeUtc(filePath);
            if (modTime > data.NewestSessionModTime)
            {
                data.NewestSessionModTime = new DateTimeOffset(modTime, TimeSpan.Zero);
                data.NewestSessionFile = filePath;
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"[JsonlService] Watcher error: {e.GetException()?.Message}");

        if (Interlocked.CompareExchange(ref _watcherRestartCount, 0, 0) >= MaxWatcherRestarts)
        {
            Debug.WriteLine("[JsonlService] Max watcher restarts reached — giving up.");
            return;
        }

        Interlocked.Increment(ref _watcherRestartCount);
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
            var fileInfo = new FileInfo(cacheFile);
            if (fileInfo.Length > MaxCacheFileSizeBytes)
            {
                Debug.WriteLine($"[JsonlService] Cache file exceeds {MaxCacheFileSizeBytes} bytes — ignoring.");
                return;
            }

            var json = File.ReadAllText(cacheFile);
            var cache = JsonSerializer.Deserialize<JsonlCache>(json);

            if (cache is null)
                return;

            var positions = cache.FilePositions ?? [];

            // Validate deserialized values — reject negative positions
            foreach (var (key, marker) in positions)
            {
                if (marker.LastReadPosition < 0)
                {
                    Debug.WriteLine($"[JsonlService] Invalid cache position for {key} — discarding cache.");
                    return;
                }
            }

            _filePositions = positions;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[JsonlService] Corrupt cache file: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[JsonlService] Failed to load cache: {ex.Message}");
        }
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

            var json = JsonSerializer.Serialize(cache, CacheSerializerOptions);
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
    // Path validation
    // -------------------------------------------------------------------------

    private bool IsPathWithinProjectsDirectory(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        var root = Path.GetFullPath(_projectsDirectory);
        return normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase);
    }

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

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

    // AppLog call-site tags — kept as constants so the log stays greppable when methods are renamed.
    private const string LoadCacheSource = "JsonlService.LoadCache";
    private const string SaveCacheSource = "JsonlService.SaveCache";
    private const string ParseFileSource = "JsonlService.ParseFileIntoProject";
    private const string SubagentContextSource = "JsonlService.BuildSubagentContext";
    private const string ContextWindowSource = "JsonlService.GetContextWindow";
    private const string FileChangeSource = "JsonlService.ProcessPendingFileChanges";
    private const string FileDeletedSource = "JsonlService.OnFileDeleted";
    private const string WatcherErrorSource = "JsonlService.OnWatcherError";

    // -------------------------------------------------------------------------
    // Internal per-project aggregation (keyed by project directory name)
    // -------------------------------------------------------------------------

    private sealed class ProjectData
    {
        public string ProjectDirName { get; set; } = string.Empty;
        public string? Cwd { get; set; }
        public string? ModelName { get; set; }
        public DateTimeOffset LastActivity { get; set; }

        public string? NewestSessionFile { get; set; }
        public DateTimeOffset NewestSessionModTime { get; set; }

        /// <summary>
        /// Maps a deduplication key to the index of its entry in <see cref="EntryLog"/>, so a
        /// repeated line for the same assistant message supersedes the earlier one instead of
        /// adding a second contribution. Indices stay valid because entries are never removed.
        /// </summary>
        public Dictionary<string, int> EntryIndexByKey { get; } = new(StringComparer.Ordinal);

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
        public decimal? CostUsd { get; init; }
        public string? ModelName { get; init; }
        public string DeduplicationKey { get; init; } = string.Empty;
    }

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly string _projectsDirectory;
    private readonly string _cacheDirectory;
    private readonly IPricingService _pricingService;
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
    /// <param name="pricingService">Pricing service for cost calculation and context-window resolution.</param>
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

            // The pointer is a snapshot: the file it names can be deleted, renamed or locked at
            // any time, and every caller is a UI refresh tick that must degrade rather than throw.
            try
            {
                return BuildContextWindow(data);
            }
            catch (IOException ex)
            {
                return HandleContextWindowFailure(data, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return HandleContextWindowFailure(data, ex);
            }
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

                // Cross-project guard (TOKS-04). Within one project the entry index already holds
                // a single entry per message.id+requestId; this catches the same message surfacing
                // under two project directories, where the two indexes cannot see each other.
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
        // Output was the one tier that stayed on the base price even though the above-200k value
        // is parsed. For Opus that is a 33% surcharge (7.5e-05 -> 1e-04), and output dominates in
        // long sessions, so cost above 200k input was systematically understated.
        var outputPrice = useExtended && pricing.OutputCostAbove200k.HasValue
            ? pricing.OutputCostAbove200k.Value
            : pricing.OutputCostPerToken;
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
        // DROPDOWN-06: use stream.Position (bytes consumed by reader) not stream.Length.
        // stream.Length reflects the file size at the moment of the call, which may have grown
        // while we were reading. stream.Position is the byte offset after the last ReadLine,
        // so a subsequent incremental read correctly picks up lines written after this drain.
        return (lines, stream.Position);
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

        // DROPDOWN-06: same as ReadAllLines -- use stream.Position not stream.Length.
        return (lines, stream.Position);
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

        foreach (var entry in entries)
        {
            // DROPDOWN-02: resolve Cwd from the FIRST non-empty cwd across ALL parsed entries.
            // Tail-window reads frequently land on entries that omit the cwd field; iterating all
            // entries instead of relying on entries[0] stabilises hydration across cold starts.
            if (string.IsNullOrEmpty(data.Cwd) && !string.IsNullOrEmpty(entry.Cwd))
                data.Cwd = entry.Cwd;

            ApplyEntryToProjectData(entry, data);
        }

        // DROPDOWN-02 diagnostic: when no entry in this file carries a cwd field, log the
        // surrogate that GetDisplayName will derive from the encoded project directory name.
        // data.Cwd intentionally stays empty here so the DROPDOWN-03 filter (IsNullOrEmpty path)
        // keeps the session visible; DisplayName is resolved by RebuildSessionsList via
        // SessionNameHelper.GetDisplayName(cwd: null, fallbackDirName: projectDirName).
        if (string.IsNullOrEmpty(data.Cwd) && !string.IsNullOrEmpty(data.ProjectDirName))
        {
            var decoded = SessionNameHelper.DecodeProjectDirectory(data.ProjectDirName);
            AppLog.Write(
                ParseFileSource,
                $"No cwd in '{data.ProjectDirName}'; display surrogate: '{decoded ?? "(none)"}'");
        }

        UpdateFilePosition(filePath, newPosition);
    }

    private static void ApplyEntryToProjectData(JsonlEntry entry, ProjectData data)
    {
        if (entry.Timestamp.HasValue && entry.Timestamp > data.LastActivity)
            data.LastActivity = entry.Timestamp.Value;

        if (!IsRelevantAssistantEntry(entry))
            return;

        var usage = entry.Message?.Usage;
        if (usage is null)
            return;

        var logItem = BuildEntryLogItem(entry, usage);

        if (logItem.DeduplicationKey.Length > 0
            && data.EntryIndexByKey.TryGetValue(logItem.DeduplicationKey, out var knownIndex))
        {
            SupersedeEntry(data, knownIndex, logItem);
            return;
        }

        AppendEntry(data, logItem);
    }

    private static EntryLogItem BuildEntryLogItem(JsonlEntry entry, JsonlUsage usage) =>
        new()
        {
            Timestamp = entry.Timestamp ?? DateTimeOffset.MinValue,
            InputTokens = usage.InputTokens ?? 0,
            OutputTokens = usage.OutputTokens ?? 0,
            CacheCreationTokens = usage.CacheCreationInputTokens ?? 0,
            CacheReadTokens = usage.CacheReadInputTokens ?? 0,
            CostUsd = entry.CostUsd,
            ModelName = entry.Message?.Model,
            DeduplicationKey = BuildDeduplicationKey(entry)
        };

    private static void AppendEntry(ProjectData data, EntryLogItem item)
    {
        if (item.DeduplicationKey.Length > 0)
            data.EntryIndexByKey[item.DeduplicationKey] = data.EntryLog.Count;

        data.EntryLog.Add(item);
        ApplyModelName(data, item.ModelName);
    }

    /// <summary>
    /// Replaces an already-recorded entry with a later line carrying the same identity.
    /// Claude Code writes one JSONL line per streamed content block of a single assistant
    /// message: every line repeats the identical input/cache figures while only the final line
    /// carries the completed output_tokens. Superseding (rather than skipping the repeat) keeps
    /// exactly one contribution per message AND keeps the authoritative one, and makes
    /// re-reading lines that were already parsed idempotent instead of additive.
    /// <see cref="ProjectData.EntryLog"/> is the only place a contribution is recorded, so
    /// replacing the element is the whole update — there is no parallel running total to correct.
    /// </summary>
    private static void SupersedeEntry(ProjectData data, int index, EntryLogItem replacement)
    {
        data.EntryLog[index] = replacement;
        ApplyModelName(data, replacement.ModelName);
    }

    private static void ApplyModelName(ProjectData data, string? modelName)
    {
        if (!string.IsNullOrEmpty(modelName))
            data.ModelName = modelName;
    }

    private static bool IsRelevantAssistantEntry(JsonlEntry entry) =>
        string.Equals(entry.Type, "assistant", StringComparison.OrdinalIgnoreCase)
        && !entry.IsSidechain;

    private static bool IsSyntheticModel(string? modelName) =>
        string.Equals(modelName, "<synthetic>", StringComparison.OrdinalIgnoreCase)
        || string.Equals(modelName, "synthetic", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Identity of one assistant message (TOKS-04). message.id is written on every usage-bearing
    /// assistant line and is repeated across the lines that belong to one streamed response;
    /// requestId narrows it to the single API call that produced it. uuid is only a fallback: it
    /// is unique per LINE, so it can never collapse a multi-line message — it merely keeps a line
    /// that carries no message.id from being counted twice when the same bytes are re-read.
    /// </summary>
    private static string BuildDeduplicationKey(JsonlEntry entry) =>
        entry.Message?.Id is { Length: > 0 } messageId
            ? $"{messageId}|{entry.RequestId}"
            : entry.Uuid ?? string.Empty;

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

    private static IReadOnlyList<SubagentContextData> BuildSubagentContext(List<string> subagentFiles, IPricingService pricingService)
    {
        var result = new List<SubagentContextData>();
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-SubagentActivityWindowSeconds);

        foreach (var file in subagentFiles)
        {
            try
            {
                // macOS parity (findActiveAgents / contentModificationDate): every tool-result
                // write bumps NTFS LastWriteTime, so long tool-calls keep the agent visible
                // even when the last assistant entry is older than the cutoff. UTC-only
                // arithmetic — Kind=Utc guaranteed by GetLastWriteTimeUtc, explicit zero
                // offset makes the requirement obvious at the comparison site.
                var mtimeUtc = File.GetLastWriteTimeUtc(file);
                var lastActivity = new DateTimeOffset(mtimeUtc, TimeSpan.Zero);

                // Short-circuit BEFORE ReadTailLines: stale files are never opened.
                if (lastActivity < cutoff)
                    continue;

                var lines = ReadTailLines(file);
                // Subagent files have isSidechain=true on all entries by design —
                // do not apply the sidechain filter here.
                var entries = ParseJsonlEntries(lines)
                    .Where(e => string.Equals(e.Type, "assistant", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Guard preserved: fresh mtime but no assistant entries yet
                // (agent just started — only user / tool-result lines). Without an
                // assistant entry we have no model + token data to display.
                if (entries.Count == 0)
                    continue;

                var lastEntry = entries[^1];
                var totalTokens = ComputeContextTokens(lastEntry);
                var modelName = lastEntry.Message?.Model;
                var maxTokens = ModelContextLimits.GetMaxContextTokens(
                    modelName, pricingService.GetPrice, observedTokens: totalTokens);
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
                AppLog.Write(SubagentContextSource, ex, $"Failed to parse subagent file {file}.");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Write(SubagentContextSource, ex, $"Access denied for subagent file {file}.");
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

    private ContextWindowData BuildContextWindow(ProjectData data)
    {
        var sessionFile = data.NewestSessionFile!;

        var entry = ReadLastAssistantEntryFromFile(sessionFile);
        if (entry is null)
            return ContextWindowData.Empty;

        var totalTokens = ComputeContextTokens(entry);
        var modelName = ResolveModelName(sessionFile, entry);
        var maxTokens = ModelContextLimits.GetMaxContextTokens(
            modelName, _pricingService.GetPrice, observedTokens: totalTokens);
        var subagentFiles = FindSubagentFilesForNewestSession(data);
        var subagents = BuildSubagentContext(subagentFiles, _pricingService);

        return new ContextWindowData
        {
            TotalTokens = totalTokens,
            MaxTokens = maxTokens,
            ModelName = modelName,
            ShouldWarnAutocompact = ModelContextLimits.ShouldWarnAutocompact(totalTokens, maxTokens),
            Subagents = subagents
        };
    }

    /// <summary>
    /// A pointer to a file that no longer exists can never succeed, so it is dropped and the dead
    /// read is not retried on every subsequent tick; the next write anywhere in the project
    /// re-establishes it via <see cref="ProcessSingleFile"/>. A file that merely failed to open
    /// keeps its pointer, so a transient sharing conflict does not blank the context bar.
    /// </summary>
    private static ContextWindowData HandleContextWindowFailure(ProjectData data, Exception ex)
    {
        var sessionFile = data.NewestSessionFile;
        AppLog.Write(ContextWindowSource, ex, $"Failed to read newest session file '{sessionFile}'.");

        if (sessionFile is not null && !File.Exists(sessionFile))
            ClearNewestSessionPointer(data);

        return ContextWindowData.Empty;
    }

    private static void ClearNewestSessionPointer(ProjectData data)
    {
        data.NewestSessionFile = null;
        // Reset the high-water mark too, otherwise no remaining file older than the vanished one
        // could ever claim the pointer again.
        data.NewestSessionModTime = default;
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

    /// <summary>
    /// True when <paramref name="cwd"/> names a project directory that still exists. Called from
    /// <see cref="RebuildSessionsList"/> while <c>_sessionsLock</c> is held, so every case that
    /// cannot be answered without a blocking filesystem round-trip is rejected up front.
    /// </summary>
    private static bool IsValidProjectDirectory(string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return false;
        // A relative cwd would resolve against this process's working directory, not the one the
        // JSONL was written from, so Directory.Exists could not answer the question being asked.
        if (!Path.IsPathRooted(cwd))
            return false;
        // UNC paths (\\server\share or //server/share) make Directory.Exists block on SMB name
        // resolution for tens of seconds when the host is unreachable — short-circuit before the
        // filesystem call rather than stall every locked read behind it.
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
            // DROPDOWN-03: keep when Cwd is empty (DisplayName already resolved via fallback in
            // ParseFileIntoProject) OR when the Cwd path is a project directory that still exists.
            // Drop only when Cwd is non-empty AND IsValidProjectDirectory rejects it.
            .Where(s => s is not null && (string.IsNullOrEmpty(s.Cwd) || IsValidProjectDirectory(s.Cwd)))
            .OrderByDescending(s => s!.LastActivity)
            .ToList()!;
    }

    // -------------------------------------------------------------------------
    // Test seams (internal — not part of IJsonlService; used by unit tests only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the number of deduplicated assistant entries recorded for the given project.
    /// Used by JsonlServiceColdStartTests to verify incremental read counts without
    /// coupling tests to token-sum arithmetic.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal int GetEntryCountForProject(string projectDirName)
    {
        lock (_sessionsLock)
        {
            return _projectData.TryGetValue(projectDirName, out var data) ? data.EntryLog.Count : 0;
        }
    }

    /// <summary>
    /// Sums the deduplicated input and output tokens recorded for the given project straight from
    /// <see cref="ProjectData.EntryLog"/>, the single source of truth for aggregation. Lets the
    /// deduplication and supersede tests assert token arithmetic without inheriting the
    /// wall-clock cutoffs that <see cref="GetStatistics"/> applies.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal (long InputTokens, long OutputTokens) GetTokenSummary(string projectDirName)
    {
        lock (_sessionsLock)
        {
            if (!_projectData.TryGetValue(projectDirName, out var data))
                return (0L, 0L);

            long inputTokens = 0;
            long outputTokens = 0;

            foreach (var item in data.EntryLog)
            {
                inputTokens += item.InputTokens;
                outputTokens += item.OutputTokens;
            }

            return (inputTokens, outputTokens);
        }
    }

    /// <summary>
    /// Triggers an incremental re-parse of the given files, exactly as the FileSystemWatcher
    /// would do after a debounce window — same per-file guard, same single rebuild afterwards.
    /// Used to simulate a second read pass after new lines have been appended to a JSONL file,
    /// without triggering a full forceFullRead scan that would re-parse already-counted entries.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal async Task ProcessFilesForTestAsync(IEnumerable<string> filePaths)
    {
        await Task.Run(() =>
        {
            lock (_sessionsLock)
            {
                foreach (var filePath in filePaths)
                    ProcessSingleFileGuarded(filePath);
                RebuildSessionsList();
            }
        });
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
        watcher.Deleted += OnFileDeleted;
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

    /// <summary>
    /// Drops every reference to a JSONL file the watcher reports as deleted: the cached read
    /// position, which a file recreated under the same name would otherwise resume from, and the
    /// per-project newest-file pointer, which would otherwise name a vanished path for the whole
    /// process lifetime. Deleting a directory reports the directory rather than each file it held,
    /// so <see cref="GetContextWindow"/> keeps its own guard for what this handler cannot see.
    /// </summary>
    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!IsPathWithinProjectsDirectory(e.FullPath))
                return;

            lock (_sessionsLock)
            {
                _filePositions.Remove(e.FullPath);

                foreach (var data in _projectData.Values)
                {
                    if (string.Equals(data.NewestSessionFile, e.FullPath, StringComparison.OrdinalIgnoreCase))
                        ClearNewestSessionPointer(data);
                }
            }

            RaiseDataUpdated();
        }
        catch (Exception ex)
        {
            // An exception escaping a FileSystemWatcher callback terminates the process.
            AppLog.Write(FileDeletedSource, ex, $"Failed to handle deletion of '{e.FullPath}'.");
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
                    ProcessSingleFileGuarded(filePath);
                }

                RebuildSessionsList();
                SaveCache();
            }

            RaiseDataUpdated();
        }
        catch (Exception ex)
        {
            AppLog.Write(FileChangeSource, ex, "Error processing pending file changes.");
        }
    }

    /// <summary>
    /// One unreadable file must not take the rest of the batch — nor the refresh that follows it —
    /// down with it, because the pending set was drained and cleared before any file was read and a
    /// skipped file gets no second chance in this pass. Its stored read position was never
    /// advanced, so the next write anywhere under the projects directory picks the lines up.
    /// </summary>
    private void ProcessSingleFileGuarded(string filePath)
    {
        try
        {
            ProcessSingleFile(filePath);
        }
        catch (Exception ex)
        {
            AppLog.Write(FileChangeSource, ex, $"Skipped '{filePath}'.");
        }
    }

    private void ProcessSingleFile(string filePath)
    {
        if (!IsPathWithinProjectsDirectory(filePath))
            return;

        // Subagent files ({projectDir}/{sessionUUID}/subagents/agent-*.jsonl) are deliberately not
        // registered here. Their content is read on demand by FindSubagentFilesForNewestSession,
        // which re-globs the directory on every GetContextWindow call, so the only effect of
        // walking up from one was a _projectData entry keyed on the session UUID — a phantom
        // session in the picker. The caller still refreshes and raises DataUpdated afterwards, so
        // the subagent bars keep tracking subagent writes.
        if (IsSubagentFile(filePath))
            return;

        // A watcher event can name a file that was deleted before the debounce window elapsed.
        // Returning here keeps the guarded catch below for the genuine race and, more importantly,
        // stops a vanished file from registering an empty ProjectData for its project.
        if (!File.Exists(filePath))
            return;

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
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var watcherException = e.GetException();
        if (watcherException is not null)
            AppLog.Write(WatcherErrorSource, watcherException, "Watcher error.");
        else
            AppLog.Write(WatcherErrorSource, "Watcher error with no exception attached.");

        if (Interlocked.CompareExchange(ref _watcherRestartCount, 0, 0) >= MaxWatcherRestarts)
        {
            AppLog.Write(WatcherErrorSource, "Max watcher restarts reached — giving up; live updates stop here.");
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
            AppLog.Write(WatcherErrorSource, ex, "Failed to restart watcher.");
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
                AppLog.Write(LoadCacheSource, $"Cache file exceeds {MaxCacheFileSizeBytes} bytes — ignoring.");
                return;
            }

            var json = File.ReadAllText(cacheFile);
            var cache = JsonSerializer.Deserialize<JsonlCache>(json);

            if (cache is null)
                return;

            // Read positions produced under different aggregation semantics would let an existing
            // installation resume from lines that were counted the old way. Dropping them costs
            // one full re-read and is the only way those numbers can self-correct.
            if (cache.SchemaVersion != JsonlCache.CurrentSchemaVersion)
            {
                AppLog.Write(
                    LoadCacheSource,
                    $"Cache schema {cache.SchemaVersion} != {JsonlCache.CurrentSchemaVersion} — discarding to force a full re-read.");
                return;
            }

            var positions = cache.FilePositions ?? [];

            // Validate deserialized values — reject negative positions
            foreach (var (key, marker) in positions)
            {
                if (marker.LastReadPosition < 0)
                {
                    AppLog.Write(LoadCacheSource, $"Invalid cache position for {key} — discarding cache.");
                    return;
                }
            }

            _filePositions = positions;
        }
        catch (JsonException ex)
        {
            AppLog.Write(LoadCacheSource, ex, "Corrupt cache file — discarding.");
        }
        catch (IOException ex)
        {
            AppLog.Write(LoadCacheSource, ex, "Failed to load cache.");
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
                SchemaVersion = JsonlCache.CurrentSchemaVersion,
                FilePositions = _filePositions
            };

            var json = JsonSerializer.Serialize(cache, CacheSerializerOptions);
            File.WriteAllText(CacheFilePath(), json);
        }
        catch (Exception ex)
        {
            AppLog.Write(SaveCacheSource, ex, "Failed to save cache.");
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

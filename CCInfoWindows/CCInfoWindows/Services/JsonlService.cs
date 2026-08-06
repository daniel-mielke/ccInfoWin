using System.Text;
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

    // StreamReader's own default. Named because the leaveOpen overload has no defaulted parameters.
    private const int LineReaderBufferBytes = 1024;

    private static readonly JsonSerializerOptions CacheSerializerOptions = new() { WriteIndented = false };

    // AppLog call-site tags — kept as constants so the log stays greppable when methods are renamed.
    private const string LoadCacheSource = "JsonlService.LoadCache";
    private const string SaveCacheSource = "JsonlService.SaveCache";
    private const string CwdDiagnosticSource = "JsonlService.LogMissingCwdSurrogate";
    private const string SubagentContextSource = "JsonlService.BuildSubagentContext";
    private const string ContextWindowSource = "JsonlService.GetContextWindow";
    private const string ScanSource = "JsonlService.ScanProjectsDirectory";
    private const string FileChangeSource = "JsonlService.ProcessPendingFileChanges";
    private const string FileDeletedSource = "JsonlService.OnFileDeleted";
    private const string WatcherErrorSource = "JsonlService.OnWatcherError";
    private const string InitializeSource = "JsonlService.InitializeAsync";

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
    /// The scalars <see cref="BuildSessionList"/> needs, copied out of the project graph so the
    /// display-name resolution and the <c>Directory.Exists</c> validity check behind it run with no
    /// lock held. <see cref="ProjectData.EntryLog"/> is deliberately not part of this: nothing about
    /// the picker depends on it, and copying it would be the expensive half.
    /// </summary>
    private readonly record struct SessionSeed(
        string ProjectDirName,
        string? Cwd,
        string? ModelName,
        DateTimeOffset LastActivity);

    /// <summary>
    /// One JSONL file's parsed content plus the stream position the next incremental read resumes
    /// from. Produced with no lock held; consumed by <see cref="ApplyFileSlice"/> under one.
    /// </summary>
    private sealed record FileSlice(List<JsonlEntry> Entries, long NewPosition);

    /// <summary>
    /// The complete result of a cold-start scan, built against a private graph so the scan's disk
    /// I/O never touches published state. Published in one step by <see cref="PublishScanResult"/>.
    /// </summary>
    private sealed record ScanResult(
        Dictionary<string, ProjectData> Projects,
        Dictionary<string, FilePositionMarker> FilePositions,
        IReadOnlyList<SessionInfo> Sessions);

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
    //
    // Concurrency contract — the reason this class has three locks instead of one:
    //
    //   _writerLock   Held for a whole write pass (the cold-start scan, or one debounce batch),
    //                 disk I/O included. Only writers take it, so at most one pass mutates the
    //                 graph at a time and a reader never waits behind a file read.
    //   _sessionsLock Guards _projectData and _filePositions. Held only for in-memory work:
    //                 never across a filesystem call, and never across an AppLog.Write, which
    //                 opens a file per entry.
    //   _debounceLock Guards the pending-change set and the debounce timer handle.
    //
    // _sessions is published by reference and never mutated after publication, so the Sessions
    // getter — the UI thread's first act on every DataUpdated — takes no lock at all.

    private readonly string _projectsDirectory;
    private readonly string _cacheDirectory;
    private readonly IPricingService _pricingService;
    private readonly Lock _sessionsLock = new();
    private readonly Lock _writerLock = new();
    private readonly object _debounceLock = new();
    private readonly HashSet<string> _pendingChangedFiles = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<SessionInfo> _sessions = [];
    private Dictionary<string, ProjectData> _projectData = [];
    private Dictionary<string, FilePositionMarker> _filePositions = [];
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private CancellationTokenSource _scanCts = new();
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

        _cacheDirectory = cacheDirectoryOverride ?? AppPaths.DataDirectory;

        _pricingService = pricingService ?? new NullPricingService();
    }

    // -------------------------------------------------------------------------
    // IJsonlService
    // -------------------------------------------------------------------------

    /// <summary>
    /// The published list, read without taking a lock. Every entry is created by
    /// <see cref="BuildSessionList"/> and never mutated afterwards, so handing the reference out is
    /// safe and the caller cannot observe a half-built list.
    /// </summary>
    public IReadOnlyList<SessionInfo> Sessions => Volatile.Read(ref _sessions);

    public bool IsScanning => Interlocked.CompareExchange(ref _isScanning, 0, 0) == 1;

    public event EventHandler? DataUpdated;

    public ContextWindowData GetContextWindow(string projectDirName)
    {
        var sessionFile = SnapshotNewestSessionFile(projectDirName);
        if (sessionFile is null)
            return ContextWindowData.Empty;

        // Everything below runs with NO lock held: this is a tail read of up to TailWindowBytes plus
        // a subagent directory glob, and the caller is a UI refresh tick. Holding _sessionsLock
        // across it made every Sessions read queue behind the file system.
        //
        // The pointer is therefore a snapshot: the file it names can be deleted, renamed or locked
        // between the snapshot and the read, so this must degrade rather than throw.
        try
        {
            return BuildContextWindow(sessionFile);
        }
        catch (IOException ex)
        {
            return HandleContextWindowFailure(projectDirName, sessionFile, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return HandleContextWindowFailure(projectDirName, sessionFile, ex);
        }
    }

    /// <summary>
    /// Copies the newest-session pointer out of the graph. A path is an immutable string, so once
    /// copied the read that follows needs no lock — and this is the only lock the context-window
    /// query takes.
    /// </summary>
    private string? SnapshotNewestSessionFile(string projectDirName)
    {
        lock (_sessionsLock)
        {
            return _projectData.TryGetValue(projectDirName, out var data)
                   && !string.IsNullOrEmpty(data.NewestSessionFile)
                ? data.NewestSessionFile
                : null;
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

        var scanToken = ResetScanCancellation();

        try
        {
            // Raised BEFORE the scan on purpose: this is the only signal that turns the scanning
            // indicator on, because the caller samples IsScanning before awaiting this method and the
            // CAS above has only just set it. The review proposed moving this to after the scan — it
            // was the pre-scan event that froze the window, but the cause was the Sessions getter
            // blocking on the lock the scan held, not the ordering. With publication by reference the
            // handler this queues no longer waits for anything, so the signal can stay where the UI
            // needs it.
            RaiseDataUpdated();

            await Task.Run(() => RunColdStartPass(scanToken));
        }
        finally
        {
            Interlocked.Exchange(ref _isScanning, 0);
            RaiseDataUpdated();
        }

        // A cancelled scan means Stop() ran: honour it instead of resurrecting the watcher it just
        // disposed.
        if (scanToken.IsCancellationRequested)
            return;

        // Guarded like the restart path in OnWatcherError: the cold-start pass has already published
        // its data, so a watcher that cannot start costs live updates, not the dashboard. Since the
        // app host starts this fire-and-forget, an escaping exception would be lost entirely.
        try
        {
            StartWatching();
        }
        catch (Exception ex)
        {
            AppLog.Write(InitializeSource, ex, "Watcher did not start — live updates unavailable until restart.");
        }
    }

    public void Stop()
    {
        CancelRunningScan();
        DisposeWatcher();
        DisposeDebounceTimer();
    }

    public void Dispose()
    {
        Stop();
        Volatile.Read(ref _scanCts).Dispose();
    }

    /// <summary>
    /// Hands the scan a token <see cref="Stop"/> can cancel. An already-cancelled source is replaced
    /// so a Stop before this scan cannot pre-cancel it; only the winner of the <c>_isScanning</c> CAS
    /// reaches here, so there is no second writer to race. The replaced source is not disposed: it
    /// carries no timer and no linked registration, and leaving it alive is what keeps a concurrent
    /// <see cref="Stop"/> from cancelling a disposed instance.
    /// </summary>
    private CancellationToken ResetScanCancellation()
    {
        var current = Volatile.Read(ref _scanCts);
        if (!current.IsCancellationRequested)
            return current.Token;

        var replacement = new CancellationTokenSource();
        Volatile.Write(ref _scanCts, replacement);
        return replacement.Token;
    }

    /// <summary>
    /// A cold-start scan is seconds of disk work on a large corpus, and Stop is either the page
    /// unloading or the process shutting down. Asking the pass to abandon its work beats finishing a
    /// scan whose result nobody will read.
    /// </summary>
    private void CancelRunningScan()
    {
        try
        {
            Volatile.Read(ref _scanCts).Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Dispose() already ran — the same shape the debounce timer's Change() guards against.
        }
    }

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

        return ReadLinesToEnd(stream);
    }

    /// <summary>
    /// Reads only lines added after startPosition.
    /// Returns the new file position for the next incremental read.
    /// </summary>
    public static (List<string> Lines, long NewPosition) ReadIncrementalLines(string filePath, long startPosition)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (startPosition >= stream.Length)
            return ([], stream.Length);

        stream.Seek(startPosition, SeekOrigin.Begin);

        return ReadLinesToEnd(stream);
    }

    /// <summary>
    /// Drains every remaining line from <paramref name="stream"/>, skipping blank ones, and returns the
    /// offset the next incremental read has to resume from. Shared by the full and the incremental read
    /// so both can only ever derive that offset one way.
    ///
    /// DROPDOWN-06: the offset is <c>stream.Position</c> — the bytes this pass actually consumed — and
    /// never <c>stream.Length</c>. Claude Code appends while the file is being read, and Length is
    /// re-queried from the OS on every access (the handle grants FileShare.Write, so .NET cannot cache
    /// it), so a Length read after the reader reached EOF already counts bytes this pass never saw.
    /// Storing that as the resume offset skips those lines permanently.
    ///
    /// The stream is left open: the caller owns it, which is also what lets a test compare the returned
    /// offset against the grown Length afterwards.
    /// </summary>
    internal static (List<string> Lines, long EndPosition) ReadLinesToEnd(Stream stream)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            LineReaderBufferBytes,
            leaveOpen: true);

        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

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

    /// <summary>
    /// One cold-start pass: adopt the cache, scan the tree, publish the result, persist the read
    /// positions. Runs under <c>_writerLock</c> so a debounce batch cannot interleave with it, and
    /// so <see cref="LoadCache"/> — which can deserialize up to <see cref="MaxCacheFileSizeBytes"/>
    /// of JSON — is off the UI thread and cannot race a concurrent read of <c>_filePositions</c>.
    /// </summary>
    private void RunColdStartPass(CancellationToken cancellationToken)
    {
        lock (_writerLock)
        {
            LoadCache();

            var result = ScanProjectsDirectory(cancellationToken);
            if (result is null)
                return;

            PublishScanResult(result);
            SaveCacheSnapshot();
        }
    }

    /// <summary>
    /// Reads and parses the whole tree into a PRIVATE project graph, then builds the session list
    /// from it — all with no lock held, because nothing here is reachable from another thread yet.
    /// Returns null when there is nothing to publish: no projects directory, or the pass was
    /// cancelled, in which case the partial graph is discarded rather than published.
    /// </summary>
    private ScanResult? ScanProjectsDirectory(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_projectsDirectory))
            return null;

        // Built from scratch: every file is force-read, so a merge into the previous graph would add
        // nothing a re-read cannot supply, while keeping projects whose directory has since gone.
        var scannedProjects = new Dictionary<string, ProjectData>();
        var scannedPositions = new Dictionary<string, FilePositionMarker>();

        foreach (var projectDir in Directory.GetDirectories(_projectsDirectory))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                AppLog.Write(ScanSource, "Scan cancelled — partial result discarded.");
                return null;
            }

            var projectDirName = Path.GetFileName(projectDir);
            var jsonlFiles = Directory.GetFiles(projectDir, JsonlFilePattern)
                .Where(f => !IsSubagentFile(f))
                .ToArray();

            if (jsonlFiles.Length == 0)
                continue;

            var data = GetOrCreateProjectData(scannedProjects, projectDirName);

            foreach (var file in jsonlFiles)
            {
                // Always do a full read on startup to rebuild the graph from scratch.
                // Cache positions are only useful for live file-watcher incremental updates.
                var slice = ReadFileSlice(file, knownPosition: null, forceFullRead: true);

                if (ApplyFileSlice(data, file, slice, scannedPositions))
                    LogMissingCwdSurrogate(projectDirName);

                AdvanceNewestSessionPointer(data, file, File.GetLastWriteTimeUtc(file));
            }
        }

        return new ScanResult(
            scannedProjects,
            scannedPositions,
            BuildSessionList(SnapshotSessionSeeds(scannedProjects)));
    }

    /// <summary>
    /// Swaps the scanned graph in and merges its read positions into the persisted map. Merging
    /// rather than replacing keeps markers the cache supplied for files this scan never saw — the
    /// map is not a pruning mechanism, and a dead marker costs nothing because
    /// <see cref="ProcessSingleFile"/> drops vanished files before it reads one.
    /// </summary>
    private void PublishScanResult(ScanResult result)
    {
        lock (_sessionsLock)
        {
            _projectData = result.Projects;

            foreach (var (filePath, marker) in result.FilePositions)
                _filePositions[filePath] = marker;
        }

        PublishSessions(result.Sessions);
    }

    private static ProjectData GetOrCreateProjectData(
        Dictionary<string, ProjectData> projects,
        string projectDirName)
    {
        if (projects.TryGetValue(projectDirName, out var data))
            return data;

        data = new ProjectData { ProjectDirName = projectDirName };
        projects[projectDirName] = data;
        return data;
    }

    private static void AdvanceNewestSessionPointer(ProjectData data, string filePath, DateTime modTimeUtc)
    {
        var modTime = new DateTimeOffset(modTimeUtc, TimeSpan.Zero);
        if (modTime <= data.NewestSessionModTime)
            return;

        data.NewestSessionModTime = modTime;
        data.NewestSessionFile = filePath;
    }

    /// <summary>
    /// Reads and parses one file. Pure I/O plus deserialization over local state only, so it runs
    /// with no lock held — it is the half of the old ParseFileIntoProject that touched the disk.
    /// <paramref name="knownPosition"/> null with <paramref name="forceFullRead"/> false means the
    /// file is new to us: only its tail is read, which is the cheap path for a watcher event naming a
    /// file the scan never saw.
    /// </summary>
    private static FileSlice ReadFileSlice(string filePath, long? knownPosition, bool forceFullRead)
    {
        List<string> lines;
        long newPosition;

        if (forceFullRead)
        {
            (lines, newPosition) = ReadAllLines(filePath);
        }
        else if (knownPosition.HasValue)
        {
            (lines, newPosition) = ReadIncrementalLines(filePath, knownPosition.Value);
        }
        else
        {
            lines = ReadTailLines(filePath).ToList();
            newPosition = new FileInfo(filePath).Length;
        }

        return new FileSlice(ParseJsonlEntries(lines).ToList(), newPosition);
    }

    /// <summary>
    /// Applies an already-parsed slice to a project. Pure memory mutation: the debounce path calls
    /// this with <c>_sessionsLock</c> held, so it must not reach the filesystem — not even through
    /// AppLog, which opens a file per entry.
    /// </summary>
    /// <returns>
    /// True when the project still has no cwd, so the caller can emit the DROPDOWN-02 surrogate
    /// diagnostic after releasing the lock.
    /// </returns>
    private static bool ApplyFileSlice(
        ProjectData data,
        string filePath,
        FileSlice slice,
        Dictionary<string, FilePositionMarker> positions)
    {
        positions[filePath] = BuildPositionMarker(slice.NewPosition);

        if (slice.Entries.Count == 0)
            return false;

        foreach (var entry in slice.Entries)
        {
            // DROPDOWN-02: resolve Cwd from the FIRST non-empty cwd across ALL parsed entries.
            // Tail-window reads frequently land on entries that omit the cwd field; iterating all
            // entries instead of relying on entries[0] stabilises hydration across cold starts.
            if (string.IsNullOrEmpty(data.Cwd) && !string.IsNullOrEmpty(entry.Cwd))
                data.Cwd = entry.Cwd;

            ApplyEntryToProjectData(entry, data);
        }

        return string.IsNullOrEmpty(data.Cwd) && !string.IsNullOrEmpty(data.ProjectDirName);
    }

    /// <summary>
    /// DROPDOWN-02 diagnostic: when no entry in a file carries a cwd field, log the surrogate that
    /// GetDisplayName will derive from the encoded project directory name. Cwd intentionally stays
    /// empty so the DROPDOWN-03 filter (IsNullOrEmpty path) keeps the session visible; DisplayName is
    /// resolved by <see cref="BuildSessionList"/> via
    /// SessionNameHelper.GetDisplayName(cwd: null, fallbackDirName: projectDirName).
    /// </summary>
    private static void LogMissingCwdSurrogate(string projectDirName)
    {
        var decoded = SessionNameHelper.DecodeProjectDirectory(projectDirName);
        AppLog.Write(
            CwdDiagnosticSource,
            $"No cwd in '{projectDirName}'; display surrogate: '{decoded ?? "(none)"}'");
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


    private static List<string> FindSubagentFilesForSession(string sessionFile)
    {
        var result = new List<string>();

        // Primary: {sessionUUID}/subagents/agent-*.jsonl
        var sessionDir = Path.ChangeExtension(sessionFile, null);
        var subagentDir = Path.Combine(sessionDir, SubagentsDirectoryName);
        if (Directory.Exists(subagentDir))
            result.AddRange(Directory.GetFiles(subagentDir, AgentFilePattern));

        // Fallback: project dir level agent files
        if (result.Count == 0)
        {
            var projectDir = Path.GetDirectoryName(sessionFile);
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

    private ContextWindowData BuildContextWindow(string sessionFile)
    {
        var entry = ReadLastAssistantEntryFromFile(sessionFile);
        if (entry is null)
            return ContextWindowData.Empty;

        var totalTokens = ComputeContextTokens(entry);
        var modelName = ResolveModelName(sessionFile, entry);
        var maxTokens = ModelContextLimits.GetMaxContextTokens(
            modelName, _pricingService.GetPrice, observedTokens: totalTokens);
        var subagentFiles = FindSubagentFilesForSession(sessionFile);
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
    ///
    /// The log entry is written before the lock is taken: AppLog opens a file per entry, and the
    /// whole point of this path is that a failing disk read no longer blocks the session list.
    /// </summary>
    private ContextWindowData HandleContextWindowFailure(string projectDirName, string sessionFile, Exception ex)
    {
        AppLog.Write(ContextWindowSource, ex, $"Failed to read newest session file '{sessionFile}'.");

        if (!File.Exists(sessionFile))
            ClearNewestSessionPointerIfUnchanged(projectDirName, sessionFile);

        return ContextWindowData.Empty;
    }

    /// <summary>
    /// Clears the pointer only while it still names the file that failed: the read ran unlocked, so a
    /// write pass may have advanced the pointer to a live file in the meantime and clearing that one
    /// would blank a context bar that has nothing wrong with it.
    /// </summary>
    private void ClearNewestSessionPointerIfUnchanged(string projectDirName, string sessionFile)
    {
        lock (_sessionsLock)
        {
            if (_projectData.TryGetValue(projectDirName, out var data)
                && string.Equals(data.NewestSessionFile, sessionFile, StringComparison.OrdinalIgnoreCase))
            {
                ClearNewestSessionPointer(data);
            }
        }
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
    /// <see cref="BuildSessionList"/>, which runs with no lock held — but a blocking filesystem
    /// round-trip still stalls the write pass that is building the list, so every case that cannot be
    /// answered without one is rejected up front.
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

    /// <summary>
    /// Rebuilds and publishes the session list from the current graph. The snapshot is taken under
    /// the lock; the display-name resolution and the <c>Directory.Exists</c> validity check behind it
    /// run outside it.
    /// </summary>
    private void RefreshPublishedSessions()
    {
        List<SessionSeed> seeds;
        lock (_sessionsLock)
        {
            seeds = SnapshotSessionSeeds(_projectData);
        }

        PublishSessions(BuildSessionList(seeds));
    }

    private static List<SessionSeed> SnapshotSessionSeeds(Dictionary<string, ProjectData> projectData) =>
        projectData
            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
            .Select(kvp => new SessionSeed(kvp.Key, kvp.Value.Cwd, kvp.Value.ModelName, kvp.Value.LastActivity))
            .ToList();

    private static IReadOnlyList<SessionInfo> BuildSessionList(List<SessionSeed> seeds)
    {
        var sessions = new List<SessionInfo>(seeds.Count);

        foreach (var seed in seeds)
        {
            var displayName = SessionNameHelper.GetDisplayName(seed.Cwd, seed.ProjectDirName);
            if (displayName is null)
                continue;

            // DROPDOWN-03: keep when Cwd is empty (DisplayName already resolved via the encoded
            // project directory name) OR when the Cwd path is a project directory that still exists.
            // Drop only when Cwd is non-empty AND IsValidProjectDirectory rejects it.
            if (!string.IsNullOrEmpty(seed.Cwd) && !IsValidProjectDirectory(seed.Cwd))
                continue;

            sessions.Add(new SessionInfo
            {
                Id = seed.ProjectDirName,
                Cwd = seed.Cwd ?? string.Empty,
                DisplayName = displayName,
                LastActivity = seed.LastActivity,
                ModelName = seed.ModelName
            });
        }

        // OrderByDescending, not List.Sort: the sort must be stable or two sessions sharing a
        // LastActivity would swap places between refreshes and reshuffle the picker.
        // AsReadOnly because the reference itself is handed to every caller of Sessions — the list is
        // shared, not copied, so it must not be castable back to a mutable List.
        return sessions.OrderByDescending(session => session.LastActivity).ToList().AsReadOnly();
    }

    private void PublishSessions(IReadOnlyList<SessionInfo> sessions) =>
        Volatile.Write(ref _sessions, sessions);

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
            lock (_writerLock)
            {
                foreach (var filePath in filePaths)
                    ProcessSingleFileGuarded(filePath);

                RefreshPublishedSessions();
            }
        });
    }

    // -------------------------------------------------------------------------
    // FileSystemWatcher
    // -------------------------------------------------------------------------

    private void StartWatching()
    {
        // A re-Initialize without an intervening Stop would otherwise leak the previous watcher: the
        // field is overwritten, but the old instance keeps its directory handle and keeps raising
        // events into these same handlers, so every round-trip doubled the event rate.
        DisposeWatcher();

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
                return;
            }

            RestartDebounceInterval(_debounceTimer);
        }
    }

    /// <summary>
    /// Restarts the debounce interval on an EXISTING timer only. Deliberately no create-if-missing
    /// branch: <see cref="Stop"/> disposes the timer and nulls the field, and resurrecting it here
    /// would restart the debounce loop after shutdown.
    /// </summary>
    private void RescheduleDebounceTimer()
    {
        lock (_debounceLock)
        {
            if (_debounceTimer is not null)
                RestartDebounceInterval(_debounceTimer);
        }
    }

    private static void RestartDebounceInterval(System.Threading.Timer timer)
    {
        try
        {
            timer.Change(DebounceMilliseconds, System.Threading.Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Timer was disposed between null-check and Change() — safe to ignore
        }
    }

    /// <summary>
    /// Drops every reference to a JSONL file the watcher reports as deleted: the cached read
    /// position, which a file recreated under the same name would otherwise resume from, and the
    /// per-project newest-file pointer, which would otherwise name a vanished path for the whole
    /// process lifetime. Deleting a directory reports the directory rather than each file it held,
    /// so <see cref="GetContextWindow"/> keeps its own guard for what this handler cannot see.
    ///
    /// Deliberately does NOT take <c>_writerLock</c>: this runs on the watcher's event thread, and
    /// blocking it for the duration of a cold-start scan risks overflowing the watcher's internal
    /// buffer. The cost is that a scan publishing right after this handler ran can reinstate the
    /// pointer it cleared, which the guard in <see cref="GetContextWindow"/> then clears again.
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
        // Another write pass owns the graph: the cold-start scan, or an overlapping debounce callback
        // (Change() on a timer whose callback is already running schedules a second one). Re-arming
        // instead of blocking keeps this thread-pool thread free AND keeps the pending set intact, so
        // the batch lands as soon as that pass finishes. The predecessor skipped the batch outright on
        // a racy _isScanning read, which left those changes waiting for the next write to the tree.
        if (!_writerLock.TryEnter())
        {
            RescheduleDebounceTimer();
            return;
        }

        try
        {
            foreach (var filePath in DrainPendingFiles())
            {
                ProcessSingleFileGuarded(filePath);
            }

            RefreshPublishedSessions();
            SaveCacheSnapshot();
        }
        catch (Exception ex)
        {
            AppLog.Write(FileChangeSource, ex, "Error processing pending file changes.");
        }
        finally
        {
            _writerLock.Exit();
        }

        // Raised outside the lock and outside the catch: a subscriber runs arbitrary code, and the UI
        // needs the refresh even when persisting the batch failed.
        RaiseDataUpdated();
    }

    private List<string> DrainPendingFiles()
    {
        lock (_debounceLock)
        {
            var drained = new List<string>(_pendingChangedFiles);
            _pendingChangedFiles.Clear();
            return drained;
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
        // registered here. Their content is read on demand by FindSubagentFilesForSession,
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

        // Read and parse with NO lock held — an active session file is up to TailWindowBytes and a UI
        // refresh tick must not queue behind it. Reading before the ProjectData is created also means a
        // file that cannot be opened leaves no empty project behind.
        var slice = ReadFileSlice(filePath, SnapshotFilePosition(filePath), forceFullRead: false);
        var modTimeUtc = File.GetLastWriteTimeUtc(filePath);

        bool cwdUnresolved;
        lock (_sessionsLock)
        {
            var data = GetOrCreateProjectData(_projectData, projectDirName);
            cwdUnresolved = ApplyFileSlice(data, filePath, slice, _filePositions);
            AdvanceNewestSessionPointer(data, filePath, modTimeUtc);
        }

        if (cwdUnresolved)
            LogMissingCwdSurrogate(projectDirName);
    }

    private long? SnapshotFilePosition(string filePath)
    {
        lock (_sessionsLock)
        {
            return _filePositions.TryGetValue(filePath, out var marker)
                ? marker.LastReadPosition
                : null;
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

            lock (_sessionsLock)
            {
                _filePositions = positions;
            }
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

    /// <summary>
    /// Copies the read positions under the lock and serializes the copy outside it: the serializer
    /// enumerates the dictionary, which a concurrent write pass must not be able to mutate mid-write,
    /// and the file write itself has no business holding a lock the UI reads through. Called only from
    /// a <c>_writerLock</c> holder, so two passes cannot write the cache file at the same time.
    /// </summary>
    private void SaveCacheSnapshot()
    {
        Dictionary<string, FilePositionMarker> snapshot;
        lock (_sessionsLock)
        {
            snapshot = new Dictionary<string, FilePositionMarker>(_filePositions);
        }

        SaveCache(snapshot);
    }

    private void SaveCache(Dictionary<string, FilePositionMarker> filePositions)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                Directory.CreateDirectory(_cacheDirectory);

            var cache = new JsonlCache
            {
                SchemaVersion = JsonlCache.CurrentSchemaVersion,
                FilePositions = filePositions
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

    private static FilePositionMarker BuildPositionMarker(long newPosition) =>
        new()
        {
            LastReadPosition = newPosition,
            FileSize = newPosition,
            LastWriteTime = DateTimeOffset.UtcNow
        };

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

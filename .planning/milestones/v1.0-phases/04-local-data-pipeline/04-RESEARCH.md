# Phase 4: Local Data Pipeline - Research

**Researched:** 2026-03-11
**Domain:** JSONL file parsing, FileSystemWatcher, session management, context window UI
**Confidence:** HIGH (all findings verified against live JSONL data and official docs)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Tolerant JSONL parsing**: unknown fields ignored, missing fields get defaults — System.Text.Json with `JsonIgnoreCondition.WhenWritingNull` / `PropertyNameCaseInsensitive`
- **Read last ~1 MB of each JSONL file** — seek to end, read backwards; no full-file parse
- **Deduplication by `message.id` and `requestId`** to prevent double-counting (TOKS-04). *(Corrected 2026-08-06: this line originally read "`uuid` (messageId)". `uuid` is NOT the message id — it is unique per JSONL LINE, and one assistant message spans several lines, so keying on it deduplicates nothing. `message.id` is the message identity; `uuid` survives only as a fallback for lines that carry no `message.id`.)*
- **Single FileSystemWatcher** on `%USERPROFILE%\.claude\projects\` with `IncludeSubdirectories = true`
- **NotifyFilters**: `LastWrite | FileName | Size`
- **300ms debounce timer** to prevent double-triggers
- **Watcher stays active across session switches** — no restart needed
- **Startup behavior**: background scan with "Scanning sessions..." indicator; API data shows immediately from cache; JSONL data loads independently
- **JSONL cache** at `%LOCALAPPDATA%\CCInfoWindows\jsonl-cache.json` — load cache first, then parse only new lines
- **Cache invalidation** via file size + last-write timestamp comparison
- **Cache format includes per-file position markers** for incremental reads
- **Session dropdown placement**: top of dashboard, above all sections
- **Session name**: last folder name of working directory path (`cwd` field)
- **Encoded directory names** shown as "Unbekanntes Projekt" if undecodable (SESS-05)
- **Grouped dropdown**: "Aktiv" at top, "Inaktiv" below with visual separator
- **Inactivity threshold**: configurable in Settings (SETT-03), default 30 minutes
- **`lastSelectedSessionId` persisted** in AppSettings; restored on startup
- **No auto-switching** away from current selection when it becomes inactive (SESS-04)
- **No flickering** of stale data when switching sessions — cache provides instant data
- **Context window layout**: full-width progress bar, percentage left-aligned, model badge right-aligned
- **Subagent bars**: dynamically appear/disappear, indented, smaller font, own model badge
- **Autocompact warning**: orange text "⚠ Autocompact bald" at >= 95% (>= 90% for 200K models)
- **No active session**: 0% bar (grayed out) + "Keine aktive Session"
- **Color thresholds**: reuse existing `ColorThresholds.cs`
- **TOKENS section**: own section below KONTEXTFENSTER, consistent section pattern
- **Token formatting**: compact with suffix — 1.2K, 1.2M
- **Per-session aggregation only** in Phase 4 (today/week/month tabs are Phase 5)

### Claude's Discretion
- FileSystemWatcher initialization and error recovery (AccessDenied, too many watchers)
- Exact JSONL field extraction beyond model, tokens, context, workingDirectory
- Cache file format and incremental read implementation details
- Subagent detection logic (how to identify subagent vs main conversation in JSONL)
- Session ID generation strategy (hash of working directory path or similar)
- Exact debounce timer implementation (DispatcherQueueTimer vs System.Threading.Timer)
- Context window size detection per model (how to map model name to max context tokens)

### Deferred Ideas (OUT OF SCOPE)
- Token aggregation by time period tabs (today/week/month) — Phase 5 (TOKS-02)
- Subagent tokens in time period aggregations — Phase 5 (TOKS-03)
- Cost calculation from `costUSD` field — Phase 5 (COST-02)
- LiteLLM pricing integration — Phase 5 (COST-01)
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| DATA-03 | JSONL files read from `%USERPROFILE%\.claude\projects\` with streaming (last ~1MB only) | Verified JSONL structure; tail-read pattern documented |
| DATA-04 | JSONL file changes detected via FileSystemWatcher with debouncing | FileSystemWatcher API verified; debounce pattern confirmed |
| DATA-05 | LiteLLM pricing cache persisted locally (Phase 5 dependency, not Phase 4 work) | Out of scope for Phase 4 |
| SESS-01 | Dropdown lists all active sessions with project name from JSONL `cwd` field | `cwd` field confirmed present in every JSONL entry |
| SESS-02 | Configurable activity threshold to hide/mark inactive sessions | Last `timestamp` field in JSONL enables activity detection |
| SESS-03 | No flickering of stale data when switching sessions | Cache-first strategy solves this |
| SESS-04 | No auto-switch away from current session when it becomes inactive | ViewModel state management decision |
| SESS-05 | Readable session names for encoded directory paths | Encoding algorithm verified: each non-alphanumeric char → `-` |
| CTXW-01 | Main context window utilization shown with progress bar and percentage | Token formula verified: `input_tokens + cache_read_input_tokens + cache_creation_input_tokens` |
| CTXW-02 | Model badge next to context bar | `message.model` field confirmed in every assistant entry |
| CTXW-03 | Active subagent context windows with own model badge and bar | Subagent JSONL confirmed at `{sessionId}/subagents/agent-*.jsonl` with `isSidechain: true` |
| CTXW-04 | Autocompact warning at >= 95% (>= 90% for 200K models) | Model→context size mapping needed; all current models are 200K |
| CTXW-05 | No active session: 0% bar + "No active session" message | UI null-state pattern; no JSONL dependency |
| TOKS-01 | Input and output token counters aggregated by session | `message.usage.input_tokens` + `output_tokens` fields confirmed |
| SETT-03 | Session activity threshold configuration in Settings | Extend `AppSettings` + `SettingsService` (existing pattern) |
</phase_requirements>

---

## Summary

Phase 4 reads Claude Code's local JSONL transcript files to derive session data, context window utilization, and token counts — all without an API call. The research confirmed the complete JSONL schema by directly inspecting live files on this machine.

The critical architectural insight is that the file structure has two JSONL locations: the main session file at `~/.claude/projects/{encoded-cwd}/{sessionId}.jsonl` and subagent files at `~/.claude/projects/{encoded-cwd}/{sessionId}/subagents/agent-*.jsonl`. Subagents are identified by `isSidechain: true` and the `agentId` field. The directory encoding algorithm is deterministic: every non-alphanumeric character in the absolute path is replaced with a hyphen (e.g., `D:\myProjects\ccInfoWin` → `D--myProjects-ccInfoWin`).

Context window utilization is calculated from the most recent `assistant` message in the main-chain session: total = `input_tokens + cache_read_input_tokens + cache_creation_input_tokens`. There is no dedicated "context window" field — the usage object IS the context window state. The `costUSD` field referenced in requirements does not appear in the actual JSONL files on this machine (as of Claude Code v2.1.72); this field is deferred to Phase 5 anyway.

**Primary recommendation:** Implement `IJsonlService` as a singleton that owns the `FileSystemWatcher`, maintains an in-memory session index, and exposes observable data. Use `System.Threading.Timer` (not `DispatcherQueueTimer`) for debounce since the timer runs on a threadpool thread and must not marshal to UI until data is ready.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.IO.FileSystemWatcher | .NET 9 built-in | Watch `~/.claude/projects/` for JSONL changes | Only .NET option; no NuGet needed |
| System.Text.Json | .NET 9 built-in | Tolerant JSONL deserialization | Already used throughout codebase |
| System.IO.RandomAccess / FileStream | .NET 9 built-in | Seek-to-end tail read of large JSONL files | Avoids loading full 2+ MB files |
| CommunityToolkit.Mvvm | 8.4 (existing) | Observable properties for session/context/token state | Already in project |
| WeakReferenceMessenger | 8.4 (existing) | `SessionSelectedMessage`, `JsonlDataUpdatedMessage` | Established pattern in project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Threading.Timer | .NET 9 built-in | 300ms debounce for FileSystemWatcher events | Use instead of DispatcherQueueTimer — debounce runs on threadpool, not UI thread |
| System.Security.Cryptography.SHA256 | .NET 9 built-in | Session ID from working directory hash | Only needed if UUID from filename is insufficient as ID |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Single watcher with IncludeSubdirectories | Multiple watchers per project directory | Single watcher is simpler, covers new projects automatically |
| System.Threading.Timer for debounce | DispatcherQueueTimer | DispatcherQueueTimer runs on UI thread — wrong for background file I/O |
| File tail read (seek to end - 1MB) | Full file read | Full read costs 50-100ms for 2.8MB files; tail read costs <5ms |

**Installation:** No new packages — all required functionality is in .NET 9 BCL and existing NuGet references.

---

## Architecture Patterns

### Recommended Service Structure
```
Services/
├── JsonlService.cs           # Singleton: FileSystemWatcher + JSONL parsing + session index
├── Interfaces/
│   └── IJsonlService.cs      # Contract for MainViewModel injection
Models/
├── SessionInfo.cs            # Session: id, cwd, displayName, lastActivity, isActive
├── JsonlEntry.cs             # Deserialization target for JSONL lines
├── JsonlCache.cs             # Cache file format with file-position markers
├── ContextWindowData.cs      # totalTokens, maxTokens, modelName, subagents[]
├── TokenSummary.cs           # inputTokens, outputTokens per session
```

### Pattern 1: JSONL Entry Deserialization

**What:** Tolerant deserialization of JSONL lines into typed records, ignoring unknown fields.
**When to use:** Every line read from any `.jsonl` file.

```csharp
// Source: verified against live JSONL on D:\myProjects\ccInfoWin
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement, // .NET 9
    // Alternative: DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Top-level JSONL entry — all fields optional/nullable
public record JsonlEntry
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("uuid")] public string? Uuid { get; init; }
    [JsonPropertyName("requestId")] public string? RequestId { get; init; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; init; }
    [JsonPropertyName("cwd")] public string? Cwd { get; init; }
    [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; init; }
    [JsonPropertyName("isSidechain")] public bool IsSidechain { get; init; }
    [JsonPropertyName("agentId")] public string? AgentId { get; init; }
    [JsonPropertyName("message")] public JsonlMessage? Message { get; init; }
}

public record JsonlMessage
{
    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("usage")] public JsonlUsage? Usage { get; init; }
}

public record JsonlUsage
{
    [JsonPropertyName("input_tokens")] public long InputTokens { get; init; }
    [JsonPropertyName("output_tokens")] public long OutputTokens { get; init; }
    [JsonPropertyName("cache_read_input_tokens")] public long CacheReadInputTokens { get; init; }
    [JsonPropertyName("cache_creation_input_tokens")] public long CacheCreationInputTokens { get; init; }
}
```

### Pattern 2: Context Window Token Calculation

**What:** Total context tokens from the most recent non-sidechain assistant message.
**When to use:** After parsing each JSONL file to compute CTXW-01 utilization.

```csharp
// Source: verified against codelynx.dev analysis + live JSONL inspection
// Total context = all token types in the usage object
long TotalContextTokens(JsonlUsage usage) =>
    usage.InputTokens + usage.CacheReadInputTokens + usage.CacheCreationInputTokens;

// Context utilization (0.0 to 1.0+)
double ContextUtilization(long totalTokens, long maxTokens) =>
    maxTokens > 0 ? (double)totalTokens / maxTokens : 0.0;
```

### Pattern 3: Model → Context Window Size Mapping

**What:** Map `message.model` string to max context tokens.
**When to use:** Computing CTXW-01 percentage and CTXW-04 autocompact threshold.

```csharp
// Source: platform.claude.com/docs/en/about-claude/models/overview (verified 2026-03-11)
// All current models are 200K tokens standard
// Opus 4.6 and Sonnet 4.6 support 1M beta (not typically used in Claude Code)
private static readonly Dictionary<string, long> ModelContextLimits = new(StringComparer.OrdinalIgnoreCase)
{
    // Current models (200K)
    ["claude-opus-4-6"]              = 200_000,
    ["claude-sonnet-4-6"]            = 200_000,
    ["claude-haiku-4-5"]             = 200_000,
    ["claude-haiku-4-5-20251001"]    = 200_000,
    // Legacy models still in use
    ["claude-sonnet-4-5"]            = 200_000,
    ["claude-sonnet-4-5-20250929"]   = 200_000,
    ["claude-opus-4-5"]              = 200_000,
    ["claude-opus-4-1"]              = 200_000,
    ["claude-sonnet-4-0"]            = 200_000,
    ["claude-opus-4-0"]              = 200_000,
    ["claude-sonnet-4-20250514"]     = 200_000,
    ["claude-opus-4-20250514"]       = 200_000,
};

private const long DefaultContextLimit = 200_000;

public static long GetMaxContextTokens(string? modelName) =>
    modelName is not null && ModelContextLimits.TryGetValue(modelName, out var limit)
        ? limit
        : DefaultContextLimit;
```

### Pattern 4: File Tail Read (Last ~1MB)

**What:** Read only the last 1MB of a JSONL file to avoid loading multi-MB files.
**When to use:** Initial parse of any session JSONL file.

```csharp
// Source: verified against 2.8MB live JSONL file; standard file seek pattern
private const long TailReadSize = 1_048_576; // 1 MB

private static IEnumerable<string> ReadTailLines(string filePath)
{
    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var startPosition = Math.Max(0, stream.Length - TailReadSize);
    stream.Seek(startPosition, SeekOrigin.Begin);

    using var reader = new StreamReader(stream);

    // Discard first partial line if not at start of file
    if (startPosition > 0)
        reader.ReadLine();

    while (reader.ReadLine() is { } line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            yield return line;
    }
}
```

### Pattern 5: Session Directory Encoding/Decoding

**What:** Decode `~/.claude/projects/` directory names back to human-readable project names.
**When to use:** Building session list for SESS-01, SESS-05.

```csharp
// Source: directly verified by comparing actual directory names against cwd field in JSONL
// Algorithm: every non-alphanumeric character in the absolute path is replaced with '-'
// Example: "D:\myProjects\ccInfoWin" -> "D--myProjects-ccInfoWin"
// The cwd field in JSONL contains the ORIGINAL path — use that for display!

// For session display name: use LAST segment of cwd path
public static string GetDisplayName(string? cwd)
{
    if (string.IsNullOrEmpty(cwd))
        return "Unbekanntes Projekt";

    // cwd is the raw OS path: "D:\myProjects\ccInfoWin" or "/home/user/proj"
    var lastSeparator = cwd.LastIndexOfAny(['\\', '/']);
    return lastSeparator >= 0 ? cwd[(lastSeparator + 1)..] : cwd;
}

// Decode encoded directory name when no JSONL is available (SESS-05)
public static string DecodeProjectDirectory(string encodedName)
{
    // Cannot reliably reverse the encoding (colon, backslash, slash all become -)
    // If cwd is available from JSONL, use that instead
    // Fall back: split by '-', filter empty, rejoin — gives partial readability
    var parts = encodedName.Split('-', StringSplitOptions.RemoveEmptyEntries);
    return parts.Length > 0 ? parts[^1] : "Unbekanntes Projekt";
}
```

### Pattern 6: FileSystemWatcher with Debounce

**What:** Single watcher on projects root, debounced with `System.Threading.Timer`.
**When to use:** `JsonlService` initialization.

```csharp
// Source: Microsoft Learn FileSystemWatcher docs + best practices
private FileSystemWatcher? _watcher;
private System.Threading.Timer? _debounceTimer;
private readonly object _debounceLock = new();
private const int DebounceMilliseconds = 300;

public void StartWatching(string projectsDirectory)
{
    _watcher = new FileSystemWatcher(projectsDirectory)
    {
        Filter = "*.jsonl",
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        InternalBufferSize = 65536, // 64KB — default 8KB fills too fast with many events
        EnableRaisingEvents = true
    };

    _watcher.Changed += OnFileChanged;
    _watcher.Created += OnFileChanged;
    _watcher.Error += OnWatcherError;
}

private void OnFileChanged(object sender, FileSystemEventArgs e)
{
    // Debounce: reset timer on every event
    lock (_debounceLock)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(
            _ => ProcessChangesAsync(e.FullPath),
            null,
            DebounceMilliseconds,
            Timeout.Infinite);
    }
}

private void OnWatcherError(object sender, ErrorEventArgs e)
{
    // Buffer overflow or access error — restart watcher
    _watcher?.Dispose();
    StartWatching(_projectsDirectory);
}
```

### Pattern 7: Subagent Discovery

**What:** Find subagent JSONL files for a session and aggregate their context windows.
**When to use:** Building CTXW-03 subagent bars.

```csharp
// Source: direct inspection of D:\myProjects\ccInfoWin JSONL files
// Subagent files are at: {projectDir}/{sessionId}/subagents/agent-{agentId}.jsonl
// They have isSidechain: true and an agentId field
// Meta file: agent-{agentId}.meta.json contains { "agentType": "Explore" } etc.

public static IEnumerable<string> GetSubagentFiles(string projectDir, string sessionId)
{
    var subagentDir = Path.Combine(projectDir, sessionId, "subagents");
    if (!Directory.Exists(subagentDir))
        return [];
    return Directory.GetFiles(subagentDir, "agent-*.jsonl");
}
```

### Pattern 8: Incremental Read with Cache

**What:** Track file position to read only new JSONL lines since last parse.
**When to use:** `FileSystemWatcher` change callback — after initial full parse.

```csharp
// Cache stores: filePath -> { LastReadPosition, LastWriteTime, FileSize }
// On change event:
//   1. Check current FileSize and LastWriteTime
//   2. If unchanged: skip
//   3. If changed: open file, seek to LastReadPosition, read new lines
//   4. Update cache entry

public record FilePositionMarker
{
    public long LastReadPosition { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset LastWriteTime { get; set; }
}
```

### Anti-Patterns to Avoid
- **Parsing entire JSONL on every watcher event**: Files are 1-3 MB; re-parsing on every keystroke would be O(N) per event — use incremental reads.
- **DispatcherQueueTimer for debounce**: DispatcherQueueTimer runs on UI thread — debounce timer must run on threadpool; marshal to UI only when data is ready.
- **Locking on DispatcherQueue.TryEnqueue**: The enqueue itself is thread-safe; only call it from background thread to update observable properties.
- **Starting watcher before projects directory exists**: Check and create directory first; watcher throws `ArgumentException` if path doesn't exist.
- **Watching individual session files**: Watch the projects root with `IncludeSubdirectories = true` — simpler and covers new session creation automatically.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSONL deserialization | Custom line parser | System.Text.Json with `PropertyNameCaseInsensitive` | Edge cases: escaped quotes, unicode, null fields |
| File tail read | Byte-by-byte backward scanner | `FileStream.Seek(offset, SeekOrigin.Begin)` + `StreamReader` | Simpler, handles encoding correctly |
| Debounce timer | Busy-wait or sleep loop | `System.Threading.Timer` with Dispose-on-reset | Correct threadpool behavior, no UI blocking |
| Model context size lookup | Dynamic API call | Static dictionary (all current models = 200K) | Models are versioned and stable; lookup takes 0ms |
| Session activity detection | Complex heuristics | Last `timestamp` from assistant message | Single field, accurate, in every entry |

**Key insight:** The JSONL format is simple enough that no parsing library is needed beyond System.Text.Json, but the incremental read + cache pattern is essential for performance.

---

## Common Pitfalls

### Pitfall 1: FileSystemWatcher Duplicate Events
**What goes wrong:** Single file write triggers 2-4 `Changed` events (Windows NTFS behavior).
**Why it happens:** Claude Code appends a line to JSONL, which triggers multiple write notifications (metadata update + data update).
**How to avoid:** 300ms debounce timer that resets on every event — only the final event triggers processing.
**Warning signs:** Token counts flickering on every keypress.

### Pitfall 2: FileStream Sharing Violation
**What goes wrong:** `IOException: The process cannot access the file because it is being used by another process.`
**Why it happens:** Claude Code holds a write lock on the JSONL file while appending.
**How to avoid:** Always open with `FileShare.ReadWrite` — Claude Code uses write-sharing internally.
**Warning signs:** Exceptions in `OnFileChanged` handler.

### Pitfall 3: Context Window = Last Assistant Message Only
**What goes wrong:** Summing ALL assistant messages in session gives token totals 10x too high for context window.
**Why it happens:** Context window is a snapshot of current context state, not cumulative.
**How to avoid:** For context window (CTXW-01): use ONLY the LAST non-sidechain assistant message. For token totals (TOKS-01): sum output_tokens across ALL non-sidechain assistant messages.
**Warning signs:** Context bar shows > 100% on short sessions.

### Pitfall 4: isSidechain Detection Scope
**What goes wrong:** Including subagent tokens in main session context window calculation.
**Why it happens:** Subagents write to separate files but share the same `sessionId`.
**How to avoid:** Main session JSONL contains only `isSidechain: false` entries. Subagent files are in `{sessionId}/subagents/` directories and have `isSidechain: true`. Treat them as separate context windows (CTXW-03).
**Warning signs:** Context window showing combined subagent + main tokens.

### Pitfall 5: Session "Active" Status Edge Cases
**What goes wrong:** Sessions appear active even when Claude Code is closed.
**Why it happens:** Last `timestamp` stays the same after session ends.
**How to avoid:** `isActive` = last assistant message timestamp within configurable threshold (default 30 min). The `last-prompt` entry type signals session end but is not reliable (may not be written if Claude Code crashes).
**Warning signs:** All sessions appear active.

### Pitfall 6: FileSystemWatcher Buffer Overflow
**What goes wrong:** `Error` event fires with `InternalBufferOverflowException`, watcher stops.
**Why it happens:** Default 8KB internal buffer fills when many files change simultaneously.
**How to avoid:** Set `InternalBufferSize = 65536` (64KB). Handle `Error` event by restarting watcher.
**Warning signs:** File changes stop being detected after heavy Claude Code use.

### Pitfall 7: Partial Line at Tail Read Start
**What goes wrong:** First line of tail read is garbage JSON (cut in the middle).
**Why it happens:** Seeking to `fileLength - 1MB` lands in the middle of a line.
**How to avoid:** Always discard the first line after a non-zero seek position — it's almost certainly a partial line.
**Warning signs:** JsonException on first parsed line.

---

## Code Examples

### Complete JSONL Entry Structure (verified live)

```json
// Main session entry (isSidechain: false)
{
  "parentUuid": "692e425e-...",
  "isSidechain": false,
  "userType": "external",
  "cwd": "D:\\myProjects\\ccInfoWin",
  "sessionId": "0ccbe4cf-89f3-45f2-8ae4-d10970c8dce4",
  "version": "2.1.71",
  "gitBranch": "master",
  "slug": "zesty-gathering-dewdrop",
  "type": "assistant",
  "uuid": "b6847418-746a-4838-83af-4e4ceb780385",
  "requestId": "req_011CYsAisMrrii6tyqZwEKKS",
  "timestamp": "2026-03-09T08:11:25.145Z",
  "message": {
    "model": "claude-opus-4-6",
    "id": "msg_01SJkkmR8ui1GD8Z9Wcqvwax",
    "type": "message",
    "role": "assistant",
    "content": [...],
    "stop_reason": "end_turn",
    "usage": {
      "input_tokens": 3,
      "cache_creation_input_tokens": 8807,
      "cache_read_input_tokens": 6380,
      "output_tokens": 16,
      "server_tool_use": { "web_search_requests": 0, "web_fetch_requests": 0 },
      "service_tier": "standard",
      "cache_creation": { "ephemeral_1h_input_tokens": 8807, "ephemeral_5m_input_tokens": 0 },
      "inference_geo": ""
    }
  }
}

// Subagent entry (isSidechain: true) — in {sessionId}/subagents/agent-{agentId}.jsonl
{
  "parentUuid": null,
  "isSidechain": true,
  "agentId": "a862ba14ceb379e6a",
  "cwd": "D:\\myProjects\\ccInfoWin",
  "sessionId": "10c8d296-57d2-4dc0-92c5-125ed90fd59f",
  "type": "assistant",
  "message": {
    "model": "claude-haiku-4-5-20251001",
    "usage": {
      "input_tokens": 3,
      "cache_creation_input_tokens": 20948,
      "cache_read_input_tokens": 0,
      "output_tokens": 1
    }
  }
}
```

### Token Formatting (TOKS-01)
```csharp
// Source: requirement in CONTEXT.md; pattern from CountdownFormatter.cs
// German thousands separator (dot), English suffix (K, M)
public static string FormatTokenCount(long tokens) => tokens switch
{
    >= 1_000_000 => $"{tokens / 1_000_000.0:0.0}M",
    >= 1_000     => $"{tokens / 1_000.0:0.0}K",
    _            => tokens.ToString("N0", CultureInfo.GetCultureInfo("de-DE"))
};
```

### Autocompact Threshold Detection (CTXW-04)
```csharp
// Source: verified against CONTEXT.md decisions
// 200K models: warn at >= 90%. All current models are 200K.
private const double AutocompactThresholdStandard = 0.95;
private const double AutocompactThresholdLargeContext = 0.90;
private const long LargeContextThreshold = 200_000;

public static bool ShouldWarnAutocompact(long totalTokens, long maxTokens) =>
    maxTokens >= LargeContextThreshold
        ? (double)totalTokens / maxTokens >= AutocompactThresholdLargeContext
        : (double)totalTokens / maxTokens >= AutocompactThresholdStandard;
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Polling files on timer | FileSystemWatcher event-driven | Standard practice | Instant updates, no CPU waste |
| Full file read on each change | Tail read (last 1MB) + incremental | Per CONTEXT.md decision | 50-100x faster on 3MB files |
| Each session file read independently | Grouped by project directory | Per JSONL structure | One watcher covers all sessions |
| `costUSD` field expected | Field NOT present in actual JSONL (v2.1.72) | Observed live | Phase 5 COST-02 approach needs verification |

**Deprecated/outdated:**
- No `costUSD` field found in actual JSONL on this machine (v2.1.71, v2.1.72). The field may have existed in older versions or may not be present in standard-tier sessions. Phase 5 should reverify before implementing COST-02.

---

## Open Questions

1. **`costUSD` field absence**
   - What we know: Inspected 40+ JSONL files across 4 projects — no `costUSD` field at top-level or in `message`
   - What's unclear: Was it removed, never added, or only present for specific account types?
   - Recommendation: Phase 5 should inspect fresh JSONL after cost-generating interactions before implementing COST-02

2. **Context window for 1M-token sessions**
   - What we know: Claude Opus 4.6 and Sonnet 4.6 support 1M context in beta; Claude Code reportedly shows 200K even for Opus 4.6 (GitHub issue #24208)
   - What's unclear: Does the `usage` object reflect actual context size (200K vs 1M)?
   - Recommendation: Default to 200K for all models. If `total_context_tokens` exceeds 200K, cap display at 200K and flag as unknown model

3. **Session "currently active" vs "last active"**
   - What we know: No live session signal in JSONL — only timestamps. No socket/IPC to Claude Code.
   - What's unclear: Can we detect if Claude Code is actively running for a session?
   - Recommendation: "Active" = last assistant message within configurable threshold (default 30 min). This matches user expectation; no live signal needed.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 + Moq 4.20.72 |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category!=Integration" -p:Platform=x64` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| DATA-03 | Tail read returns last N bytes of JSONL | unit | `dotnet test --filter "JsonlService"` | ❌ Wave 0 |
| DATA-03 | Tolerant parse ignores unknown fields | unit | `dotnet test --filter "JsonlParsing"` | ❌ Wave 0 |
| DATA-03 | Partial first line after seek is discarded | unit | `dotnet test --filter "TailRead"` | ❌ Wave 0 |
| DATA-04 | Debounce coalesces multiple rapid events | unit | `dotnet test --filter "FileWatcher"` | ❌ Wave 0 |
| SESS-01 | Display name extracted from cwd last segment | unit | `dotnet test --filter "SessionInfo"` | ❌ Wave 0 |
| SESS-05 | Encoded directory name decoded to readable form | unit | `dotnet test --filter "SessionInfo"` | ❌ Wave 0 |
| CTXW-01 | Total context tokens = input + cache_read + cache_creation | unit | `dotnet test --filter "ContextWindow"` | ❌ Wave 0 |
| CTXW-02 | Model string maps to display badge text | unit | `dotnet test --filter "ContextWindow"` | ❌ Wave 0 |
| CTXW-04 | Autocompact warning fires at correct threshold | unit | `dotnet test --filter "ContextWindow"` | ❌ Wave 0 |
| TOKS-01 | Token aggregation sums only non-sidechain messages | unit | `dotnet test --filter "TokenAggregation"` | ❌ Wave 0 |
| TOKS-01 | Token formatter produces K/M suffix correctly | unit | `dotnet test --filter "TokenFormatter"` | ❌ Wave 0 |
| SETT-03 | Activity threshold persists and restores | unit | `dotnet test --filter "SettingsService"` | ❌ Wave 0 (extend existing) |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64 --filter "JsonlService|ContextWindow|TokenAggregation|SessionInfo"`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` — covers DATA-03, DATA-04, TOKS-01
- [ ] `CCInfoWindows.Tests/Helpers/ContextWindowTests.cs` — covers CTXW-01, CTXW-02, CTXW-04
- [ ] `CCInfoWindows.Tests/Helpers/TokenFormatterTests.cs` — covers TOKS-01 formatting
- [ ] `CCInfoWindows.Tests/Models/SessionInfoTests.cs` — covers SESS-01, SESS-05

---

## Sources

### Primary (HIGH confidence)
- Live JSONL files inspected at `C:/Users/DanielMielke/.claude/projects/` — JSONL schema, field names, subagent structure, encoding algorithm
- `platform.claude.com/docs/en/about-claude/models/overview` — Model context window limits (all current = 200K)
- `learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher` — FileSystemWatcher API, InternalBufferSize, Error event
- Existing codebase: `ColorThresholds.cs`, `CountdownFormatter.cs`, `UsageHistoryService.cs`, `AppSettings.cs`

### Secondary (MEDIUM confidence)
- codelynx.dev/posts/calculate-claude-code-context — Token formula: `input_tokens + cache_read_input_tokens + cache_creation_input_tokens` (cross-verified with live data)
- dev.to/yurukusa — Tail read pattern: last 64KB; formula confirmed independently
- gist.github.com/BoQsc — Directory encoding algorithm (cross-verified by comparison of live directories vs cwd field)

### Tertiary (LOW confidence)
- WebSearch results on subagent isSidechain field — confirmed by direct file inspection (elevated to HIGH)
- github.com/anthropics/claude-code issue #24208 — 1M context display bug in Claude Code (single source, flagged)

---

## Metadata

**Confidence breakdown:**
- JSONL schema and field names: HIGH — directly verified from 40+ live files
- Context window formula: HIGH — cross-verified by 2 independent sources + live data
- FileSystemWatcher patterns: HIGH — official Microsoft docs
- Model context limits: HIGH — official Anthropic docs
- Subagent detection: HIGH — direct file inspection shows `isSidechain: true` in subagent files
- costUSD field: HIGH (that it is absent) — not found in any of 40+ files inspected
- 1M context handling: LOW — single GitHub issue source, not verified with live 1M session

**Research date:** 2026-03-11
**Valid until:** 2026-04-11 (stable domain; JSONL format changes rarely without version bumps)

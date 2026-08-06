using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// Tracks the read position within a JSONL file to enable incremental parsing.
/// </summary>
public record FilePositionMarker
{
    [JsonPropertyName("lastReadPosition")]
    public long LastReadPosition { get; init; }

    [JsonPropertyName("fileSize")]
    public long FileSize { get; init; }

    [JsonPropertyName("lastWriteTime")]
    public DateTimeOffset LastWriteTime { get; init; }
}

/// <summary>
/// Cached per-session aggregated data to avoid full re-parse on startup.
/// </summary>
public class CachedSessionData
{
    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("cacheReadInputTokens")]
    public long CacheReadInputTokens { get; set; }

    [JsonPropertyName("cacheCreationInputTokens")]
    public long CacheCreationInputTokens { get; set; }

    [JsonPropertyName("lastModel")]
    public string? LastModel { get; set; }

    [JsonPropertyName("lastActivity")]
    public DateTimeOffset LastActivity { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }
}

/// <summary>
/// Persistent cache mapping JSONL file paths to their read positions and session aggregates.
/// Serialized to jsonl-cache.json in the app data directory.
/// </summary>
public class JsonlCache
{
    /// <summary>
    /// Version of the persisted layout AND of the aggregation semantics behind the stored read
    /// positions. A file stamped with any other value is discarded on load, which forces a
    /// one-time full re-read of every JSONL file instead of resuming from positions that were
    /// produced by a different interpretation of the data.
    ///
    /// 2 — deduplication keys on message.id + requestId. Version 1 (unstamped) keyed on a
    /// uniqueHash field that Claude Code never writes, so its positions mark lines that were
    /// counted once per streamed content block rather than once per assistant message.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Deliberately defaults to 0 rather than <see cref="CurrentSchemaVersion"/>: System.Text.Json
    /// leaves a property at its initializer when the JSON omits the key, so a default of
    /// <see cref="CurrentSchemaVersion"/> would make every unstamped legacy file look current.
    /// The writer sets this explicitly.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("filePositions")]
    public Dictionary<string, FilePositionMarker> FilePositions { get; set; } = [];

    [JsonPropertyName("sessionData")]
    public Dictionary<string, CachedSessionData> SessionData { get; set; } = [];
}

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
    [JsonPropertyName("filePositions")]
    public Dictionary<string, FilePositionMarker> FilePositions { get; set; } = [];

    [JsonPropertyName("sessionData")]
    public Dictionary<string, CachedSessionData> SessionData { get; set; } = [];
}

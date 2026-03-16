using System.Text.Json;
using System.Text.Json.Serialization;

namespace CCInfoWindows.Models;

/// <summary>
/// Token usage from a single JSONL message.
/// </summary>
public record JsonlUsage
{
    [JsonPropertyName("input_tokens")]
    public long? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public long? OutputTokens { get; init; }

    [JsonPropertyName("cache_read_input_tokens")]
    public long? CacheReadInputTokens { get; init; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public long? CacheCreationInputTokens { get; init; }
}

/// <summary>
/// The message payload within a JSONL entry.
/// </summary>
public record JsonlMessage
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("usage")]
    public JsonlUsage? Usage { get; init; }
}

/// <summary>
/// A single deserialized JSONL log entry from Claude Code's session files.
/// All fields are nullable except IsSidechain which defaults to false.
/// </summary>
public record JsonlEntry
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    [JsonPropertyName("isSidechain")]
    public bool IsSidechain { get; init; } = false;

    [JsonPropertyName("agentId")]
    public string? AgentId { get; init; }

    [JsonPropertyName("message")]
    public JsonlMessage? Message { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("costUSD")]
    public decimal? CostUsd { get; init; }

    [JsonPropertyName("uniqueHash")]
    public string? UniqueHash { get; init; }
}

namespace CCInfoWindows.Models;

/// <summary>
/// Represents a Claude Code session with its metadata and activity state.
/// </summary>
public class SessionInfo
{
    /// <summary>Session GUID extracted from the JSONL filename.</summary>
    public required string Id { get; init; }

    /// <summary>Working directory of the project for this session.</summary>
    public required string Cwd { get; init; }

    /// <summary>Human-readable project name derived from the last path segment of Cwd.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Timestamp of the most recent activity in this session.</summary>
    public DateTimeOffset LastActivity { get; set; }

    /// <summary>Name of the most recently used model in this session.</summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// Returns true when this session had activity within the given threshold from now.
    /// </summary>
    public bool IsActive(TimeSpan threshold)
    {
        return DateTimeOffset.UtcNow - LastActivity <= threshold;
    }

    public override string ToString() => DisplayName;
}

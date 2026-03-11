namespace CCInfoWindows.Models;

/// <summary>
/// Aggregated token counts for a session.
/// </summary>
public record TokenSummary
{
    public static readonly TokenSummary Empty = new() { InputTokens = 0, OutputTokens = 0 };

    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
}

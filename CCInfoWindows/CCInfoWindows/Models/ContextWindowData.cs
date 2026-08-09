using CCInfoWindows.Helpers;

namespace CCInfoWindows.Models;

/// <summary>
/// Context window state for a single subagent within a session.
/// </summary>
public record SubagentContextData
{
    public required string AgentId { get; init; }
    public long TotalTokens { get; init; }
    public long MaxTokens { get; init; }
    public string? ModelName { get; init; }
    public DateTimeOffset LastActivity { get; init; }

    /// <summary>
    /// Run id of the workflow that spawned this agent, taken literally from the directory name
    /// under subagents/workflows/ (e.g. "wf_11f45d5b-27d"). Null for agents spawned by the Agent
    /// tool, which live one level higher. Only used to group agents of one run into a single row.
    /// </summary>
    public string? WorkflowId { get; init; }

    public double Utilization
    {
        get
        {
            var effective = ModelContextLimits.GetEffectiveMaxTokens(MaxTokens);
            return effective > 0 ? Math.Clamp((double)TotalTokens / effective, 0.0, 1.0) : 0.0;
        }
    }
}

/// <summary>
/// Context window state for a session, including per-subagent breakdowns.
/// </summary>
public record ContextWindowData
{
    public static readonly ContextWindowData Empty = new()
    {
        TotalTokens = 0,
        MaxTokens = ModelContextLimits.DefaultContextLimit,
        ModelName = null,
        ShouldWarnAutocompact = false,
        Subagents = []
    };

    public long TotalTokens { get; init; }
    public long MaxTokens { get; init; }
    public string? ModelName { get; init; }
    public bool ShouldWarnAutocompact { get; init; }
    public IReadOnlyList<SubagentContextData> Subagents { get; init; } = [];

    public double Utilization
    {
        get
        {
            var effective = ModelContextLimits.GetEffectiveMaxTokens(MaxTokens);
            return effective > 0 ? Math.Clamp((double)TotalTokens / effective, 0.0, 1.0) : 0.0;
        }
    }
}

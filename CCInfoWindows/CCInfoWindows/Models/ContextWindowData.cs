using CCInfoWindows.Helpers;

namespace CCInfoWindows.Models;

/// <summary>
/// One phase of a workflow run, out of the `phases` array of its script's `export const meta` block.
/// <paramref name="Detail"/> is optional — meta entries are allowed to carry a title only.
///
/// Windows-only, like everything else reachable from <see cref="SubagentContextData.WorkflowId"/>.
/// </summary>
public record WorkflowPhase(string Title, string? Detail);

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
    ///
    /// Windows-only — this is the authoritative note for the whole workflow display feature.
    ///
    /// Upstream ccInfo for macOS (stefanlange/ccInfo) has NO workflow display of any kind: no row,
    /// no gear glyph, no aggregation, no tooltip. `spec/` (v1.7.1 … v1.11.1) contains not one hit
    /// for "workflow", and the subagent requirements FA-042 / FA-062 know only the flat Agent-tool
    /// path. Workflows are a newer Claude Code feature than the reference app.
    ///
    /// So everything reachable from this field is an extension BEYOND parity, not a catch-up. If
    /// upstream ever ships its own workflow view, that is a collision with decisions already taken
    /// here — compare the two designs first, do not blindly align. Decisions D-1…D-27 and their
    /// measurements: `.planning/milestones/v1.7-ROADMAP.md`.
    ///
    /// Most sites carrying this feature repeat the marker `Windows-only` and point back here —
    /// JsonlService (discovery + journal), MainViewModel (row and label composition), MainView.xaml
    /// (the row template), the WorkflowSubagent* resw keys — but grep it as a starting point, not
    /// as a complete list.
    /// </summary>
    public string? WorkflowId { get; init; }

    /// <summary>
    /// Agents spawned and finished in this agent's workflow run, counted from the run's
    /// journal.jsonl. A run-level fact carried redundantly on every agent of the run: the display
    /// groups by <see cref="WorkflowId"/> and reads it off the first member, which keeps the
    /// transport out of <see cref="ContextWindowData"/>. Zero for plain Agent-tool subagents and for
    /// runs with no journal (older runs, other harness versions) — the row then shows tokens only
    /// rather than a guessed count.
    ///
    /// RunAgentsStarted is what has been spawned SO FAR, not a planned total, so it grows during a
    /// run whose fan-out is decided in stages (3/8, later 9/29). That matches what the Workflow tool
    /// itself reports and is intended, not a defect.
    /// </summary>
    public int RunAgentsStarted { get; init; }

    /// <inheritdoc cref="RunAgentsStarted"/>
    public int RunAgentsDone { get; init; }

    /// <summary>
    /// When this agent's workflow run started, for the row's tooltip. Run-level and carried
    /// redundantly like the counts above. Taken from the run directory's creation time, which is
    /// measured 2-4 s after the real start. <c>default</c> for plain Agent-tool subagents.
    ///
    /// The run's completed-run JSON carries an exact startTime, and it is deliberately not read: that
    /// file is written at run COMPLETION, and a completed run stops writing, so the 30-second
    /// staleness gate has already removed its row before the file exists.
    /// </summary>
    public DateTimeOffset RunStartedUtc { get; init; }

    /// <summary>
    /// Workflow name and one-line summary, sanitised and length-capped — free text out of a
    /// user-written script, so control characters are stripped before they can break the tooltip's
    /// line layout. Null for plain subagents and for runs whose script cannot be read; the tooltip
    /// drops those lines rather than showing a placeholder.
    ///
    /// Read from the run's SCRIPT (workflows/scripts/*-{runId}.js), which exists from the moment the
    /// run is created. That is the only source — see <see cref="RunStartedUtc"/> for why the
    /// completed-run JSON, which carries the same fields, is never consulted.
    /// </summary>
    public string? RunName { get; init; }

    /// <inheritdoc cref="RunName"/>
    public string? RunDescription { get; init; }

    /// <summary>
    /// The run's declared phases, in script order. Empty — never null — for plain subagents and for
    /// runs whose script has no readable `phases` array; the tooltip then omits the phase table
    /// entirely. Same source and sanitisation as <see cref="RunName"/>.
    /// </summary>
    public IReadOnlyList<WorkflowPhase> RunPhases { get; init; } = [];

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

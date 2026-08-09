using CCInfoWindows.Models;
using CCInfoWindows.ViewModels;
using Microsoft.UI.Xaml.Media;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Aggregation tests for MainViewModel.BuildSubagentRows (v1.7). The method is static so these
/// run without constructing a ViewModel: the two delegates keep the WinRT brush type and the
/// localizer out of the test path.
/// </summary>
public class MainViewModelSubagentRowTests
{
    private const long MaxTokens = 200_000;

    // Headless seams: SolidColorBrush needs WinRT COM, the localizer needs an initialized app.
    private static readonly Func<string, SolidColorBrush> NullBrush = _ => null!;
    private static readonly Func<string, int, string> TestLabel = (id, count) => $"{id}|{count}";

    // -------------------------------------------------------------------------
    // D-1: agents of one run collapse into a single row
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSubagentRows_TwelveWorkflowAgents_ProducesOneRowWithCount()
    {
        var agents = Enumerable.Range(0, 12)
            .Select(i => Agent($"a{i}", tokens: 10_000, workflowId: "wf_run-1"))
            .ToList();

        var rows = MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel);

        var row = Assert.Single(rows);
        Assert.True(row.IsWorkflow);
        Assert.Equal("wf_run-1|12", row.Label);
    }

    // -------------------------------------------------------------------------
    // D-2: the percentage is the MAXIMUM of the group, never the average
    // -------------------------------------------------------------------------

    /// <summary>
    /// The scenario the decision was made for: 20 agents idling near zero while one runs into its
    /// autocompact. Averaging would report a reassuring single-digit number for a group that has an
    /// agent about to be truncated.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowGroup_UsesMaximumNotAverage()
    {
        var agents = Enumerable.Range(0, 20)
            .Select(i => Agent($"low{i}", tokens: 5_000, workflowId: "wf_run-1"))
            .Append(Agent("high", tokens: 160_000, workflowId: "wf_run-1"))
            .ToList();

        var expectedMax = agents.Max(a => a.Utilization);
        var average = agents.Average(a => a.Utilization);

        var row = Assert.Single(MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel));

        Assert.Equal(expectedMax, row.Utilization, precision: 6);
        Assert.True(row.Utilization > average, "row must report the maximum, not the average");
    }

    // -------------------------------------------------------------------------
    // D-5: one row per run, run id taken verbatim
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSubagentRows_TwoConcurrentRuns_ProducesTwoRowsWithVerbatimIds()
    {
        List<SubagentContextData> agents =
        [
            Agent("b", tokens: 10_000, workflowId: "wf_11f45d5b-27d"),
            Agent("a", tokens: 20_000, workflowId: "wf_11f45d5b-27d"),
            Agent("c", tokens: 30_000, workflowId: "wf_99aabbcc-01")
        ];

        var rows = MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["wf_11f45d5b-27d", "wf_99aabbcc-01"], rows.Select(r => r.AgentId));
        Assert.Equal("wf_11f45d5b-27d|2", rows[0].Label);
        Assert.Equal("wf_99aabbcc-01|1", rows[1].Label);
    }

    // -------------------------------------------------------------------------
    // Ordering: plain subagents first, workflow rows after, both deterministic
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSubagentRows_MixedSession_PutsPlainSubagentsFirst()
    {
        List<SubagentContextData> agents =
        [
            Agent("zulu", tokens: 10_000, workflowId: "wf_zzz"),
            Agent("alpha", tokens: 10_000),
            Agent("bravo", tokens: 10_000)
        ];

        var rows = MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel);

        Assert.Equal(["alpha", "bravo", "wf_zzz"], rows.Select(r => r.AgentId));
        Assert.Equal([false, false, true], rows.Select(r => r.IsWorkflow));
    }

    /// <summary>
    /// Plain rows keep the icon and the model badge they always had; only workflow rows swap the
    /// glyph and drop the badge (D-3), so a regression here would be visible but silent.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_PlainSubagent_KeepsArrowIconAndEmptyLabel()
    {
        var rows = MainViewModel.BuildSubagentRows([Agent("alpha", tokens: 10_000)], NullBrush, TestLabel);

        var row = Assert.Single(rows);
        Assert.False(row.IsWorkflow);
        Assert.Equal("↳", row.Icon);
        Assert.Equal(string.Empty, row.Label);
    }

    [Fact]
    public void BuildSubagentRows_WorkflowRow_UsesGearIcon()
    {
        var rows = MainViewModel.BuildSubagentRows(
            [Agent("a", tokens: 10_000, workflowId: "wf_run-1")], NullBrush, TestLabel);

        Assert.Equal("⚙", Assert.Single(rows).Icon);
    }

    // -------------------------------------------------------------------------
    // Empty input
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSubagentRows_NoSubagents_ProducesNoRows()
    {
        Assert.Empty(MainViewModel.BuildSubagentRows([], NullBrush, TestLabel));
    }

    private static SubagentContextData Agent(string agentId, long tokens, string? workflowId = null) => new()
    {
        AgentId = agentId,
        TotalTokens = tokens,
        MaxTokens = MaxTokens,
        ModelName = "claude-sonnet-4-20250514",
        LastActivity = DateTimeOffset.UtcNow,
        WorkflowId = workflowId
    };
}

using System.Globalization;
using CCInfoWindows.Helpers;
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

    // Stands in for FormatWorkflowRow, which needs the localizer. Both texts come from one call, so
    // the fake proves the aggregated facts reach label and tooltip alike.
    private static readonly Func<WorkflowRowFacts, (string Label, WorkflowTooltipData Tooltip)> TestLabel =
        f => ($"{f.RunId}|{f.AgentsDone}/{f.AgentsStarted}|{f.TotalTokens}",
              new WorkflowTooltipData(
                  [new WorkflowTooltipLine("tip:", $"{f.RunId}|{f.StartedUtc:O}|{f.Name}|{f.Description}")],
                  $"phases:{f.Phases.Count}",
                  f.Phases.Select(p => new WorkflowPhaseRow(p.Title, p.Detail)).ToList()));

    // -------------------------------------------------------------------------
    // D-1: agents of one run collapse into a single row
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSubagentRows_TwelveWorkflowAgents_ProducesOneRow()
    {
        var agents = Enumerable.Range(0, 12)
            .Select(i => Agent($"a{i}", tokens: 10_000, workflowId: "wf_run-1", runAgentsStarted: 12, runAgentsDone: 4))
            .ToList();

        var rows = MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel);

        var row = Assert.Single(rows);
        Assert.True(row.IsWorkflow);
        Assert.Equal("wf_run-1|4/12|120000", row.Label);
    }

    // -------------------------------------------------------------------------
    // A workflow row carries no percentage at all — it reports extensive quantities
    // -------------------------------------------------------------------------

    /// <summary>
    /// Utilization is a ratio against a per-agent ceiling. Over a group that ceiling does not exist
    /// (21 agents, 21 windows), so maximum, mean and sum alike produce a number with no reference
    /// quantity — which is why the bar and the percentage left the workflow row entirely. Anything
    /// re-deriving a group percentage would have to pick one of two wrong answers again.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowGroup_ReportsNoPercentage()
    {
        var agents = Enumerable.Range(0, 20)
            .Select(i => Agent($"low{i}", tokens: 5_000, workflowId: "wf_run-1"))
            .Append(Agent("high", tokens: 160_000, workflowId: "wf_run-1"))
            .ToList();

        var row = Assert.Single(MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel));

        Assert.Equal(0, row.Utilization);
        Assert.Equal(0, row.Percentage);
        Assert.Equal(string.Empty, row.PercentageText);
    }

    /// <summary>
    /// The token figure is a SUM, the one aggregate that adds up cleanly over a run. A maximum or a
    /// mean here would silently under-report a fan-out by the agent count.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowGroup_SumsTokensAcrossAgents()
    {
        List<SubagentContextData> agents =
        [
            Agent("a", tokens: 5_000, workflowId: "wf_run-1"),
            Agent("b", tokens: 160_000, workflowId: "wf_run-1"),
            Agent("c", tokens: 35_000, workflowId: "wf_run-1")
        ];

        var row = Assert.Single(MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel));

        Assert.Equal("wf_run-1|0/0|200000", row.Label);
    }

    /// <summary>
    /// The run-level counts reach the label. They are carried redundantly on every agent of the run;
    /// Max is what makes a list whose members disagree report the highest, not the first.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowGroup_PassesRunProgressToLabel()
    {
        List<SubagentContextData> agents =
        [
            Agent("a", tokens: 1_000, workflowId: "wf_run-1", runAgentsStarted: 30, runAgentsDone: 29),
            Agent("b", tokens: 2_000, workflowId: "wf_run-1", runAgentsStarted: 30, runAgentsDone: 29)
        ];

        var row = Assert.Single(MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel));

        Assert.Equal("wf_run-1|29/30|3000", row.Label);
    }

    /// <summary>
    /// The badge is absent on a workflow row: the agents of one run can be on different models, so
    /// there is no single badge to show, and the template collapses it.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowRow_CarriesNoModelBadge()
    {
        var row = Assert.Single(MainViewModel.BuildSubagentRows(
            [Agent("a", tokens: 10_000, workflowId: "wf_run-1")], NullBrush, TestLabel));

        Assert.Equal(string.Empty, row.ModelBadge);
        Assert.Null(row.BadgeColor);
    }

    // -------------------------------------------------------------------------
    // D-5: one row per run, run id taken verbatim
    // -------------------------------------------------------------------------

    /// <summary>
    /// Also the guard that two runs never pool their counts or their token sums — each row is
    /// exactly one run.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_TwoConcurrentRuns_ProducesTwoRowsWithVerbatimIds()
    {
        List<SubagentContextData> agents =
        [
            Agent("b", tokens: 10_000, workflowId: "wf_11f45d5b-27d", runAgentsStarted: 8, runAgentsDone: 3),
            Agent("a", tokens: 20_000, workflowId: "wf_11f45d5b-27d", runAgentsStarted: 8, runAgentsDone: 3),
            Agent("c", tokens: 30_000, workflowId: "wf_99aabbcc-01", runAgentsStarted: 2, runAgentsDone: 2)
        ];

        var rows = MainViewModel.BuildSubagentRows(agents, NullBrush, TestLabel);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["wf_11f45d5b-27d", "wf_99aabbcc-01"], rows.Select(r => r.AgentId));
        Assert.Equal("wf_11f45d5b-27d|3/8|30000", rows[0].Label);
        Assert.Equal("wf_99aabbcc-01|2/2|30000", rows[1].Label);
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
        Assert.Equal("\u21B3", row.Icon);
        Assert.Equal(string.Empty, row.Label);
    }

    [Fact]
    public void BuildSubagentRows_WorkflowRow_UsesGearIcon()
    {
        var rows = MainViewModel.BuildSubagentRows(
            [Agent("a", tokens: 10_000, workflowId: "wf_run-1")], NullBrush, TestLabel);

        Assert.Equal("\u2699", Assert.Single(rows).Icon);
    }

    // -------------------------------------------------------------------------
    // Empty input
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSubagentRows_NoSubagents_ProducesNoRows()
    {
        Assert.Empty(MainViewModel.BuildSubagentRows([], NullBrush, TestLabel));
    }

    // -------------------------------------------------------------------------
    // D-12/D-17: the hover tooltip carries what the trimmed row cannot
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSubagentRows_PlainSubagent_CarriesNoTooltip()
    {
        var row = Assert.Single(MainViewModel.BuildSubagentRows([Agent("alpha", tokens: 10_000)], NullBrush, TestLabel));

        Assert.Null(row.Tooltip);
    }

    /// <summary>
    /// The run metadata reaches the formatter. Without this the tooltip would silently fall back to
    /// its degraded form even for a finished run whose name and description are on disk.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowRow_PassesRunMetadataToTheFormatter()
    {
        var started = new DateTimeOffset(2026, 8, 9, 13, 19, 44, TimeSpan.Zero);
        var row = Assert.Single(MainViewModel.BuildSubagentRows(
            [Agent("a", tokens: 10_000, workflowId: "wf_x", startedUtc: started, name: "review", description: "does things")],
            NullBrush,
            TestLabel));

        Assert.Equal($"tip:wf_x|{started:O}|review|does things", Line(row.Tooltip!.Lines[0]));
    }

    /// <summary>
    /// The phases reach the formatter too. They come from a different file than name and description
    /// (the run script, not the completed-run JSON), so a break here would be invisible to every
    /// other metadata assertion.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowRow_PassesRunPhasesToTheFormatter()
    {
        var row = Assert.Single(MainViewModel.BuildSubagentRows(
            [Agent("a", tokens: 10_000, workflowId: "wf_x", phases: [new("Detect", "9 detectors"), new("Verify", null)])],
            NullBrush,
            TestLabel));

        Assert.Equal("phases:2", row.Tooltip!.PhasesCaption);
        Assert.Equal([("Detect", "9 detectors"), ("Verify", null)],
            row.Tooltip.Phases.Select(p => (p.Title, p.Detail)));
    }

    /// <summary>
    /// The run's phases are carried by every agent of the run. A hand-built list whose first member
    /// read the script before it existed must not blank the table for the whole run.
    /// </summary>
    [Fact]
    public void BuildSubagentRows_WorkflowRow_TakesPhasesFromTheFirstAgentThatHasThem()
    {
        var row = Assert.Single(MainViewModel.BuildSubagentRows(
            [
                Agent("a", tokens: 1_000, workflowId: "wf_x"),
                Agent("b", tokens: 1_000, workflowId: "wf_x", phases: [new("Detect", null)])
            ],
            NullBrush,
            TestLabel));

        Assert.Equal("Detect", Assert.Single(row.Tooltip!.Phases).Title);
    }

    [Fact]
    public void FormatWorkflowTooltip_CarriesIdCountsAndTokens()
    {
        var text = string.Join('\n', TextLines(MainViewModel.FormatWorkflowTooltip(
            Facts(name: "review-v16-to-v17", description: "Multi-dimensional review"), Localize, Pattern, German)));

        Assert.Contains("wf_eda3abc2-8c9", text);
        Assert.Contains("31/31", text);
        Assert.Contains("3.3M", text);
        Assert.Contains("review-v16-to-v17", text);
        Assert.Contains("Multi-dimensional review", text);
    }

    // -------------------------------------------------------------------------
    // The phase table
    // -------------------------------------------------------------------------

    /// <summary>
    /// The rows carry no numbering, but the caption still carries the count — a numberless table
    /// cannot be counted at a glance, and "how many phases" is the one thing the caption is for.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_KeepsThePhaseCountOnTheCaption()
    {
        var tooltip = MainViewModel.FormatWorkflowTooltip(
            Facts(phases: [new("Detect", "d"), new("Verify", "v"), new("Report", null)]), Localize, Pattern, German);

        Assert.Equal(["Detect", "Verify", "Report"], tooltip.Phases.Select(p => p.Title));
        Assert.Equal("Phasen (3)", tooltip.PhasesCaption);
        Assert.True(tooltip.HasPhases);
    }

    /// <summary>
    /// A phase entry is allowed to carry a title only — meta blocks in the wild do. The detail
    /// column then stays empty instead of the row vanishing.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_KeepsPhasesWithoutADetail()
    {
        var tooltip = MainViewModel.FormatWorkflowTooltip(
            Facts(phases: [new("Scan", null)]), Localize, Pattern, German);

        Assert.Null(Assert.Single(tooltip.Phases).Detail);
    }

    /// <summary>
    /// No script, no phases: the caption is empty and HasPhases collapses the whole block, so the
    /// tooltip shows no orphaned "Phasen (0)" heading over an empty table.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_WithoutPhases_CollapsesTheTable()
    {
        var tooltip = MainViewModel.FormatWorkflowTooltip(Facts(), Localize, Pattern, German);

        Assert.Empty(tooltip.Phases);
        Assert.Equal(string.Empty, tooltip.PhasesCaption);
        Assert.False(tooltip.HasPhases);
    }

    /// <summary>
    /// D-17: no bare value anywhere. The first line is the type and is its own label; every other
    /// line has to name what it shows, because a tooltip is read in isolation.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_LabelsEveryValueLine()
    {
        var lines = TextLines(MainViewModel
            .FormatWorkflowTooltip(Facts(name: "n", description: "d"), Localize, Pattern, German));

        Assert.Equal(7, lines.Length);
        Assert.Equal("Workflow", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.Contains(": ", line));
    }

    // -------------------------------------------------------------------------
    // Label / value split — the label half is greyed out, the value half is not
    // -------------------------------------------------------------------------

    /// <summary>
    /// The label keeps its trailing space: the template renders the halves as two adjacent Runs, so
    /// a trimmed label would glue "Name:" to its value.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_SplitsEachLineIntoALabelAndAValue()
    {
        var tooltip = MainViewModel.FormatWorkflowTooltip(
            Facts(name: "code-clone-review", description: "d"), Localize, Pattern, German);

        var name = tooltip.Lines.Single(l => l.Label.StartsWith("Name", StringComparison.Ordinal));
        var id = tooltip.Lines.Single(l => l.Label.StartsWith("ID", StringComparison.Ordinal));

        Assert.Equal(("Name: ", "code-clone-review"), (name.Label, name.Value));
        Assert.Equal(("ID: ", "wf_eda3abc2-8c9"), (id.Label, id.Value));
    }

    /// <summary>
    /// Order is part of the contract, not an accident of how the list is built: the fixed-width
    /// facts come first so they never shift, and the two that wrap — and that a script-less run does
    /// not have at all — sit at the end.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_PutsNameAndDescriptionAfterTheMeasurements()
    {
        var labels = MainViewModel
            .FormatWorkflowTooltip(Facts(name: "n", description: "d"), Localize, Pattern, German)
            .Lines.Select(l => l.Label.TrimEnd(' ', ':'));

        Assert.Equal(["Workflow", "ID", "Agents", "Start", "Kontext", "Name", "Beschreibung"], labels);
    }

    /// <summary>
    /// "Agents: {0}/{1} fertig" puts the value in the MIDDLE of the sentence. The split is at the
    /// first placeholder, so the trailing word travels with the value rather than being lost or
    /// landing in the label.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_TemplateWithATrailingWord_KeepsItWithTheValue()
    {
        var agents = MainViewModel
            .FormatWorkflowTooltip(Facts(), Localize, Pattern, German)
            .Lines.Single(l => l.Label.StartsWith("Agents", StringComparison.Ordinal));

        Assert.Equal("Agents: ", agents.Label);
        Assert.Equal("31/31 fertig", agents.Value);
    }

    /// <summary>
    /// A template with no placeholder is all label and no value — right for the leading type line,
    /// which is a heading rather than a labelled value.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_TypeLine_IsAllLabelAndNoValue()
    {
        var kind = MainViewModel.FormatWorkflowTooltip(Facts(), Localize, Pattern, German).Lines[0];

        Assert.Equal("Workflow", kind.Label);
        Assert.Equal(string.Empty, kind.Value);
    }

    /// <summary>
    /// Every value line has a non-empty label, or the grey/white contrast that carries the tooltip's
    /// structure silently disappears for that line.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_EveryLineCarriesALabel()
    {
        var tooltip = MainViewModel.FormatWorkflowTooltip(
            Facts(name: "n", description: "d"), Localize, Pattern, German);

        Assert.All(tooltip.Lines, l => Assert.NotEmpty(l.Label));
    }

    /// <summary>
    /// The degraded form is the NORMAL case, not an edge case: the metadata file is written when the
    /// run completes, so a live run has no name and no description at all. Exactly the two lines go,
    /// and nothing else shifts.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_WithoutMetadata_DropsExactlyTheNameAndDescriptionLines()
    {
        var full = TextLines(MainViewModel.FormatWorkflowTooltip(Facts(name: "n", description: "d"), Localize, Pattern, German));
        var degraded = TextLines(MainViewModel.FormatWorkflowTooltip(Facts(), Localize, Pattern, German));

        Assert.Equal(5, degraded.Length);
        Assert.Equal(full.Where(l => !l.StartsWith("Name:") && !l.StartsWith("Beschreibung:")), degraded);
    }

    /// <summary>
    /// A run with no journal.jsonl has no agent count. Dropping the line beats rendering "0/0".
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_WithoutJournal_DropsTheAgentLine()
    {
        var lines = TextLines(MainViewModel
            .FormatWorkflowTooltip(Facts() with { AgentsStarted = 0, AgentsDone = 0 }, Localize, Pattern, German));

        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain(lines, l => l.StartsWith("Agents:"));
    }

    /// <summary>
    /// The start line's layout comes from the active language's resw pattern, same as the
    /// next-window label — not from a culture check in code.
    /// </summary>
    [Fact]
    public void FormatWorkflowTooltip_LetsThePatternDecideTheStartTimeLayout()
    {
        // Asserted structurally, not against a literal time: the value is converted to local time,
        // so a literal would encode the machine's zone and fail everywhere else.
        var timeOnly = StartLineOf(MainViewModel.FormatWorkflowTooltip(Facts(), Localize, "HH:mm", German));
        var withDate = StartLineOf(MainViewModel.FormatWorkflowTooltip(Facts(), Localize, "ddd dd.MM., HH:mm", German));

        Assert.DoesNotContain(".", timeOnly);
        Assert.Contains(".", withDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatWorkflowTooltip_FallsBackToACultureDerivedPattern_WhenTheDictionaryCannotAnswer(string? pattern)
    {
        // An unbuilt localizer echoes the uid back, a built one returns empty for an unknown uid.
        // Handing either to ToString would silently produce DateTime's general format.
        var localTime = RunStart.LocalDateTime;

        var startLine = StartLineOf(MainViewModel.FormatWorkflowTooltip(Facts(), Localize, pattern, German));

        Assert.NotEqual($"Start: {localTime.ToString(German)}", startLine);
        Assert.Equal(
            $"Start: {localTime.ToString(CountdownFormatter.CultureDefaultPattern(German), German)}",
            startLine);
    }

    // -------------------------------------------------------------------------
    // G-2: rows of a run that stopped writing are retired on the countdown tick
    // -------------------------------------------------------------------------

    /// <summary>
    /// The defect itself: nothing repaints after a run's last write, because that write comes from
    /// the run's own agents, so the row outlives the run indefinitely.
    /// </summary>
    [Fact]
    public void RetireStaleRows_DropsARunThatStoppedWritingLongAgo()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = MainViewModel.BuildSubagentRows(
            [Agent("a1", 10_000, workflowId: "wf_done", runAgentsStarted: 4, runAgentsDone: 4,
                   lastActivity: now - TimeSpan.FromHours(2))],
            NullBrush,
            TestLabel);

        MainViewModel.RetireStaleRows(rows, now);

        Assert.Empty(rows);
    }

    /// <summary>
    /// The reason the window is ten minutes and not the service's 30 s. Measured on the real
    /// 43-agent run: 26 % of its runtime had NO agent fresh within 30 s, and one agent went 474 s
    /// without a write inside a single model call. The 30 s gate is calibrated for write-triggered
    /// sampling, where a repaint proves an agent just wrote; sampled on a clock it would delete the
    /// row of a run that is still going. Shrink SubagentRetirementWindow below ~8 min and this
    /// fails.
    /// </summary>
    [Fact]
    public void RetireStaleRows_KeepsALiveRunAcrossTheLongestMeasuredWriteGap()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = MainViewModel.BuildSubagentRows(
            [Agent("straggler", 10_000, workflowId: "wf_live", runAgentsStarted: 43, runAgentsDone: 42,
                   lastActivity: now - TimeSpan.FromSeconds(474))],
            NullBrush,
            TestLabel);

        MainViewModel.RetireStaleRows(rows, now);

        Assert.Single(rows);
    }

    /// <summary>
    /// A run is retired on its NEWEST agent, mirroring the service's per-run gate: one agent still
    /// writing keeps the whole run on screen, however long its finished siblings have been quiet.
    /// </summary>
    [Fact]
    public void RetireStaleRows_KeepsARunWhoseFinishedAgentsAreStaleButOneStillWrites()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = MainViewModel.BuildSubagentRows(
            [
                Agent("finished", 10_000, workflowId: "wf_mixed", runAgentsStarted: 2, runAgentsDone: 1,
                      lastActivity: now - TimeSpan.FromHours(3)),
                Agent("writing", 20_000, workflowId: "wf_mixed", runAgentsStarted: 2, runAgentsDone: 1,
                      lastActivity: now)
            ],
            NullBrush,
            TestLabel);

        MainViewModel.RetireStaleRows(rows, now);

        Assert.Single(rows);
    }

    /// <summary>
    /// Plain Agent-tool rows freeze for the same reason and are retired the same way — and the
    /// stale one must not take its live neighbour with it.
    /// </summary>
    [Fact]
    public void RetireStaleRows_RetiresPlainRowsIndividually()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = MainViewModel.BuildSubagentRows(
            [
                Agent("stale", 10_000, lastActivity: now - TimeSpan.FromHours(1)),
                Agent("live", 20_000, lastActivity: now)
            ],
            NullBrush,
            TestLabel);

        MainViewModel.RetireStaleRows(rows, now);

        Assert.Equal("live", Assert.Single(rows).AgentId);
    }

    private static string StartLineOf(WorkflowTooltipData tooltip) =>
        TextLines(tooltip).Single(l => l.StartsWith("Start:", StringComparison.Ordinal));

    /// <summary>
    /// The tooltip's fact lines as rendered text — label and value concatenated, which is what the
    /// two adjacent Runs produce on screen. The phase table is deliberately not in here: it is a
    /// Grid, not text, and the line-structure assertions above are about the text.
    /// </summary>
    private static string[] TextLines(WorkflowTooltipData tooltip) =>
        tooltip.Lines.Select(Line).ToArray();

    private static string Line(WorkflowTooltipLine line) => line.Label + line.Value;

    // Real de-DE resw values — the point of the assertions above is the line structure the code
    // builds, so the templates have to be the real ones. An unexpected key fails loudly rather than
    // returning something plausible.
    private static string Localize(string key) => key switch
    {
        "WorkflowTooltipKind" => "Workflow",
        "WorkflowTooltipName" => "Name: {0}",
        "WorkflowTooltipDescription" => "Beschreibung: {0}",
        "WorkflowTooltipId" => "ID: {0}",
        "WorkflowTooltipAgents" => "Agents: {0}/{1} fertig",
        "WorkflowTooltipStart" => "Start: {0}",
        "WorkflowTooltipContext" => "Kontext: {0} Tokens",
        "WorkflowTooltipPhases" => "Phasen ({0})",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "unexpected tooltip resource key")
    };

    private const string Pattern = "ddd dd.MM., HH:mm";
    private static readonly CultureInfo German = new("de-DE");

    // 2026-08-09T13:19:44Z — the real start of wf_eda3abc2-8c9, 15:19 in this machine's local time.
    private static readonly DateTimeOffset RunStart = new(2026, 8, 9, 13, 19, 44, TimeSpan.Zero);

    private static WorkflowRowFacts Facts(
        string? name = null,
        string? description = null,
        IReadOnlyList<WorkflowPhase>? phases = null) =>
        new("wf_eda3abc2-8c9", 31, 31, 3_252_640, RunStart, name, description, phases ?? []);

    private static SubagentContextData Agent(
        string agentId,
        long tokens,
        string? workflowId = null,
        int runAgentsStarted = 0,
        int runAgentsDone = 0,
        DateTimeOffset startedUtc = default,
        string? name = null,
        string? description = null,
        IReadOnlyList<WorkflowPhase>? phases = null,
        DateTimeOffset? lastActivity = null) => new()
    {
        AgentId = agentId,
        TotalTokens = tokens,
        MaxTokens = MaxTokens,
        ModelName = "claude-sonnet-4-20250514",
        LastActivity = lastActivity ?? DateTimeOffset.UtcNow,
        WorkflowId = workflowId,
        RunAgentsStarted = runAgentsStarted,
        RunAgentsDone = runAgentsDone,
        RunStartedUtc = startedUtc,
        RunName = name,
        RunDescription = description,
        RunPhases = phases ?? []
    };
}

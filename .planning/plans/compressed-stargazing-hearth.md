# Context breakdown line under the context bar

## Context

`/context` in Claude Code shows the context usage split into 9 categories. The JSONL transcripts
do not carry that split, so the app can show only what it can measure itself. The user has
chosen variant A from the mockup canvas (https://claude.ai/artifact/Mup1ADhW4JHwrCceUYMtMM):

- **one** line directly under the existing context ProgressBar, above the percentage/badge row
- the bar stays as it is: single colour, no segments
- `Basis 70.3K · Chat 33.9K · Puffer 33.0K · Frei 62.8K`
- 12 px (= `QuotaResetDateStyle` size), **whole line in `SecondaryTextBrush`** (the lighter grey of
  variant B: #8E8E93 dark / #6E6E73 light). The labels are not dimmer than the values.

### What the four values mean

| Label | Computation | Source |
|---|---|---|
| Basis | context tokens of the session's **first** assistant entry | file head, memoized |
| Chat | `max(0, TotalTokens − Basis)` | derived |
| Puffer | `min(StandardAutocompactBuffer, MaxTokens)` (33,000) | `ModelContextLimits` |
| Frei | `max(0, MaxTokens − TotalTokens − Puffer)` | derived |

Basis + Chat + Puffer + Frei = MaxTokens, except where a clamp applies.

**Known inaccuracy, accepted:** Basis contains the first user prompt, because the first request
caches system prompt, tools, memory, skills **and** that prompt together (measured: 70,267 for this
session, including a screenshot). The transcript gives no clean way to separate them. Memory is
not shown separately, as the user asked.

**Basis survives `/compact`.** System prompt, tools and memory do not change when the conversation
is compacted. So the value from the head of the file stays valid. After a compact, Chat can drop
below 0, and the clamp to 0 handles that.

## Implementation

### 1. Model — `Models/ContextWindowData.cs`
- New `long BaselineTokens { get; init; }` on `ContextWindowData` (0 = unknown). `Empty` stays at 0.
- Computed properties next to `Utilization`, so view model and tests share one formula:
  `ChatTokens`, `BufferTokens`, `FreeTokens` (with the clamps from the table above).
  `BufferTokens` uses `ModelContextLimits.StandardAutocompactBuffer`. It is the same constant
  `GetEffectiveMaxTokens` already uses (`Helpers/ModelContextLimits.cs:25,109`), so the line and the
  percentage stay on the same scale.

### 2. Service — `Services/JsonlService.cs`
- New `ReadBaselineTokens(string sessionFile)`. It reads the file **from the start**, line by line
  with `FileShare.ReadWrite` like `ReadTailLines` (`:498`), and stops at the first entry that
  `IsRelevantAssistantEntry` accepts **and** that carries usage. It then returns
  `ComputeContextTokens(entry)` (`:1403`). The existing `ReadTailLines` only sees the last
  `TailWindowBytes` and misses the start of long sessions.
- Upper read limit (e.g. the first 2,000 lines or `TailWindowBytes`). If no assistant entry
  turns up within it, the result is 0: no line, no unbounded read.
- **Memoize** in a static `ConcurrentDictionary<string, long> BaselineCache`, keyed by the path,
  modelled on `AgentSnapshotCache` (`:1056`), including its "never evicted" note. The first assistant
  entry of a file never changes, so no mtime stamp is needed. Cache only values > 0. A session
  that has not answered yet is asked again on the next pass.
- `BuildContextWindow` (`:1277`) sets `BaselineTokens = ReadBaselineTokens(sessionFile)`. Error
  handling stays in the existing `try`/`HandleContextWindowFailure` around it.

### 3. ViewModel — `ViewModels/MainViewModel.cs`
- New `[ObservableProperty] private string _contextBreakdownText = string.Empty;` (G-3: no `null!`).
- Set in `PaintContextWindow` (`:1608`), the single enumeration of all KONTEXTFENSTER properties.
  That way the clear path (`hasSession: false`) resets the line automatically.
  Empty when `!hasSession || context.BaselineTokens == 0`.
- Format through a static, testable helper `FormatContextBreakdown(ContextWindowData, Func<string,string> localize)`,
  modelled on `FormatWorkflowRow` (`:1750ff.`). Numbers through `TokenFormatter.FormatTokenCount`.
  That gives `70.3K` and not the mockup's `70,3k`, so the format matches the existing workflow row
  (`3.3M Tokens`).
- New `[ObservableProperty] bool _hasContextBreakdown` or a check on `string.Empty` for the
  visibility, so an empty line does not take 8 px of StackPanel spacing.

### 4. Localization — `Strings/de-DE/Resources.resw`, `Strings/en-US/Resources.resw`
- One key `ContextBreakdownLabel`, **single-segment** (WinUI3Localizer rule, enforced by
  `ResourceCoverageTests`):
  - de: `Basis {0} · Chat {1} · Puffer {2} · Frei {3}`
  - en: `Base {0} · Chat {1} · Buffer {2} · Free {3}`

### 5. View — `Views/MainView.xaml` (between `:211` ProgressBar and `:213` percentage row)
```xml
<TextBlock Text="{x:Bind ViewModel.ContextBreakdownText, Mode=OneWay}"
           Visibility="{x:Bind ViewModel.HasContextBreakdown, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
           FontSize="12"
           Foreground="{ThemeResource SecondaryTextBrush}"
           TextTrimming="CharacterEllipsis"
           TextWrapping="NoWrap" />
```
`TextTrimming`: with 1M models the line gets wider ("Frei 884.6K"). At 360 DIP it ends with "…"
instead of being cut off hard. Plus a `ToolTipService.ToolTip` with the same text, so the cut-off
part stays readable.

## Tests (TDD, write first)

- `JsonlServiceTests`: baseline = first assistant entry even when the file is larger than
  `TailWindowBytes`. The first entry has no usage and the second does, so the second counts.
  A file with no assistant entry gives 0. A sidechain entry at the start does not count as the
  baseline.
- `ContextWindowData` tests: the sum equals MaxTokens, Chat is clamped to 0 when the total is below
  the baseline (post-compact), Frei is clamped to 0 in the warning zone.
- `MainViewModel`: `FormatContextBreakdown` with a fixed `localize` delegate. The clear path empties
  the line (check this against the existing regression test on `ClearSessionData`).
- `ResourceCoverageTests` picks up the new key automatically (de + en present, single segment).

## Verification

1. `dotnet test` — all green (currently 898).
2. `./dev.cmd`, active session: the line appears under the bar, and the values add up to the model
   limit. Compare Basis against `cache_creation_input_tokens` of the first assistant entry in the
   session JSONL.
3. Running `/context` in the same session as a plausibility check: Basis ≈ the sum of all
   categories except Messages and Free space, minus the first prompt.
4. Switch the theme between dark and light, switch the language between de and en (the line
   follows on the next poll).
5. Default width of 360 DIP: the line does not wrap, and with a 1M model it ends with "…".

## Housekeeping
- The working tree on `master` has uncommitted changes to `WorkflowScriptMeta*`. Leave them alone,
  work on the branch `feat/context-breakdown`, and commit only the files of this feature.
- Conventional commits (`test:`, `feat:`).

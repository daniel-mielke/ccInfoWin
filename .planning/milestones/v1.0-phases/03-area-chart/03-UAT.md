---
status: diagnosed
phase: 03-area-chart
source: 03-01-SUMMARY.md, 03-02-SUMMARY.md
started: 2026-03-11T12:20:00Z
updated: 2026-03-11T12:35:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Area Chart Visible on Dashboard
expected: The old ProgressBar for 5-hour usage is replaced by a Win2D canvas area chart. The chart area is visible with the dark theme background color (#2C2C2E).
result: issue
reported: "es wird nur ein grüner punkt (aktuell bei 42%) angezeigt. ich beobachte das area chart schon seit einiger zeit. die prozentzahl erhöht sich, der punkt in dem area chart steigt weiter nach oben und rechts (das ist korrekt), aber es wird kein flächendiagramm angezeigt und auch keine linie am oberen rand des flächendiagramm. ich vermute, dass immer nur der aktuelle wert aus der api-repsonse angezeigt wird. es fehlt die history, die lokal gspeichert werden muss"
severity: major

### 2. Threshold Lines at 50% and 100%
expected: Two horizontal dashed lines are drawn across the chart at the 50% and 100% utilization levels. These serve as visual reference thresholds.
result: pass

### 3. Y-Axis Labels (0%, 50%, 100%)
expected: The Y-axis displays percentage labels at 0%, 50%, and 100% positions along the left edge of the chart.
result: pass

### 4. Step Chart Rendering with Zone Colors
expected: As usage data accumulates (after at least one poll cycle), the chart draws a step-style area fill. Different utilization zones (low/medium/high) should use distinct colors with semi-transparent fills.
result: issue
reported: "nope, es wird keine Step-Style Flächenfüllung angezeigt"
severity: major

### 5. Glow Dot at Current Value
expected: A glowing dot (with a Gaussian blur halo effect) is displayed at the rightmost point of the chart, indicating the current utilization value.
result: pass

### 6. Chart Updates on Each Poll
expected: When the dashboard polls for new usage data (every polling interval), the chart redraws to include the new data point. The step chart extends to the right with the latest value.
result: pass

### 7. History Persists Across App Restart
expected: Close and reopen the app. The chart should immediately display the previously accumulated history data (loaded from disk) before the first API poll completes.
result: issue
reported: "nein, erst nach api-repsonse"
severity: major

### 8. History Resets on 5-Hour Window Change
expected: When the API returns a new ResetsAt timestamp (indicating a new 5-hour window), the old history is cleared and accumulation starts fresh. The chart should show only data from the current window.
result: skipped
reason: History nicht funktional, Reset-Verhalten kann nicht getestet werden

## Summary

total: 8
passed: 4
issues: 3
pending: 0
skipped: 1

## Gaps

- truth: "Area chart displays step-style area fill and top stroke line using accumulated history data"
  status: failed
  reason: "User reported: Only a green dot is shown (currently at 42%). The percentage increases and the dot moves up and right (correct), but no area fill and no top stroke line are drawn. Suspects only current API value is used, missing local history accumulation."
  severity: major
  test: 1
  root_cause: "DrawChartFills and DrawChartTopLine produce degenerate/empty paths when a segment has only 1 point. The for-loop in DrawChartTopLine (startIndex+1 to endIndex) is never entered for single-point segments. DrawChartFills creates a zero-width vertical line. Additionally, the step chart does not extend the last point horizontally to the current time (right edge), so even with multiple points the rightmost value has no visible plateau."
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs"
      issue: "DrawChartFills (lines ~141-185) and DrawChartTopLine (lines ~187-220) do not handle single-point segments and do not extend to right edge"
  missing:
    - "Handle single-point segments: draw horizontal bar from point to next segment start or right edge"
    - "After last data point, extend step horizontally to current time (right edge) for visible plateau"
    - "Ensure segment boundaries include connecting points in both adjacent segments"
  debug_session: ""

- truth: "Step chart area fills are drawn with zone-specific colors at 40% alpha per utilization zone"
  status: failed
  reason: "User reported: No step-style area fill is displayed"
  severity: major
  test: 4
  root_cause: "Same root cause as Test 1 — DrawChartFills produces degenerate geometry for single-point segments. This is a duplicate symptom of the drawing bug."
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs"
      issue: "DrawChartFills creates zero-area path for single-point zone segments"
  missing:
    - "Fix DrawChartFills to produce visible area for all segment sizes (see Test 1 fix)"
  debug_session: ""

- truth: "History loaded from disk on app start for instant chart display before first API poll"
  status: failed
  reason: "User reported: Chart only appears after first API response, not loaded from disk on startup"
  severity: major
  test: 7
  root_cause: "Two causes: (1) Even when history IS loaded from disk in InitializeAsync, the drawing bug (Test 1) prevents the loaded points from being visible. (2) If the 5-hour window has expired since last run, the first PollUsageAsync detects a new ResetsAt and immediately clears all loaded history, replacing it with a single point — which is then invisible due to the drawing bug."
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      issue: "AppendHistoryPoint clears history on ResetsAt change; InitializeAsync loads history but drawing bug makes it invisible"
  missing:
    - "Fix drawing bug (Test 1) so loaded history points are actually rendered"
    - "Optionally: on startup, check if ResetsAt is in the past and proactively clear stale history before displaying"
  debug_session: ""

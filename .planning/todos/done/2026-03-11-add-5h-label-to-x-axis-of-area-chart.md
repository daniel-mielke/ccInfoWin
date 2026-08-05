---
created: 2026-03-11T10:32:00Z
title: Add 5h label to x-axis of area chart
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs:114-135
---

## Problem

The 5-hour area chart is missing the "5h" label on the x-axis. The user expects a clear time range indicator showing "5h" on the horizontal axis to communicate the chart's time window at a glance.

The axis labels are drawn in `DrawAxesAndLabels()` in `MainView.xaml.cs`. Currently threshold labels (50%, 100%) exist on the y-axis, but the x-axis lacks the "5h" time designation.

## Solution

- Add a `session.DrawText("5h", ...)` call in `DrawAxesAndLabels()` positioned at the right end of the x-axis
- Use the existing `CanvasTextFormat` and chart label color from `ChartColors`
- Match font size and style with existing axis labels for visual consistency

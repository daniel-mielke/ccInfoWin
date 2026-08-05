---
created: 2026-03-11T10:28:00Z
title: Add filled area gradient to 5h chart
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs:141-185
  - CCInfoWindows/CCInfoWindows/Helpers/ChartRenderer.cs:38
---

## Problem

The 5-hour area chart currently shows only a single data point instead of a filled area chart. The colored gradient fill beneath the usage line is missing entirely. Expected behavior: the area below the usage line should be filled with a color gradient matching the utilization zones (green 0-50%, yellow 50-75%, orange 75-90%, red 90-100%).

The `DrawChartFills()` method in `MainView.xaml.cs` (line ~141) is responsible for drawing the colored area segments. `ChartRenderer.GetZoneSegments()` groups data points by color zone. Either there is insufficient historical data (only one point), or the fill geometry/path is not being rendered correctly.

## Solution

- Investigate whether `UsageHistoryService` is collecting enough data points over time (needs >1 point for area fill)
- Verify `DrawChartFills()` creates proper `CanvasPathBuilder` geometry with bottom-edge closure for fill
- Ensure gradient/zone colors from `ChartColors` are applied to each segment
- If single-point issue: may need to seed initial data or wait for multiple polling cycles

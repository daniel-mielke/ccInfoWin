---
created: 2026-03-11T10:25:39.642Z
title: Fix 5h chart dashed line thickness and add 0% line
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs:114-125
---

## Problem

In the 5-hour area chart, the dashed threshold lines have incorrect visual weights:
1. **50% line is too thick** — it appears twice as thick as the 100% line. The 50% line should be thinner (equal or less prominent than 100%).
2. **0% baseline line is missing** — there is no dashed line at the 0% threshold. It needs to be added for visual completeness.

The dashed lines are drawn in `MainView.xaml.cs` inside `DrawAxesAndLabels()` (lines ~124-125) using the `DashStrokeStyle` defined at lines 27-30.

## Solution

- Reduce stroke width of the 50% dashed line to be equal to or thinner than the 100% line
- Add a new `session.DrawLine()` call at y=0% using the same `DashStrokeStyle`

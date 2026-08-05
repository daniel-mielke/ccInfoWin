---
created: 2026-03-16T10:20:00Z
title: Filter inactive sessions from project dropdown
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Models/SessionDisplayItem.cs
---

## Problem

The session/project dropdown accumulates all projects ever used, including ones from days or weeks
ago that are no longer active. This makes the dropdown increasingly cluttered over time. Each entry
also has a green/gray activity dot that becomes meaningless when stale sessions are shown.

Only **active** sessions/projects should appear in the dropdown. Inactive ones should be removed
entirely (including their green/gray status indicator dot).

## Solution

- Define "active" criteria: e.g., session has activity within the last N hours (configurable via
  settings threshold, or a sensible default like 24h)
- Filter `SessionDisplayItem` list in `JsonlService` or `MainViewModel` to exclude inactive sessions
- Remove the green/gray dot for inactive sessions (they simply shouldn't appear)
- Consider: should the currently-selected session always remain visible even if inactive?
- The file watcher already updates sessions live — filtering should integrate with that flow
- May need a "show all" toggle or settings option for users who want to see historical sessions

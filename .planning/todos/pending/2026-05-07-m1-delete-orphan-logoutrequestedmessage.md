---
created: 2026-05-07
source: v1.4 code review
severity: major
area: Messages/LogoutRequestedMessage.cs
related_phase: 21-history-persistence-hardening
---

# M-1: Delete orphan `LogoutRequestedMessage.cs`

## Problem

`Messages/LogoutRequestedMessage.cs` is dead code from the reverted Plan 21-03. There is:
- Zero `Send(new LogoutRequestedMessage(...))` callsite anywhere in the codebase
- Zero `IRecipient<LogoutRequestedMessage>` declaration
- Zero `Register<LogoutRequestedMessage>` registration

The XML docstring on the class still describes the reverted architecture as if it were active ("Handled by MainViewModel as the single source of truth for logout"). This is actively misleading code — a developer reading it would believe the messenger pattern is in use.

## Why This Matters

CLAUDE.md is explicit: "Delete commented-out code — Git preserves history". Dead classes with documentation that contradicts reality are worse than commented-out code because they look alive. The clarifying comments in `MainViewModel.cs:52` and `SettingsViewModel.cs:247` explaining the revert are sufficient context — the file itself adds confusion, not value.

## Fix

```bash
git rm CCInfoWindows/CCInfoWindows/Messages/LogoutRequestedMessage.cs
```

Verify build still passes (it should — nothing references the type).

## Effort

XS — one file deletion + build verification.

## v1.5 Priority

Low effort, high cleanliness ROI. Do this opportunistically in any v1.5 PR that touches the `Messages/` folder, or as a standalone cleanup commit.

---
name: Backlog — Session dropdown empty on cold start (Cwd hydration + 30-day visibility window)
description: After cold start the "Active Session" dropdown is empty although JSONL files for recent projects exist on disk. Root cause is fragile Cwd hydration in JsonlService + over-restrictive IsValidProjectDirectory filter, not the 120-min activity threshold. v1.5 fix combines a hydration hardening pass with a configurable session-visibility window (default 30 days, configurable: 7d / 30d / 90d / unlimited).
type: project
originSessionId: 4fcfe4f9-d257-456b-bc4f-1109b37175ac
---
# Session dropdown: empty on cold start — root cause + v1.5 fix plan

**Reported:** 2026-05-07 by user during v1.4 UAT.
**Re-scoped:** 2026-05-07 after code investigation falsified the original "120-min hydration" hypothesis.

## Symptom

After app restart, the "Aktive Sitzung" / "Active Session" ComboBox is empty. User expects sessions from the last 30 days to appear, including those that ran days ago (computer was off in between).

User quote (DE):
> "wenn ich am abend an einem projekt (session) arbeite, dann den computer ausschalte, und drei tage später die app ccInfoWin wieder starte, sollten projekte aus der vergangenheit (inaktiv) im dropdown angezeigt werden. ich stelle mir das so vor, dass inaktive projekte, die nicht länger als 30 tage inaktiv waren, im dropdown als 'inaktive' sessions angezeigt werden."

## What was already in place (post-Phase 22 D-06)

- `MainViewModel.RefreshSessions` (cs:676-678) **already removed** the `.Where(s => s.IsActive(threshold))` filter.
- Inactive sessions are supposed to appear in the dropdown with greyed-out styling + tooltip.
- The 30-min `SessionActivityThresholdMinutes` only controls *active vs inactive label*, not visibility.
- Despite this, the dropdown is still empty on cold start → the filter isn't the bug.

## Root cause (verified 2026-05-07)

Two combined bugs in `JsonlService`:

### Bug 1 — Fragile Cwd hydration

`JsonlService.ParseFileIntoProject` (cs:577-578) sets `data.Cwd` only from the **first parsed entry**:
```csharp
var firstEntry = entries[0];
if (string.IsNullOrEmpty(data.Cwd))
    data.Cwd = firstEntry.Cwd;
```
- Tail reading (1 MB window) can land on entries that don't carry a `cwd` field.
- `RebuildSessionsList` then drops the session via `IsValidProjectDirectory(s.Cwd)` (cs:766-777, :798): empty Cwd → `false` → session removed.
- Plus `SessionNameHelper.GetDisplayName(cwd, dirName)` returns null only when both inputs fail — but the upstream Cwd filter strikes first, so even a derivable name doesn't save the session.

### Bug 2 — No upper bound on session age

Even after fixing Bug 1, ALL JSONL files (regardless of age) would surface. User wants 30-day cutoff with configurability (Option C).

## Disk state on report day (verified)

| Project dir | Newest JSONL age | Files |
|---|---|---|
| `D--myProjects-ccInfoWin` | 0 d | 36 |
| `C--Users-DanielMielke--claude-mem-observer-sessions` | 0 d | 218 |
| `D--deepr-recurri` | 0 d | 2 |
| `D--Intershop-Musicstore-SC-MS-Vorschlag-Produktsuche-Hybrid` | 2.9 d | 35 |
| `D--SAP-Testo-testo-frontend-nextjs` | 16.1 d | 13 |

5 projects on disk, 4 within 30 days — but dropdown is empty. Confirms Bug 1.

## Fix plan (two phases)

### Phase A — Harden Cwd hydration (`JsonlService`)

1. **Resolve Cwd across all entries**, not just the first. Iterate entries, take the first non-empty `cwd` (current code already iterates — change `if (string.IsNullOrEmpty(data.Cwd)) data.Cwd = firstEntry.Cwd;` to a per-entry update inside `ApplyEntryToProjectData`).
2. **Add fallback Cwd surrogate** when no entry carries `cwd`: derive from `SessionNameHelper.DecodeProjectDirectory(projectDirName)`. This handles encoded dir names like `D--myProjects-ccInfoWin` → produces a usable display label even without filesystem-resolvable Cwd.
3. **Soften `IsValidProjectDirectory` filter**: don't drop sessions just because `Cwd` is empty/unresolvable. Keep them in the list if a display name can still be derived from the project directory name. Mark them visually as "no cwd resolved" if needed (greyed/italic).
4. **Logging**: emit `Debug.WriteLine` with how many sessions were dropped and why — makes future regressions diagnosable without a debugger.

### Phase B — Configurable visibility window (Option C)

1. **`AppSettings.cs`**: new property `SessionVisibilityWindowDays` (int, default `30`).
2. **Settings UI**: new dropdown/slider in Settings view — options `7`, `30`, `90`, `0` (= unlimited). Localize labels (resw keys: `SessionVisibilityWindow.Header`, `SessionVisibilityWindow.7d`, `.30d`, `.90d`, `.Unlimited`).
3. **Filter location**: enforce in `MainViewModel.RefreshSessions` (display layer), **NOT** in `JsonlService`. Reason: keeps historical data available for cost/stats aggregation; only the ComboBox is filtered. Future "show last 90 days" toggles or stats panels stay possible without re-loading.
4. **Filter expression** (sketch):
   ```csharp
   var visibilityCutoff = settings.SessionVisibilityWindowDays > 0
       ? DateTimeOffset.UtcNow.AddDays(-settings.SessionVisibilityWindowDays)
       : DateTimeOffset.MinValue;
   var displayItems = latestSessions
       .Where(s => s.LastActivity >= visibilityCutoff)
       .OrderByDescending(s => s.LastActivity)
       .Select(...)
   ```
5. **Reactive re-filter**: when user changes the setting, re-run `RefreshSessions` immediately (similar pattern to existing `SessionTimeoutChangedMessage`). New message: `SessionVisibilityChangedMessage(int newWindowDays)`.

## Why this matters

- Silently broken UX: dropdown looks like the app forgot all your work; no error, no clue.
- Already on user's daily-use list: gap reported during normal v1.4 UAT, not edge-case.
- Touches the same surface as v1.4 D-06 (inactive-session visibility) — natural continuation.
- Cwd hydration fix is independently valuable: improves session display reliability across the app, not just dropdown.

## Verify before scoping

- Repro the empty-dropdown state in a debug session and inspect `_sessions` after `RebuildSessionsList`: confirm sessions are dropped at the `IsValidProjectDirectory` filter, not at `GetDisplayName` (`Cwd is null` vs `Directory.Exists(Cwd) == false`).
- Open one of the JSONL files from a "missing" project (e.g. `D--Intershop-Musicstore-...` 2.9 d old) and confirm whether the `cwd` field is present at all — Tauri reference suggests it should be in every entry, but verify against current Claude CLI output.
- Manually set `data.Cwd` to a non-empty bogus value and re-test: does the session appear? Confirms the filter is the gate, not Cwd hydration alone.
- Confirm no regression on subagent files (cs:642-644 `IsSubagentFile` — must stay excluded from session list regardless of fix).

## Out of scope for this issue

- Removing the `IsValidProjectDirectory` `Directory.Exists` check entirely. Sessions whose project directory was deleted by the user should still drop — only "cwd not yet resolved" should be tolerant.
- Changing `SessionActivityThresholdMinutes` semantics (still controls active/inactive label).
- Performance optimization for huge `_projectData` dictionaries — current 5-project size is fine; revisit only if cold-start scan exceeds 1s.

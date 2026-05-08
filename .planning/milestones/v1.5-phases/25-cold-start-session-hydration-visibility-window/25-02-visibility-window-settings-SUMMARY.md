---
phase: 25-cold-start-session-hydration-visibility-window
plan: "02"
subsystem: settings/session-visibility
tags: [settings, combobox, visibility-window, messaging, localization, display-filter]
dependency_graph:
  requires: [25-01-jsonlservice-hardening]
  provides: [DROPDOWN-01, DROPDOWN-04]
  affects: [AppSettings, SettingsViewModel, SettingsView, MainViewModel, Messages, Resources.resw]
tech_stack:
  added: []
  patterns: [IRecipient-G1, ValueChangedMessage, ComboBox-index-mapping, display-layer-filter]
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Models/AppSettings.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
decisions:
  - "CD-01: ComboBox (not segmented control) — mirrors SessionTimeoutMinutes precedent; 4 discrete options"
  - "CD-04: MainViewModel handles SessionVisibilityChangedMessage directly via IRecipient (mirrors SessionTimeoutChangedMessage at line 1043)"
  - "resw key convention: TextBlock label uses SettingsSessionVisibilityWindow.Text; ComboBoxItems use SessionVisibilityWindow{7d|30d|90d|Unlimited}.Content — mirrors Timeout15.Content pattern"
  - "Display-layer filter only: Sessions.Clear/Add loop untouched so JsonlService aggregation stats remain over ALL sessions"
  - "Task 4 visual smoke deferred to Phase 28 Final UAT per user directive (never pause on human-verify checkpoints)"
metrics:
  duration: "~5 min"
  completed: "2026-05-08"
  tasks_completed: 3
  files_changed: 7
---

# Phase 25 Plan 02: Visibility Window Settings (DROPDOWN-01/04) Summary

SessionVisibilityWindowDays ComboBox in General Settings tab with reactive display-layer filter in MainViewModel.RefreshSessionList — wired through SessionVisibilityChangedMessage with G-1-compliant TryEnqueue dispatch.

## What Was Built

### AppSettings Additions (AppSettings.cs)

Two new JSON-persisted properties appended after `SonnetContextSize`:

```csharp
[JsonPropertyName("sessionVisibilityWindowDays")]
public int SessionVisibilityWindowDays { get; set; } = 30;

[JsonPropertyName("sessionVisibilityMigrationShown")]
public bool SessionVisibilityMigrationShown { get; set; }
```

Default 30 per D-03 / research Decision 4. `SessionVisibilityMigrationShown` defaults to `false` so existing installs trigger the migration toast on first launch (Plan 25-03 / D-04).

### SessionVisibilityChangedMessage (Messages/SessionVisibilityChangedMessage.cs)

Mirror of `SessionTimeoutChangedMessage` — `ValueChangedMessage<int>` carrying `newWindowDays`:

```csharp
public class SessionVisibilityChangedMessage : ValueChangedMessage<int>
{
    public SessionVisibilityChangedMessage(int newWindowDays) : base(newWindowDays) { }
}
```

### SettingsViewModel Additions (SettingsViewModel.cs)

- `VisibilityWindowDayOptions = [7, 30, 90, 0]` (static array; 0 = unlimited)
- `DefaultVisibilityWindowIndex = 1` (30 days)
- `[ObservableProperty] _selectedVisibilityWindowIndex`
- `Initialize()` loads: `_selectedVisibilityWindowIndex = MapVisibilityDaysToIndex(settings.SessionVisibilityWindowDays)`
- `OnPropertyChanged(nameof(SelectedVisibilityWindowIndex))` added to cascade
- `OnSelectedVisibilityWindowIndexChanged(int value)`: saves to settings + emits `SessionVisibilityChangedMessage`
- `MapIndexToVisibilityDays(int index)` / `MapVisibilityDaysToIndex(int days)` helpers

### SettingsView.xaml Row 3.5

Inserted between Session Timeout divider and Dark Mode row:

```xml
<!-- Row 3.5: Session Visibility Window (DROPDOWN-04 / D-03) -->
<Grid Height="40" Padding="12,0">
    ...
    <TextBlock l:Uids.Uid="SettingsSessionVisibilityWindow" ... />
    <ComboBox x:Name="VisibilityWindowComboBox" l:Uids.Uid="VisibilityWindowComboBox"
              SelectedIndex="{x:Bind ViewModel.SelectedVisibilityWindowIndex, Mode=TwoWay}" MinWidth="120">
        <ComboBoxItem l:Uids.Uid="SessionVisibilityWindow7d" />
        <ComboBoxItem l:Uids.Uid="SessionVisibilityWindow30d" />
        <ComboBoxItem l:Uids.Uid="SessionVisibilityWindow90d" />
        <ComboBoxItem l:Uids.Uid="SessionVisibilityWindowUnlimited" />
    </ComboBox>
</Grid>
```

### MainViewModel Changes (MainViewModel.cs)

**Class declaration** (line 48-51):
```csharp
IRecipient<SessionVisibilityChangedMessage>   // DROPDOWN-04 / D-03
```

**InitializeAsync registration** (after line 316):
```csharp
WeakReferenceMessenger.Default.Register<SessionVisibilityChangedMessage>(this);   // DROPDOWN-04 / D-03
```

**Receive method** (after Receive(SessionTimeoutChangedMessage), at line ~1057):
```csharp
public void Receive(SessionVisibilityChangedMessage message)
{
    // G-1 compliant: constructor-injected _dispatcherQueue is non-null. L-02 honored.
    _dispatcherQueue.TryEnqueue(RefreshSessionList);
}
```

**RefreshSessionList cutoff filter** (inserted before `OrderByDescending`, display layer only):
```csharp
var visibilityCutoff = settings.SessionVisibilityWindowDays > 0
    ? DateTimeOffset.UtcNow.AddDays(-settings.SessionVisibilityWindowDays)
    : DateTimeOffset.MinValue;

var displayItems = latestSessions
    .Where(s => s.LastActivity >= visibilityCutoff)
    .OrderByDescending(s => s.LastActivity)
    .Select(...)
    .ToList();
```

The `Sessions.Clear/Add` loop above is untouched — `JsonlService` aggregation stats remain over ALL sessions (D-03 scope boundary honored).

### Resw Key Pairs (5 keys × 2 locales)

| Key | de-DE | en-US |
|-----|-------|-------|
| `SettingsSessionVisibilityWindow.Text` | `Sichtbarkeitsfenster` | `Visibility window` |
| `SessionVisibilityWindow7d.Content` | `7 Tage` | `7 days` |
| `SessionVisibilityWindow30d.Content` | `30 Tage` | `30 days` |
| `SessionVisibilityWindow90d.Content` | `90 Tage` | `90 days` |
| `SessionVisibilityWindowUnlimited.Content` | `Unbegrenzt` | `Unlimited` |

**Convention used:** TextBlock label via `SettingsSessionVisibilityWindow.Text` (matches `SettingsSessionTimeout.Text` pattern). ComboBoxItems via `{uid}.Content` (matches `Timeout15.Content` pattern). Not `.Header` — the project uses `.Text` for row labels and `.Content` for ComboBox items consistently.

## Visual Smoke Deferred

**Task 4 (checkpoint:human-verify)** is deferred to Phase 28 Final UAT per user directive to never pause on `human-verify` checkpoints during autonomous plan execution. The following manual smoke steps should be performed in Phase 28:

1. Open Settings → General tab. Confirm new row "Sichtbarkeitsfenster" / "Visibility window" between Session Timeout and Dark Mode.
2. ComboBox shows "30 Tage" / "30 days" selected by default.
3. 4 options visible: 7/30/90 Tage + Unbegrenzt (DE) or days + Unlimited (EN).
4. `settings.json` contains `"sessionVisibilityWindowDays": 30` and `"sessionVisibilityMigrationShown": false`.
5. Switch to "7 Tage" → `settings.json` updates to `7`; Active Session ComboBox reflects filter.
6. Switch to "Unbegrenzt" → `settings.json` shows `0`; all sessions visible.
7. Language toggle DE↔EN switches row label and all 4 option texts correctly.

## Deviations from Plan

None. Plan executed exactly as written. The resw key naming convention (`SettingsSessionVisibilityWindow.Text` + `SessionVisibilityWindow{Xd}.Content`) was confirmed by reading the existing `SettingsSessionTimeout.Text` / `Timeout15.Content` pattern — the plan's Note about checking convention before committing was followed.

## Test Results

| Suite | Before 25-02 | After 25-02 |
|-------|-------------|-------------|
| Total | 290 | 290 |
| Passing | 288 | 288 |
| Failing | 2 | 2 |
| New failures | — | 0 |

Pre-existing failures: 2 × `ClaudeApiServiceTests` (parameter-naming mismatches, out of scope).

- `MessengerThreadingConventionTests`: 2 passed (G-1 / L-02 compliance verified on new `Receive`)
- `ResourceCoverageTests`: 4 passed (DE/EN key parity for 5 new keys confirmed)
- `JsonlServiceColdStartTests`: 4 passed (Plan 25-01 regression check clean)

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries introduced.

## Self-Check

- `CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs` — FOUND
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` contains `SessionVisibilityWindowDays` — FOUND
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` contains `SessionVisibilityMigrationShown` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` contains `SelectedVisibilityWindowIndex` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` contains `VisibilityWindowDayOptions = [7, 30, 90, 0]` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` contains `IRecipient<SessionVisibilityChangedMessage>` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` contains `Register<SessionVisibilityChangedMessage>` — FOUND
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` contains `visibilityCutoff` — FOUND
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` contains `VisibilityWindowComboBox` — FOUND
- de-DE Resources.resw contains `SettingsSessionVisibilityWindow.Text` — FOUND
- en-US Resources.resw contains `SettingsSessionVisibilityWindow.Text` — FOUND
- Commit `6f7da42` (Task 1) — FOUND
- Commit `bcf4007` (Task 2) — FOUND
- Commit `87e2bfc` (Task 3) — FOUND
- Build: 0 errors — PASS
- MessengerThreadingConventionTests: 2 passed — PASS
- ResourceCoverageTests: 4 passed — PASS
- Full suite: 2 pre-existing failures only — PASS

## Self-Check: PASSED

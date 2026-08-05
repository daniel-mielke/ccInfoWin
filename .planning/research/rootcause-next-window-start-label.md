---
name: Backlog — macOS v1.12.0 feature parity (next-window label + session renaming)
description: Upstream stefanlange/ccInfo released v1.12.0 on 2026-05-02 with two new features. v1.5 should bring CCInfoWindows to v1.12.0 parity (analogous to how v1.4 = v1.11.1 parity). Two features: (A) absolute next-window start time displayed next to the countdown — small label change; (B) session renaming via pencil button + new "Sessions" Settings tab with persistent custom names — medium-high effort: dual UI surface, new persistence layer, override in SessionNameHelper, migration story.
type: project
originSessionId: 4fcfe4f9-d257-456b-bc4f-1109b37175ac
---
# v1.12.0 Feature Parity — two upstream features for v1.5

**Reported:** 2026-05-07 by user. Originally captured as a single backlog item (next-window label only); re-scoped 2026-05-07 after checking upstream release notes — v1.12.0 contains TWO features, both belong in v1.5.

**Upstream source:** https://github.com/stefanlange/ccInfo/blob/main/RELEASENOTES.md (v1.12.0 dated 2026-05-02).
**Our current parity baseline:** v1.4 milestone shipped as "macOS v1.11.1 Feature Parity" (closed 2026-05-07). Manual Reload Button (v1.11.1) verified present in our `LoginView.xaml` and resw files. So v1.11.1 parity is real, not aspirational — v1.5 = the natural next parity step.

---

## Feature A — Next 5h-window start time label

**User quote (DE):**
> "ich vermisse bei diesem milestone eine implementierung einer zusätzlichen zeitangabe beim '5-stunden-fenster'. und zwar, wann das nächste 5-stunden-Fenster beginnt (mit angabe von wochentag und uhrzeit)"

**Upstream behavior (v1.12.0 release note):**
> "Display the absolute reset time of the 5-hour window below the chart (e.g., 'Mo 1.5. 16:30') alongside the countdown timer"

### What to add

Below (or next to) the existing `FiveHourCountdown` label ("zurücksetzung in 1 Std. 49 Min."), add a second small label showing the next window's START time as weekday + 24h clock:

- **DE format:** "Neues Fenster: Mi., 14:30" — or "Mo 1.5. 16:30" matching upstream wording for cross-day clarity
- **EN format:** "Next window: Wed 14:30" — or "Mon May 1, 16:30" for cross-day clarity

### Where the data lives

- `UsageResponse.FiveHour.ResetsAt` (DateTimeOffset?) — the Reset time of the CURRENT 5-hour window IS the START of the NEXT window. No new API field needed; just format the same value differently.
- Currently surfaced only as relative countdown via `CountdownFormatter.FormatCountdown(data.FiveHour.ResetsAt)` → `FiveHourCountdown` ObservableProperty in `MainViewModel`.

### Implementation sketch

1. **MainViewModel:** new `[ObservableProperty] FiveHourNextWindowStartText` formatting `FiveHour.ResetsAt.LocalDateTime` to weekday + clock using `CultureInfo.CurrentUICulture` (auto-switches DE/EN with app language).
   - Short form: `data.FiveHour.ResetsAt.LocalDateTime.ToString("ddd HH:mm", CultureInfo.CurrentUICulture)`
   - Cross-day form (when reset is >12h away or next calendar day): `"ddd d. MMM HH:mm"` — matches upstream "Mo 1.5. 16:30" pattern
2. **MainView.xaml:** small TextBlock under the countdown row, bound to the new property. Use existing 13px secondary-text typography ramp.
3. **Localization:** new resw key — `FiveHourNextWindowStart.Text` in both `de-DE` and `en-US/Resources.resw` per Phase 23 pattern.
4. **Null handling:** `FiveHour.ResetsAt` can be null when no current window. Decide: hide the label entirely vs. render "Neues Fenster: —". Upstream behavior unknown; our default = hide.

### Effort estimate
~2 hours: 1 ObservableProperty, 1 XAML row, 2 resw entries, 1 unit test for the formatter (verify cross-day format kicks in correctly).

---

## Feature B — Session renaming with persistent custom names

**Upstream behavior (v1.12.0 release note):**
> "Session renaming via pencil button next to switcher or new Sessions tab in Settings; names persist across restarts"

### What to add

Two UI entry points + one persistence layer:

1. **Inline pencil button** next to the session switcher (ComboBox) in MainView. Click → opens an editable text field (or a small dialog) → user types custom name → save.
2. **New "Sessions" tab** in Settings showing a list of all known sessions with their current display names + edit fields for custom rename. Bulk-management surface.
3. **Persistence:** custom names survive app restarts. Storage location TBD: probably `%LOCALAPPDATA%\CCInfoWindows\session-names.json` as a `Dictionary<string projectDirName, string customName>`.

### Where this hooks in

- **`SessionNameHelper.GetDisplayName(cwd, fallbackDirName)`** (Helpers/SessionNameHelper.cs) — currently has a 3-step fallback chain (cwd → decoded dir name → null). Add a new step at the TOP of the chain: check the custom-name store first; only fall back to existing logic if no custom name exists.
- **New service:** `ISessionNameStore` / `SessionNameStore` — load/save the JSON map, expose `GetCustomName(string projectDirName)` and `SetCustomName(string projectDirName, string?)` (null = clear). Register in DI.
- **`SessionInfo.DisplayName`** — currently set in `JsonlService.RebuildSessionsList` via `SessionNameHelper.GetDisplayName`. Either:
  - (a) inject `ISessionNameStore` into `JsonlService` and let `GetDisplayName` consume it, OR
  - (b) move the display-name resolution one layer up to `MainViewModel.RefreshSessions` so `JsonlService` stays storage-free.
  - **Decision needed during /gsd-discuss-phase**: option (b) keeps `JsonlService` cleaner (no new dependency); option (a) gives a single source of truth for display names across the app.
- **MainViewModel:** new `[RelayCommand] RenameSessionAsync(SessionDisplayItem item)` to drive the pencil-button flow. Trigger `RefreshSessions` after save.
- **SettingsViewModel + new SettingsSessionsView.xaml:** the bulk-management tab. Inserts as a new pivot/tab item alongside existing Settings sections.

### Migration / edge cases

- **Existing users have no custom names** → `session-names.json` doesn't exist on first run → fallback chain unchanged → no migration needed.
- **Session disappears from disk after rename** (project deleted, JSONL files removed) → custom name becomes orphaned. Decision: prune on next app start (cleanup pass) vs. keep forever (safer if user re-clones the project). Default = keep, prune on user request only.
- **Project directory renamed** (Cwd changes but user wants to keep the rename) → keyed by `projectDirName` (encoded), which is stable across cwd changes. Verify this assumption against Claude CLI's encoding behavior.
- **Localization:** the rename UI itself needs DE/EN strings (label "Sitzung umbenennen" / "Rename session", placeholder, save/cancel buttons, settings tab title).

### Effort estimate
~1.5–2 days:
- Service + persistence layer + tests: 0.5d
- Pencil-button inline UI + binding: 0.5d
- Settings "Sessions" tab + bulk UI: 0.5d
- Localization, edge cases, integration tests: 0.5d

---

## Why both features belong in v1.5 (not split)

- **Parity narrative** — v1.4 was branded "v1.11.1 Feature Parity"; v1.5 = "v1.12.0 Feature Parity" is a clean continuation. If we skip Feature B and ship only Feature A, we'd need a separate "v1.12.0 catch-up" milestone later, doubling the milestone overhead.
- **Shared surface** — both features touch session display: Feature A formats time; Feature B overrides session names. Some plumbing (e.g. the `SessionTimeoutChangedMessage`-style reactive refresh pattern) gets reused.
- **Effort balance** — Feature A is small, Feature B is medium. Bundling gives a v1.5 milestone with a sensible mass: too small to justify standalone, too useful to defer.

## Out of scope for v1.5 (deliberate)

- **Session deletion / archiving** — upstream release note doesn't mention it. If users want to remove sessions from the dropdown, they currently can via the "30-day visibility window" Issue 1 fix.
- **Cloud sync of custom names** — purely local persistence is fine. DPAPI not needed (names aren't secrets).
- **Per-session colors / icons / tags** — not in upstream v1.12.0; revisit only if user requests.

## Verify before scoping (during /gsd-discuss-phase)

- Confirm upstream's actual rendering of Feature A: is it always "Mo 1.5. 16:30" format, or does it switch between short ("Mo 16:30") and long depending on cross-day? Inspect macOS app or its source if available.
- Confirm Feature B's pencil-button placement: directly next to the switcher, or floating? Inline edit or modal dialog?
- Decide JsonlService dependency direction (option a vs b above) before planning.
- Verify `projectDirName` stability across Claude CLI versions — risk: if encoding changes, custom-name keys go stale.

---
gsd_state_version: 1.0
milestone: v1.6
milestone_name: macOS ccInfo v1.15.2 Feature Parity
status: in_progress
workflow: ultracode / plan-mode (NOT GSD -- see CLAUDE.md "Workflows")
roadmap: .planning/milestones/v1.6-ROADMAP.md
stopped_at: Phase 0 complete (tests in .sln, structural resw tests, FakeDispatcherTimer moved, CLAUDE.md fixed). Phase 1 next.
last_updated: "2026-08-05T20:55:00.000Z"
last_activity: 2026-08-05 -- Phase 0 done, 345/345 tests green
progress:
  total_phases: 6
  completed_phases: 1
  percent: 17
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-05-08)

**Core value:** Developers can see their Claude usage limits (5-hour window, weekly quota, context window) at a glance in real-time, preventing unexpected throttling.
**Current focus:** v1.6 — feature parity with macOS ccInfo v1.15.2 (gap: v1.13.0 … v1.15.2)

## Current Position

**Milestone: v1.6 — macOS ccInfo v1.15.2 Feature Parity**
**Roadmap: `.planning/milestones/v1.6-ROADMAP.md` — read this first.**
Workflow: ultracode / plan mode, **not GSD**. No PLAN.md/SUMMARY.md per phase;
the roadmap is the single source of truth. Update the phase table below after each phase.

Phase: 2 of 5 — NEXT
Status: Phasen 0 + 1 abgeschlossen 2026-08-05
Last activity: 2026-08-05 -- Phase 1 committed, 366/366 Tests grün

### Roadmap-Korrektur aus Phase 1 (wichtig für die Abnahme)

Der Roadmap-Pseudocode zu Schritt 2 („`max_input_tokens > 200_000` → nur wenn
Above-200k-Preisstufe existiert, sonst 200K") widerspricht der eigenen Prosa und der
Abdeckungsmatrix. Gegen die echten Upstream-Daten geprüft: die Above-200k-Stufe ist ein
**Veto**, kein Gate. Sie markiert ein Opt-in-Fenster, das einen Beta-Header braucht
(`claude-sonnet-4-20250514`: 1M **mit** Stufe → effektiv 200K), während die nativen
1M-Modelle (`sonnet-5`, `sonnet-4-6`, `opus-5`, `opus-4-6/4-7/4-8`, `fable-5`) **keine**
Stufe haben und ihr volles Fenster behalten. Nur so entstehen die in der Matrix
zugesagten Ergebnisse. Implementiert ist die Veto-Semantik.

| Phase | Inhalt | Status |
|-------|--------|--------|
| 0 | Fundament: Tests in .sln, ResourceCoverageTests entschärfen, FakeDispatcherTimer verschieben, CLAUDE.md:44 | **fertig** |
| 1 | Kontextfenster aus `max_input_tokens` (v1.14.2) + Upstream-Pricing-Datei | **fertig** |
| 2 | Sonnet-Context-Setting zurückbauen (v1.14.2) | offen |
| 3 | Chart-Redesign: monotone-cubic, Fade-Fill, Glow, Insets (v1.13.0 + v1.14.0) | offen |
| 4 | Notifications: 80/95-Thresholds + Window-Reset (v1.5.0 + v1.15.0/1/2) | offen |
| 5 | Restfixes: `.Distinct()`, Steilheitsfilter, Output-Tier, Re-Aggregate | offen |

### Verify-Umgebung (für Folgephasen)

- Testbaseline **345/345 grün**. Die zwei früher als „pre-existing" geführten
  `ClaudeApiServiceTests`-Fehler waren veraltete Tests (HttpClient-Ära), nicht ein
  Produktionsbug — sie wurden in Phase 0 auf den echten Vertrag
  (`ClaudeApiService.cs:75` `if (responseBody is null) return null;`) nachgezogen.
- Screenshot-Geometrie für `windows-mcp` (Fenster sonst rechts abgeschnitten):
  `window_management set_bounds x=60 y=20 width=625 height=1180`.
- **Gesperrte Workstation blockiert Screenshots.** Läuft `LogonUI.exe`, komponiert DWM für
  die Sitzung nicht mehr und `screenshot_control` liefert ein schwarzes Bild — die App läuft
  dabei normal weiter. Prüfen mit `Get-Process LogonUI`. Ersatz: `mcp__windows-mcp__ui_find`
  / `ui_read` lesen den UIA-Baum mit Live-Werten auch im gesperrten Zustand und sind für
  Zahlen/Texte sogar präziser. Nur **pixelbezogene** Prüfungen (Phase 3: Glow-Clipping,
  Kurven-Überschwingen, beide Themes, Export-PNG vs. Live) brauchen eine entsperrte Sitzung.

**Reihenfolge:** 0 zuerst (blockiert Verify). Dann 1 → 2, 3 und 4 unabhängig, 5 zuletzt.
**Einzige Konfliktdatei:** `App.xaml.cs` — Phase-1-Edit vor Phase-4-Edit.

### Entschieden (nicht neu aufrollen)

1. **Notification-Scheduling:** In-Process-`IDispatcherTimer`, keine neue Abhängigkeit.
   Dokumentierte Abweichung: Reset-Toast nur bei laufender App (unpackaged →
   `AppNotificationManager` kann nicht terminieren).
2. **Pricing-Fallback:** Upstream-`claude-pricing-fallback.json` 1:1 übernehmen.
3. **Verify pro Phase:** App beenden → `dotnet test` → `dotnet build -c Release` →
   App starten + Screenshot per `windows-mcp` → Bugfix-Runde bei Fund.

### Nicht anfassen

- Die 5 im Roadmap-Abschnitt „Bereits erfüllt" gelisteten Punkte — sie sind verifiziert
  schon erledigt (orange Sonnet-Badges, DE-Übersetzungen, 2 Dezimalstellen bei Kosten,
  `?? 0`-Immunität, teilweise Long-Context-Preisstufen).
- `MainViewModel.cs:627` und `:666` (`IsWindowReset`) — die Notifications brauchen einen
  eigenen Trigger, Begründung in Roadmap Phase 4.
- `MainViewModel`-Split (1235 Zeilen) — im Backlog, nicht in diesem Milestone.

**v1.5 Phase Sequence (research-validated, do not reorder):**

1. **Phase 24** — DISPATCH foundation (`IDispatcherQueue` adapter + C-1/C-2 fix + G-1 convention enforcement)
2. **Phase 25** — DROPDOWN (Cwd hydration + visibility window + cold-start data-loss race fix)
3. **Phase 26** — RENAME (session-rename feature, biggest phase: ContentDialog + 5th Settings tab + `ISessionNameStore`)
4. **Phase 27** — NEXTWIN + ORGID + PRICING + L10N (mid-risk feature trio with non-overlapping surfaces; B3 + M-2/L10N must couple)
5. **Phase 28** — CLEANUP (M-1 + M-3 + Nits + final UAT)

## Performance Metrics

**v1.4 totals (shipped):**

- Total phases: 4 (Phase 20-23)
- Total plans: 13 (10 base + 3 gap-closure)
- Total commits: 51 (range `21a73bb..0d9c483` + audit + archive)
- LOC delta: 64 files, +11,115 / -42 lines
- Test coverage delta: +26 tests on modified surface, 4 new test classes

**v1.5 in flight:**

| Phase | Plans | Status | Completed |
|-------|-------|--------|-----------|
| 24 Dispatcher Foundation & Marshaling Convention | 0 | Not started | — |
| 25 Cold-Start Session Hydration & Visibility Window | 0 | Not started | — |
| 26 Persistent Session Renaming | 0 | Not started | — |
| 27 Next-Window Label, Org-ID Picker, Pricing Surfacing & L10N | 0 | Not started | — |
| 28 v1.4 Cleanup & Final UAT | 0 | Not started | — |
| Phase 24 P01 | 25min | 3 tasks | 5 files |
| Phase 24 P02 | 35min | 3 tasks | 5 files |
| Phase 24 P03 | 4min | 3 tasks | 3 files |
| Phase 25 P01 | 35min | 2 tasks | 4 files |
| Phase 25 P02 | 5 | 3 tasks | 7 files |
| Phase 25 P25-03 | 10 | 2 tasks | 5 files |
| Phase 26-persistent-session-renaming P01 | 18m | 3 tasks | 7 files |
| Phase 26 P02 | 45 | 2 tasks | 9 files |
| Phase 26 P03 | 70 | 2 tasks | 15 files |
| Phase 27-nextwin-orgid-pricing-l10n P01 | 15 | 3 tasks | 5 files |
| Phase 27-nextwin-orgid-pricing-l10n P02 | 20 | 3 tasks | 5 files |
| Phase 27-nextwin-orgid-pricing-l10n P03 | 4min | 3 tasks | 6 files |
| Phase 27-nextwin-orgid-pricing-l10n P04 | 240min | 3 tasks | 14 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table. Recent v1.4 additions:

- `_autoReauthAttempted` single bool flag for first-vs-second 401 routing
- Hybrid sync+async history persistence (sync at termination, async during poll)
- `IDispatcherTimer` adapter for headless About-tab timer testing
- Direct DI call instead of `WeakReferenceMessenger` for logout (production hotfix lesson)
- Gap-closure as additional wave within parent phase
- Belt-and-suspenders `IsEnabled` x:Bind on `[RelayCommand]` buttons

**v1.5 architecture decisions (from research/SUMMARY.md, to be logged in PROJECT.md as phases ship):**

- Decision 1: `ISessionNameStore` hooks at the display layer in `MainViewModel.RefreshSessionList` — NOT inside `JsonlService` (preserves storage-free service tests; honors D-13 lesson)
- Decision 2: Phase build order 24 → 25 → 26 → 27 → 28 (foundation before any new `IRecipient<>` lands)
- Decision 3: `IDispatcherQueue` ships as full adapter in Phase 24 (interface + production adapter + `FakeDispatcherQueue` + convention test) — mirrors v1.4 `IDispatcherTimer` precedent

**v1.5 conventions to land in CLAUDE.md:**

- G-1: `IRecipient<>.Receive` always-TryEnqueue rule (Phase 24)
- G-2: JSON-on-disk store pattern with `SemaphoreSlim` write guard (Phase 26 first consumer: `ISessionNameStore`)
- G-3: `[ObservableProperty]` defaults — prefer real initializers over `null!` (Phase 28)
- [Phase ?]: IDispatcherQueue adapter ships as full interface + WinuiDispatcherQueueAdapter singleton + FakeDispatcherQueue test double, mirroring v1.4 IDispatcherTimer precedent (Phase 24 Plan 01)
- [Phase ?]: CD-01: ComboBox for SessionVisibilityWindowDays (mirrors SessionTimeoutMinutes precedent)
- [Phase ?]: CD-04: MainViewModel handles SessionVisibilityChangedMessage directly via IRecipient (not JsonlService re-emit)
- [Phase ?]: DROPDOWN-05: InfoBar migration toast in MainViewModel.InitializeAsync with synchronous SaveSettings on dismiss (D-04 + CD-02 + CD-05 honored)
- [Phase ?]: ISessionNameStore D-01 shape locked: GetCustomName/SetCustomName/ClearCustomName/Save/SaveAsync/NameChanged
- [Phase ?]: session-names.json path D-02: %LOCALAPPDATA%\CCInfoWindows\, Dictionary<string,string> keyed by encoded projectDirName
- [Phase ?]: D-L10-01: 5 LastFetchRelative resw keys with {0} placeholders on MinutesAgo/HoursAgo/DaysAgo
- [Phase ?]: D-L10-02: LastFetchRelativeTime uses 5-branch if-chain calling Localizer.Get() per category; DateTimeOffset.Now retained
- [Phase ?]: IsPricingErrorVisible = IsPricingError && !IsSessionExpired (D-PR-04 banner-stack policy: auth wins)
- D-OG-01: ListAvailableOrganizationsAsync extracted from private TryMigrateOrgIdAsync as public IClaudeApiService method
- D-OG-02: OrgPicker ContentDialog created fully programmatically in View code-behind (XamlReader.Load unavailable in WinUI 3)
- D-OG-03: Event-bridge pattern (RequestOpenOrgPickerDialog) for MVVM-safe ContentDialog from ViewModel
- D-OG-04: D-13 workaround — org-switch logout uses AuthStateChangedMessage(false) broadcast (MainViewModel is AddTransient)
- D-OG-05: OrgMismatch InfoBar poll-counter threshold=5, in-memory suppression only (not persisted)

### Roadmap Evolution

- Phase 29 added (post-v1.5 archive): Fix Subagent activity detection: switch from assistant-timestamp to filesystem mtime (macOS parity). Driver: visual UAT discovered that during long tool-calls subagents fall out of the 30s `lastEntry.Timestamp` cutoff (`JsonlService.cs:712–716`); macOS original (`JSONLParser.swift:457–483, findActiveAgents`) uses `contentModificationDate` of the file instead, which captures every tool-result write — robust against silent assistant gaps. Evidence: `spec/v1.11.1-macOS/{claude-cli-4-agents-aktiv,ccinfo-nur-2-sub-agents}.png` show 4 active agents in CLI vs. 2 in ccInfo.

### Open Tech Debt (carried into v1.5)

**v1.4 code-review findings (2026-05-07, scheduled in v1.5):**

- 🔴 **C-1**: Fire-and-forget Task in `MainViewModel.Receive(AuthStateChangedMessage)` → Phase 24 (DISPATCH-04)
- 🔴 **C-2**: `Receive(AuthStateChangedMessage)` mutates UI state without DispatcherQueue marshaling → Phase 24 (DISPATCH-04)
- 🟡 **M-1**: Orphan `LogoutRequestedMessage.cs` from reverted Plan 21-03 → Phase 28 (CLEANUP-01)
- 🟡 **M-2**: `LastFetchRelativeTime` hardcoded EN strings — couples with B3 → Phase 27 (L10N-01)
- 🟡 **M-3**: `_contextModelBadgeColor = null!` → Phase 28 (CLEANUP-02)
- ⚪ **Nits**: 3 minor cleanups → Phase 28 (CLEANUP-03)

**Carried from earlier milestones / phase backlog (memory-tracked):**

- Cold-start session scanning (`.planning/research/rootcause-session-dropdown.md`) → Phase 25 (DROPDOWN-01..06)
- Multi-account org-id picker (`.planning/research/rootcause-org-id-picker.md`) → Phase 27 (ORGID-01..05)
- Pricing service silent failure (`.planning/research/rootcause-pricing-never-loaded.md`) → Phase 27 (PRICING-01..03)
- Next 5h-window start label (`.planning/research/rootcause-next-window-start-label.md`) → Phase 27 (NEXTWIN-01..03)
- WeakReferenceMessenger + AddTransient ViewModels = recipient GC pitfall — codified as G-1 convention in Phase 24
- 2 pre-existing `ClaudeApiServiceTests` failures (parameter naming mismatch, production unaffected — out of scope per REQUIREMENTS.md)
- 13 pre-existing `JsonlServiceTests` failures (parameter naming mismatch, production unaffected — out of scope per REQUIREMENTS.md)
- AUTH-01/02 visual smoke deferred — dev build can't easily force a 401

### Blockers/Concerns

(None — roadmap approved, ready for Phase 24 planning)

## Session Continuity

Last session: 2026-05-08T20:47:00Z
Stopped at: Phase 28 CLEANUP-01..04 + Final UAT Checklist complete. Milestone v1.5 all phases done.
Resume file: .planning/phases/28-v1-4-cleanup-final-uat/28-FINAL-UAT-CHECKLIST.md (visual UAT pending)

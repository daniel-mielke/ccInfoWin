---
gsd_state_version: 1.0
milestone: v1.6
milestone_name: macOS ccInfo v1.15.2 Feature Parity
status: uat_complete
workflow: ultracode / plan-mode (NOT GSD -- see CLAUDE.md "Workflows")
roadmap: .planning/milestones/v1.6-ROADMAP.md
stopped_at: Visual UAT complete. U1-U11 all pass; U6 failed and was fixed (axis label gutter). Ready for the v1.6 ship tag.
last_updated: "2026-08-06T07:30:00.000Z"
last_activity: 2026-08-06 -- visual UAT U1-U11, U6 axis-label regression fixed, 434/434 tests green
progress:
  total_phases: 6
  completed_phases: 6
  percent: 100
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

Phase: alle 6 (0-5) abgeschlossen — **Code komplett, visuelle UAT durchgeführt**
Status: implementiert und committet 2026-08-05, UAT 2026-08-06
Last activity: 2026-08-06 -- UAT U1-U11, U6-Regression gefixt, 434/434 Tests grün

**Nächster Schritt:** v1.6 Ship-Tag — **wartet auf den User.** Er führt am 2026-08-06 ab ca. 10:00
noch eigene manuelle Tests durch (mehrere Stunden) und will erst danach taggen. Vor dem Tag also
nachfragen, ob dabei etwas aufgefallen ist; mögliche Funde landen zuerst hier, nicht direkt im Tag.

Wenn er dabei einen Reset-Toast sieht: das 5-h-Fenster endet regulär am 2026-08-06 um 13:29 lokal,
ein Toast zu diesem Zeitpunkt ist also echt und kein Rückstand aus der UAT.

### Laufzeitbeleg Phase 4 (aus `%LOCALAPPDATA%\CCInfoWindows\notification-state.json`)

Der erste echte Poll nach dem Release-Build hat genau den v1.15.1-Fehlerfall und seine
Behebung dokumentiert:

```json
"fiveHour": { "windowId": "2026-08-06T00:20:00.0000000+00:00",
              "resetsAt": "2026-08-06T00:20:00.07056+00:00",  "peakUtilization": 48 }
"weekly":   { "windowId": "2026-08-09T00:00:00.0000000+00:00",
              "resetsAt": "2026-08-09T00:00:00.070581+00:00", "peakUtilization": 9 }
```

Sub-Sekunden-Rauschen in beiden `resetsAt` (`.07056` vs `.070581`) → ein `==`-Vergleich hätte
zwischen zwei Polls nie gematcht. Die minuten-truncateten `windowId` sind stabil, alle Flags
stehen bei 48 %/9 % korrekt auf `false`. Das Wochenfenster endet auf `00:00:00`, also an einer
Kalendergrenze — der Grund für Countdown statt Kalendervergleich.

### Befund aus Phase 3: die GPU-Tests sind ein echter Prüfkanal

`ExportHelperTests` mit `[Trait("Category","RequiresGPU")]` laufen auf dieser Maschine
tatsächlich gegen ein reales Direct2D-Gerät (459 ms / 48 ms), sie werden **nicht**
übersprungen. Damit ist empirisch belegt, dass `FillGeometry(geometry, brush, opacityBrush)`
einen **Gradient** als Primärbrush akzeptiert — die alte D2D-1.0-Doku verlangt dort einen
BitmapBrush, das gilt hier nicht mehr. Ein dritter Test mit bösartigem Input
(Duplikat-Zeitstempel, Punkte hinter dem Fensterende, 600 Punkte) hält den NaN-Pfad zu.
Bei künftigen Win2D-Änderungen: `dotnet test --filter "Category=RequiresGPU"`.

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
| 2 | Sonnet-Context-Setting zurückbauen (v1.14.2) | **fertig** (UAT U10 bestanden) |
| 3 | Chart-Redesign: monotone-cubic, Fade-Fill, Glow, Insets (v1.13.0 + v1.14.0) | **fertig** (UAT U1–U9 bestanden, U6-Regression gefixt) |
| 4 | Notifications: 80/95-Thresholds + Window-Reset (v1.5.0 + v1.15.0/1/2) | **fertig** (UAT U11 bestanden, Toast am Schirm bestätigt) |
| 5 | Restfixes: `.Distinct()`, Steilheitsfilter, Output-Tier, Re-Aggregate | **fertig** |

### Verify-Umgebung (für Folgephasen)

- Testbaseline **434/434 grün** (Stand 2026-08-06, inkl. 3 neuer `AxisLabelFitsInGutter`-Fälle).
  Historisch: 345/345 nach Phase 0. Die zwei früher als „pre-existing" geführten
  `ClaudeApiServiceTests`-Fehler waren veraltete Tests (HttpClient-Ära), nicht ein
  Produktionsbug — sie wurden in Phase 0 auf den echten Vertrag
  (`ClaudeApiService.cs:75` `if (responseBody is null) return null;`) nachgezogen.
- Screenshot-Geometrie für `windows-mcp` (Fenster sonst rechts abgeschnitten):
  `window_management set_bounds x=60 y=20 width=625 height=1180`.
- **`screenshot_control target=window` schneidet bei 1.25-DPI rechts ab** — es liefert 625×1180,
  das Fenster ist aber 781×1475 physisch. Stattdessen `target=region x=75 y=20 width=790 height=1480`
  nehmen; `set_bounds` meldet die *sichtbare* Rahmenbreite zurück (765×1467), nicht die logische.
- **Toasts sind per Screenshot nicht prüfbar** — weder `Graphics.CopyFromScreen` noch
  `screenshot_control` erfassen das Banner (empirisch: 24 Frames über 10 s, null geänderte Pixel,
  während der Toast real sichtbar war). Auch `wpndatabase.db` bekommt keinen neuen Zeitstempel.
  Beides sind falsche Negative. Gültige Belege: ein Mensch sieht hin, oder ein Zustands-Seiteneffekt
  (z. B. die Fenster-Rotation in `notification-state.json`).
- **Gesperrte Workstation blockiert Screenshots.** Läuft `LogonUI.exe`, komponiert DWM für
  die Sitzung nicht mehr und `screenshot_control` liefert ein schwarzes Bild — die App läuft
  dabei normal weiter. Prüfen mit `Get-Process LogonUI`. Ersatz: `mcp__windows-mcp__ui_find`
  / `ui_read` lesen den UIA-Baum mit Live-Werten auch im gesperrten Zustand und sind für
  Zahlen/Texte sogar präziser. Nur **pixelbezogene** Prüfungen brauchen eine entsperrte
  Sitzung. Zusatzbefund: bei gesperrter Sitzung feuert auch `ui_click` (UIA-Invoke) keine
  Commands — Navigation in Unterseiten ist damit ebenfalls blockiert.

### Visuelle UAT — Ergebnis (2026-08-06, entsperrte Sitzung, Release-Build)

| # | Zu prüfen | Ergebnis | Beleg |
|---|---|---|---|
| U1 | Kurvenform glatt | **PASS** | Übergänge gerundet, keine 90°-Stufen mehr; 7× Zoom auf echte Daten |
| U2 | kein Überschwingen | **PASS** | 95/60/95 bleibt unter 100 %, 0/100/0 berührt die Gitterlinien exakt und schneidet nirgends durch |
| U3 | Fill fadet zur Baseline | **PASS** | vertikaler Verlauf klar sichtbar, kein flacher Block |
| U4 | Glow dreischichtig | **PASS** | Halo, Farbscheibe, weißer Kern einzeln erkennbar |
| U5 | Glow an allen Rändern frei | **PASS** (mit dokumentierter Toleranz) | Scheibe r≈4,5 px und sichtbare Blur-Schulter frei; nur der Ausläufer <8 % Intensität erreicht bei 11 px Inset die rechte Kante — genau die in `ChartRenderer.GlowInset` begründete Auslegung |
| U6 | Achsenlabels zentriert | **FAIL → gefixt** | „100%"/„50%" brachen auf zwei Zeilen um; Ursache + Fix unten |
| U7 | Chart-Höhe 160 statt 120 | **PASS** | Panel 150 → 200 px bei identischem 1.25-Scale gemessen; alle Abschnitte darunter vollständig |
| U8 | beide Themes | **PASS** | helles Theme über den Dark-Mode-Schalter geprüft, U1/U4/U6 dort identisch sauber |
| U9 | Export deckungsgleich | **PASS** | Export-Replik derselben Punktliste deckt sich mit dem Live-Chart; gemeinsamer Code-Pfad, `ChartAreaHeight == 144` per Test gepinnt |
| U10 | Settings → Allgemein | **PASS** (Zahl im Kriterium falsch) | Sonnet-Zeile weg, Divider gleichmäßig 61 px. Es sind **8 → 7** Zeilen, nicht 7 → 6 — `git show 1571dd3` entfernt genau ein `<Grid Height="40">` plus einen Divider |
| U11 | Toast-Rauchtest | **PASS** | Toast „Nutzungsfenster zurückgesetzt" vom User am Bildschirm bestätigt (2026-08-06 09:50). Unpackaged Toasts funktionieren. Details unten |

**U6 — Ursache und Fix**

Phase 3 stellte die Prozentlabels von `DrawText(text, x, y, …)` auf die **Rect-Überladung** um, um sie
auf ihrer Gitterlinie zu zentrieren. Ein Rect bricht aber um: der Gutter ist
`LeftMargin(22) − AxisLabelGutter(4) = 18 px`, „100%" misst mit Segoe UI Variable 10 aber
**24,36 px** und „50%" **18,96 px** — beide brachen in „100" / „%" um und straddelten damit die Linie,
die sie markieren sollten. „0%" (13,57 px) passte und blieb einzeilig, was den Fehler halb kaschierte.
Vor v1.6 lief der Text über den Rand in die Plotfläche statt umzubrechen, deshalb fiel es nie auf.

Fix: `ChartRenderer.LeftMargin` 22 → 32 (Gutter 28 px), plus `ExportHelperTests.AxisLabelFitsInGutter`
als `[Theory]` über „100%"/„50%"/„0%", die die echte Textbreite gegen `ChartDrawing.AxisLabelRectWidth`
misst. Kein Test hing an der 22 — alle referenzieren die Konstante. Kosten: ~3,4 % Plotbreite.

**U11 — bestanden, mit einer wichtigen Werkzeug-Lehre**

Der Reset-Toast erscheint unter `WindowsPackageType=None`. Ablauf: `notification-state.json` mit
veralteter `windowId` präpariert → nächster Poll rotiert das Fenster, und die Rotation ist im Code
**erst nach** `SendResetToastIfDue(...)` erreichbar. Der User hat den Toast
„Nutzungsfenster zurückgesetzt" (`WindowResetNotificationTitle`, de-DE) am Bildschirm bestätigt.
`AppNotificationManager` ist unpackaged voll funktionsfähig: die AUMID der exakten Release-exe steht
unter `HKCU\Software\Classes\AppUserModelId`, `IsSupported()` true, `Register()` gelaufen. Alle acht
Notification-Resource-Keys existieren in beiden Sprachdateien.

**Lehre für künftige Läufe: Toasts sind nicht per Screenshot prüfbar.** Weder
`Graphics.CopyFromScreen` (GDI/BitBlt) noch `screenshot_control` haben das Banner erfasst — 24 Frames
über 10 s zeigten null geänderte Pixel, während der Toast real auf dem Schirm stand. Auch
`wpndatabase.db` bekam keinen neuen Zeitstempel. Beides sind **falsche Negative**; ein
Kontrollversuch mit einem reinen PowerShell-Toast (Standard-AUMID) war genauso unsichtbar und hätte
fast zu der Fehldiagnose „diese Sitzung stellt gar keine Toasts zu" geführt. Für Toast-Prüfungen
gibt es nur zwei gültige Belege: ein Mensch sieht hin, oder ein beobachtbarer Seiteneffekt im
Zustand (hier die Fenster-Rotation in `notification-state.json`).

**Beobachtung am Wochenfenster (kein Fehler, aber notieren):** beim Auslösen rotierte auch das
Wochenfenster, von `2026-08-08T23:59` auf `2026-08-09T00:00`. Die API lieferte `23:59:59.705` und
beim Folge-Poll `00:00:00.617` — 912 ms Jitter über eine Minutengrenze, also ein anderer
Truncation-Bucket. Genau der in `BuildWindowId` dokumentierte „one extra toast"-Grenzfall. Relevant
ist: das Wochenfenster endet **strukturell** auf `23:59:59`, klebt also systematisch an dieser
Grenze — dort ist der Grenzfall der Regelfall, nicht der Zufall. Falls das im Betrieb nervt, wäre
eine Toleranz von ±2 s beim ID-Vergleich der Hebel, nicht das Aufgeben der Truncation.

**Datenherkunft je Prüfpunkt** (Auflage der Freigabe vom 2026-08-06)

| Prüfpunkt | Daten |
|---|---|
| U1, U4, U6, U7, U8, U9, U10 | echte Live-Daten |
| U2, U5 | **synthetische** Punktmengen über eine Wegwerf-Render-Harness (`ExportHelper.RenderChartToPng` → PNG auf Platte, danach gelöscht) |
| U11 | `notification-state.json` präpariert (veraltete `windowId`) |

`usage-history.json` und `notification-state.json` wurden vorher nach `.bak` gesichert.
`usage-history.json` ist wiederhergestellt und verifiziert (65 Punkte, 06:30–07:23 UTC, max 18 %,
keine synthetischen Reste). `notification-state.json` wurde **bewusst nicht** aus dem `.bak`
zurückgespielt: das Backup trägt die `windowId` eines abgelaufenen Fensters und hätte beim
nächsten Start einen unechten Reset-Toast ausgelöst. Auf disk steht der von der App selbst
geschriebene, korrekte Live-Zustand.

Beide Backups liegen noch unter `%LOCALAPPDATA%\CCInfoWindows\` als
`usage-history.json.bak` und `notification-state.json.bak`. Sie werden nicht mehr gebraucht und
können gelöscht werden — `notification-state.json.bak` sollte man **nicht** zurückspielen
(Begründung oben).

**Zwei Befunde ohne Fix, bewusst offengelassen**

1. **Fill ist bei niedriger Auslastung fast unsichtbar.** Der Fade-Gradient ist am *Plot-Top*
   verankert, nicht an der Kurve (`ChartDrawing.FillAlphaAtTop/AtBaseline`). Bei 5–18 % liegt die
   Fläche komplett im unteren, fast transparenten Bereich — im hellen Theme praktisch nicht mehr
   erkennbar, wo v1.5 noch einen sichtbaren Block zeigte. Kriterium U3 ist erfüllt; wer den Fill
   auch bei kleinen Werten sehen will, müsste den Gradienten an der Kurve verankern. Kandidat v1.7.
2. **Ein Live-Chart über die volle Fensterbreite ist nicht erzeugbar**, solange das 5h-Fenster erst
   ~30 min alt ist. Punkte in der Zukunft überleben zwar das Pruning, aber der Poll hängt den echten
   Messpunkt *hinten* an die Liste — `FilterByMinSpacing` ersetzt damit den letzten Punkt und die
   Kurve klappt zur Glow-Position zurück. Kein Produktfehler (echte Historie ist immer zeitlich
   sortiert), aber die Form-Prüfungen U1–U3 stützen sich deshalb auf den Export-Pfad, der
   Zeile für Zeile dieselben `ChartDrawing`-Methoden aufruft.

**Belegbilder** in `spec/v1.6-uat/` (gitignored): `before-v1.6-full-window.png`,
`after-v1.6-full-window.png`, `after-v1.6-live-chart-dark.png`, `u2-overshoot-95-60-95.png`,
`u2-overshoot-0-100-0.png`, `u5-glow-top-right.png`, `u5-glow-bottom-right.png`,
`u5-glow-left-top.png`, `u9-live-replica.png`, `u8-main-light.png`, `u8-settings-light.png`,
`u10-settings-general.png`, `u11-windows-dnd-state.png`.

Was **nicht** offen ist: alle Zahlen-/Text-Prüfungen wurden per UIA-Baum belegt —
Kontextfenster 31 % bei 309.951 Tokens (Opus 5, 1M-Fenster; bei 200 K wäre auf 100 % geklemmt
worden), Statistik-Zeile „Haiku 4.5, Opus 4.7, Opus 4.8, Opus 5" (dedupliziert und sortiert,
vorher „…, Opus 5, Opus 4.7, Opus 4.8"), und `notification-state.json` mit korrekt
truncateten Window-IDs.

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

# Feature Landscape — v1.5 Milestone (macOS v1.12.0 Feature Parity + Hardening)

**Domain:** Windows desktop port of stefanlange/ccInfo (Claude Code usage monitor)
**Researched:** 2026-05-07
**Scope:** 11 items across 3 clusters (A: parity, B: hardening, C: code-review remediation)
**Verification anchor:** Upstream RELEASENOTES.md v1.12.0 (2026-05-02) verified via WebFetch
**Overall confidence:** HIGH (upstream wording confirmed verbatim; backlog memory files are authoritative for B-cluster)

---

## Cluster A — macOS v1.12.0 Feature Parity (NEW user-facing features)

### A1: Next 5h-window start time label

**Expected user behavior** (verified against upstream verbatim quote):
> "Show the absolute reset time of the 5-hour window below the chart (e.g. 'Mo 1.5. 16:30') next to the existing time-until-reset countdown"

User sees the existing relative countdown ("zurücksetzung in 1 Std. 49 Min.") AND a second small label below/beside it showing the absolute clock time at which the next 5-hour window opens. Format: weekday-abbrev + day.month + 24h clock — e.g. "Mo 1.5. 16:30" (DE) / "Mon May 1, 16:30" (EN). When `FiveHour.ResetsAt` is null (no active window), the label hides entirely (default policy from backlog).

**Concrete spec:**
- New `[ObservableProperty] FiveHourNextWindowStartText` on `MainViewModel`, formatted via `CultureInfo.CurrentUICulture` from existing `data.FiveHour.ResetsAt.LocalDateTime`.
- New TextBlock under the countdown row in `MainView.xaml`, 13px secondary-text typography ramp.
- Format string: short `"ddd HH:mm"` if reset is same calendar day, long `"ddd d.M. HH:mm"` for cross-day (matches upstream "Mo 1.5. 16:30").
- Re-renders on every poll cycle (already covered by existing `RefreshAsync` flow).

**Classification: TABLE STAKES**
Upstream parity item. Once users notice macOS shows it, they expect Windows to match. No new dependency surface, purely additive.

**Complexity: S (Small)**
- Existing data flow: `FiveHour.ResetsAt` already deserialized in `UsageResponse`.
- Existing pattern: `CountdownFormatter.FormatCountdown` shows the relative variant; absolute formatter is a sibling helper.
- Existing localization plumbing: `l:Uids.Uid` + resw entries; same pattern as v1.4 Phase 23 (4 keys added).
- ~2 hours work per backlog estimate. One unit test for cross-day boundary.

**Dependencies on shipped features:**
- `MainViewModel.FiveHourCountdown` ObservableProperty (v1.0)
- `CountdownFormatter` helper (v1.0)
- `l:Uids.Uid` runtime localization (v1.0)
- `IDispatcherTimer`-driven re-render pattern is NOT needed here — countdown already re-renders via existing poll cycle.

---

### A2: Session renaming with persistent custom names

**Expected user behavior** (verified against upstream verbatim quote):
> "Rename any session via the pencil button next to the switcher or the new Sessions tab in Settings; names persist across restarts and replace the auto-derived project name everywhere"

Two parallel entry points + one persistence layer:

1. **Inline pencil button** (Segoe Fluent glyph e.g. `&#xE70F;`) directly next to the session switcher ComboBox in MainView. Click → user enters custom name → save → all references to this session everywhere in the UI now show the custom name (dropdown label, header, statistics, cost panel).
2. **New "Sessions" Settings tab** showing the full known-session list with current display name + edit field per row. Bulk-management surface for renaming, viewing, and (out of scope for v1.5) clearing names.
3. **Persistence:** custom names survive app restart. Storage: `%LOCALAPPDATA%\CCInfoWindows\session-names.json` as `Dictionary<string projectDirName, string customName>`. NOT credential-protected (names aren't secrets; backlog explicitly notes "DPAPI not needed").

**Concrete spec:**
- New `ISessionNameStore` / `SessionNameStore` service (load/save JSON, `GetCustomName(projectDirName)`, `SetCustomName(projectDirName, string?)` where null clears).
- `SessionNameHelper.GetDisplayName(...)` gets a NEW first step: check the store before the existing 3-step fallback chain (cwd → decoded dir name → null). The existing 3 steps remain unchanged so empty stores produce identical behavior to today.
- New `[RelayCommand] RenameSessionAsync(SessionDisplayItem)` on `MainViewModel`.
- New `SettingsSessionsView.xaml` panel + `IsSessionsTabVisible` ObservableProperty on `SettingsViewModel`.
- New `SegmentedItem` in `SettingsView.xaml` Segmented Control (currently 4 tabs: General/Updates/Account/About → grows to 5).
- Reactive refresh: new `SessionNameChangedMessage` triggers `MainViewModel.RefreshSessions` so dropdown + header update without poll wait.

**Classification: TABLE STAKES**
Upstream parity. The pencil button is the headline feature of v1.12.0 — auto-derived project names are notoriously cryptic ("ccInfoWin" / "claude-mem-observer-sessions"), and users with multiple identical-looking sessions need disambiguation.

**Complexity: L (Large)**
- Touches THREE layers: persistence service (new), helper override (existing `SessionNameHelper`), and TWO UI surfaces (inline pencil on MainView + new Settings tab).
- `JsonlService` dependency-direction decision (option a: inject `ISessionNameStore` into JsonlService vs option b: resolve names in MainViewModel) is a non-trivial architectural call — see Open Scope Questions below.
- Settings Segmented Control grows from 4 → 5 tabs — visual fit at 360px width must be re-verified (badge sizing, spacing).
- ~1.5–2 days per backlog estimate.

**Dependencies on shipped features:**
- `SessionNameHelper.GetDisplayName` 3-step fallback chain (v1.0, v1.2 polish)
- `JsonlService.RebuildSessionsList` (v1.0) — current call site of `GetDisplayName`
- Settings Segmented Control framework (v1.3)
- `IDispatcherTimer` adapter pattern (v1.4) — model for any time-aware UI (not strictly needed here, but the messaging-refresh pattern (`SessionTimeoutChangedMessage`) is the precedent for `SessionNameChangedMessage`).
- `SessionInfo.DisplayName` field (v1.0)
- AVOID `WeakReferenceMessenger` for the rename → save → refresh path because of the v1.4 transient-VM GC pitfall. Use direct DI injection for exactly-once delivery (per architecture memory).

---

## Cluster B — Bug Hardening (user-visible behavior corrections)

### B1: Session-dropdown empty on cold start (Cwd hydration + 30-day visibility window)

**Expected user behavior** (from `backlog_session_dropdown_recent_sessions.md`):
After cold start, the "Aktive Sitzung" / "Active Session" ComboBox is populated with sessions whose JSONL files were modified within the configured visibility window (default 30 days). Inactive sessions appear greyed out with tooltip; active sessions appear normal. User can change the visibility window in Settings via a new dropdown with options 7 / 30 / 90 / unlimited days.

**Two underlying defects** (verified in `JsonlService` cs:577-578, 766-777, 798):
1. **Bug 1 — Fragile Cwd hydration:** `data.Cwd` is set only from `entries[0]` and only when previously empty. Tail reading (1 MB window) often lands on entries lacking `cwd`, leaving `data.Cwd` null/empty. `IsValidProjectDirectory` then drops the session, so it never appears in the list.
2. **Bug 2 — No upper bound on session age:** even after Bug 1 is fixed, ALL JSONL files surface regardless of age. User wants 30-day cutoff with configurability.

**Concrete spec:**
- **Phase A (`JsonlService` hardening):**
  - Iterate ALL entries to resolve Cwd (first non-empty wins, not just `entries[0]`).
  - Fallback Cwd surrogate from `SessionNameHelper.DecodeProjectDirectory(projectDirName)` when no entry carries `cwd`.
  - Soften `IsValidProjectDirectory`: keep sessions whose Cwd is empty/unresolvable but whose decoded dir name yields a display name. Mark visually as "no cwd resolved" (greyed/italic) if needed.
  - `Debug.WriteLine` count of dropped sessions + reason for diagnosability.
- **Phase B (Configurable visibility window):**
  - `AppSettings.SessionVisibilityWindowDays` (int, default 30).
  - New ComboBox in Settings General tab with 4 entries: 7 / 30 / 90 / 0 (unlimited).
  - Filter applied in `MainViewModel.RefreshSessions` (display layer), NOT in `JsonlService` — keeps historical data available for cost/stats.
  - New `SessionVisibilityChangedMessage(int newWindowDays)` for reactive re-filter on setting change (mirrors existing `SessionTimeoutChangedMessage`).

**Classification: TABLE STAKES**
This is a silent UX failure — the dropdown looks like the app forgot all the user's work. Reported during normal v1.4 UAT, not edge-case. Already on user's daily-use list. Fixes obvious bug; no upstream parity argument needed.

**Complexity: M (Medium)**
- Phase A is surgical (3-4 line changes in `ParseFileIntoProject` + soften 1 filter predicate + log statement). Risk: subagent-file exclusion (`IsSubagentFile`, cs:642-644) MUST stay intact — regression test required.
- Phase B is wider: new AppSettings property, new resw keys (5 new), new Settings UI row, new reactive message, new filter in `MainViewModel.RefreshSessions`.
- Tests: cold-start scan repro, Cwd-empty + decoded-name-only path, visibility-cutoff boundary, message round-trip.
- Estimate: 1–1.5 days.

**Dependencies on shipped features:**
- `JsonlService.ParseFileIntoProject` + `RebuildSessionsList` (v1.0)
- `SessionNameHelper.DecodeProjectDirectory` (v1.0)
- `IsValidProjectDirectory` filter + UNC path guard (v1.2)
- `MainViewModel.RefreshSessions` (v1.0, v1.4 D-06 removed `IsActive` filter)
- `SessionTimeoutChangedMessage` (v1.4) — pattern for `SessionVisibilityChangedMessage`
- AppSettings persistence layer (v1.0)
- Settings General tab framework (v1.3)

---

### B2: Org-ID picker for multi-account users

**Expected user behavior** (from `backlog_org_id_picker.md`):
Users with multiple Anthropic accounts under the same email (e.g. personal + team) can correctly track usage of their chosen org. The app:
1. Auto-detects suspicious zero-utilization (heuristic: 5+ consecutive polls returning `utilization: 0` while signed in).
2. Shows a banner / Settings notice prompting "this might be the wrong organization".
3. Provides a Settings UI control listing available orgs from `/api/organizations`, letting the user pick and persist the choice in `CCInfoWindows/claude-org` (Credential Manager).
4. Provides an explicit "Re-detect organization" button in the Account Settings tab (force re-resolve, doesn't trust the existing cached value).

**Concrete spec:**
- Extend `ClaudeApiService.TryMigrateOrgIdAsync` with a force-mode parameter (`bool forceReresolve = false`) that bypasses the "already cached" short-circuit.
- New `IClaudeApiService.ListOrganizationsAsync()` returning all orgs the signed-in user belongs to.
- New `SettingsViewModel.AvailableOrgs` ObservableCollection + `SelectedOrg` property.
- New `[RelayCommand] ReDetectOrganizationAsync` on SettingsViewModel.
- Heuristic: counter on `MainViewModel` increments on each `utilization == 0` poll, resets on any non-zero value. At threshold (5) → set `HasOrgMismatchWarning` ObservableProperty → InfoBar banner shown.
- New row in Settings Account tab: "Organization" label + ComboBox + "Re-detect" button.
- Persist override in Credential Manager under existing `claude-org` key (overwrite auto-resolved value).

**Classification: TABLE STAKES**
Silent breakage of the entire app for multi-account users. No upstream equivalent exists yet (macOS likely has the same bug; we're potentially fixing it BEFORE upstream). But because the symptom is "all metrics show 0%", which looks like a fundamental bug, users WILL report this — defending against it is table stakes for trust.

Edge case: this is borderline differentiator-for-now (upstream hasn't shipped it) but classifying as table stakes because it fixes a current zero-trust bug, not a new feature.

**Complexity: M (Medium)**
- Requires verifying the actual `/api/organizations` endpoint exists and returns the expected shape (currently UNVERIFIED — flagged in backlog "Verify before scoping"). If endpoint absent, fallback path (manual org-ID text-entry override) needed.
- Heuristic counter is straightforward but needs careful state management — must not trigger on legitimate idle users (sleeping computer, weekend).
- Banner/InfoBar UI infrastructure already exists (`HasApiError` pattern from v1.4).
- Tests: heuristic boundary, force-reresolve happy path, ListOrganizations error fallback.
- Estimate: 1–1.5 days, +0.5d if endpoint discovery requires research.

**Dependencies on shipped features:**
- `ClaudeApiService.TryMigrateOrgIdAsync` (v1.0)
- Credential Manager `claude-org` slot (v1.0)
- WebViewBridge for API calls (Cloudflare bypass, v1.x)
- `HasApiError` pattern + InfoBar surface (v1.4)
- Settings Account tab framework (v1.3)

**RISK FLAG:** depends on Anthropic API behavior we don't fully control — endpoint availability + rate limits + payload schema unverified. Roadmap should research-flag this phase.

---

### B3: Pricing-service silent-failure surfaced

**Expected user behavior** (from `backlog_pricing_never_loaded.md`):
When `_pricingService.EnsurePricesLoadedAsync()` fails (network, JSON parse, filesystem), the user sees an InfoBar banner / `HasApiError`-style indicator stating "Pricing data unavailable" with a Retry action. About-tab "Last fetched" shows the actual last successful timestamp (or a localized "Never" with explanatory tooltip). Cost analytics columns show "—" instead of "0" or fallback defaults when pricing is unavailable, so users don't mistake degraded state for real zero cost.

**Concrete spec:**
- Replace fire-and-forget catch-all in `MainViewModel.cs:366-370` with proper error propagation:
  - Catch typed exceptions (`HttpRequestException`, `JsonException`, `IOException`); log to `Debug.WriteLine` AND surface to a new `HasPricingError` ObservableProperty.
  - Marshal exception observation to UI thread via `DispatcherQueue.TryEnqueue` (couples with C-2 below — same architectural family).
- New `HasPricingError` + `PricingErrorMessage` ObservableProperties on `MainViewModel`.
- New InfoBar in MainView (or Settings Updates tab) showing the error with a "Retry" button → `RetryPricingLoadCommand`.
- Cost columns show "—" / "n/a" placeholder when `_pricingService.LastFetch == null`.
- Couples with C-cluster M-2: `LastFetchRelativeTime` localization (currently hardcoded EN strings) — same surface, fix together.

**Classification: TABLE STAKES**
Silent degradation that breaks user trust ("why does it say 'Never'?"). The fix is small but essential for cost-analytics integrity.

**Complexity: S (Small)**
- Bulk of work is in one method (`MainViewModel` pricing-load Task.Run block) + 2 ObservableProperties + 1 InfoBar.
- Existing `HasApiError` pattern is the template.
- Couples with C-2 (DispatcherQueue marshaling) and M-2 (localization) — combine into a single phase to amortize test setup.
- Estimate: 0.5 day standalone, ~1 day combined with C-2 + M-2.

**Dependencies on shipped features:**
- `IPricingService` / `LiteLLMPricingService` (v1.0)
- `HasApiError` ObservableProperty + InfoBar pattern (v1.4)
- `SettingsViewModel.LastFetchRelativeTime` (v1.0, currently EN-only — see M-2)
- `IDispatcherTimer` About-tab adapter (v1.4) — consumer of pricing timestamps; will start ticking once pricing loads succeed.

---

## Cluster C — v1.4 Code-Review Remediation (internal hardening, NOT classified)

These are listed for completeness but are NOT classified as table stakes / differentiators / anti-features per scope instructions. They are internal correctness fixes with low/no user-visible surface (except via the bugs they prevent in B-cluster).

| ID | Item | Surface | Couples with |
|----|------|---------|--------------|
| **C-1** | Fire-and-forget exception swallow in `MainViewModel.Receive(AuthStateChangedMessage)` | Internal — auth flow correctness | (B3 same architectural pattern) |
| **C-2** | Missing `DispatcherQueue` marshaling in `Receive(AuthStateChangedMessage)`; candidate for `IDispatcherQueue` adapter mirroring `IDispatcherTimer` | Internal — thread-marshaling correctness | B3 (both consume the same adapter), establishes a project-wide rule for `IRecipient<>` declarations |
| **M-1** | Delete orphan `LogoutRequestedMessage.cs` (dead code from reverted Plan 21-03) | Internal — dead code | none |
| **M-2** | Localize hardcoded EN strings in `LastFetchRelativeTime` | Minor user-visible (German users see EN strings) | B3 (same surface, `SettingsViewModel.LastFetchRelativeTime`) |
| **M-3** | Restore real default for `_contextModelBadgeColor = null!` | Internal — null-correctness | none |
| **Nits** | 3 minor opportunistic cleanups (bundled) | Internal | none |

**Why bundled and unclassified:** These represent quality debt from v1.4 code review. They have no user-facing behavior change in isolation — value is reduced future-bug-risk and code clarity. Roadmap should fold them into the same phases as their B-cluster siblings (C-2 with B3, M-2 with B3) to amortize test setup and keep the touched-surface count low.

---

## Anti-Features (explicitly NOT building in v1.5)

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| Session deletion / archiving from dropdown | Not in upstream v1.12.0; B1's 30-day visibility window already prunes the dropdown | Configure visibility window (B1) |
| Cloud sync of custom session names | Local-only is fine; backlog explicitly defers | `session-names.json` in `%LOCALAPPDATA%` only |
| DPAPI encryption of session names | Names aren't secrets | Plain JSON file |
| Per-session colors / icons / tags | Not in upstream v1.12.0 | Defer to V2-future, only on user request |
| Removing `IsValidProjectDirectory` `Directory.Exists` check entirely | Sessions whose project directory was deleted SHOULD drop | Soften filter only for empty/unresolvable Cwd, keep deletion-detection |
| Multi-account support beyond org-id picker | Out of scope per PROJECT.md "Out of Scope" | B2 picker is the boundary — not a full multi-tenant rebuild |
| Migrating to `WeakReferenceMessenger` for rename → refresh | Known transient-VM GC pitfall (v1.4 architecture memory) | Use direct DI injection or strong-reference messenger for exactly-once flows |

---

## Open Scope Questions (surface for /gsd-discuss-phase)

Scoping decisions that the roadmap drafter should NOT fix unilaterally — they require discussion at phase-discuss time:

### A2 — Settings tab placement (5th tab)
The current Segmented Control has 4 tabs at 360px width: General (green) / Updates (blue) / Account (red) / About (orange). Options for the new "Sessions" tab:

1. **5th tab inserted between General and Updates.** Pro: groups data-management adjacent to general settings. Con: widest layout — must re-verify Segmented Control fits at 360px; badges may need to shrink from 30×30 to 28×28.
2. **5th tab appended at the end (after About).** Pro: minimal disruption to existing tab order. Con: feels like an afterthought; About is conventionally last.
3. **5th tab replacing or merging with another tab** (e.g. consolidate Account+Sessions into one). Pro: keeps 4-tab width. Con: muddies semantics; rejected.
4. **Preferred default for roadmap to plan against:** option 1 (insert between General and Updates), with a width-validation acceptance criterion in Phase X (verify all 5 badges render at 360px without clipping; fall back to 28×28 badges if needed).

### A2 — Pencil rename UI: inline-edit vs modal dialog
1. **Inline edit:** click pencil → ComboBox item swaps to a TextBox + check/cancel mini-buttons → Enter saves. Pro: fast, no context switch, matches Windows 11 modern feel. Con: cramped at 360px width; ComboBox+TextBox+2 buttons in one row is tight.
2. **Modal ContentDialog:** click pencil → modal with TextBox + Save/Cancel → close. Pro: roomy, accessible, easier to localize/test. Con: heavier interaction; one extra click.
3. **Preferred default for roadmap to plan against:** option 2 (ContentDialog) for inline pencil; the Settings Sessions tab uses inline edit per row (no width constraint there). Rationale: the MainView session row is already crowded with the ComboBox and pencil icon; modal avoids layout breakage.

### A2 — `JsonlService` dependency direction (option a vs b)
Per backlog: should `ISessionNameStore` be injected into `JsonlService` (option a, single source of truth) OR resolved one layer up in `MainViewModel.RefreshSessions` (option b, keeps `JsonlService` storage-free)?
- **Option a:** clean callsite — `SessionInfo.DisplayName` is correct everywhere it's read.
- **Option b:** cleaner separation of concerns — `JsonlService` stays a pure parser. But every consumer of `SessionInfo.DisplayName` must remember to overlay the store, easy to miss.
- **Preferred default for roadmap to plan against:** option a, because the v1.4 logout-leak hotfix (D-13) demonstrated that "overlay at every callsite" patterns silently break under refactoring. Single source of truth wins.

### B1 — Visibility-window default value
Backlog specifies default 30 days. Confirm during /gsd-discuss-phase:
- Should an existing user's setting (post-upgrade) default to 30, or honor an "unlimited" default to avoid surprise-pruning of old sessions?
- **Preferred default for roadmap:** 30 days for new installs; existing installs migrate to 30 with a one-time toast notification "Sessions older than 30 days are now hidden — change in Settings".

### B2 — Heuristic threshold tuning
5 consecutive zero-polls is a starting estimate. At 30-second poll intervals, that's 2.5 minutes of zero-utilization before warning. May be too aggressive for users on a quiet day; may be too lenient if user is actively coding and seeing 0% (which IS the bug case).
- **Roadmap: surface as a tuneable constant in code (`OrgMismatchPollThreshold = 5`), revisit after dogfooding.**

---

## Localization Delta Summary (new resw keys, DE + EN)

All new keys follow the existing `l:Uids.Uid` runtime-localization pattern (see v1.4 Phase 23 baseline: 130+ existing keys, 6 new added). Both `de-DE/Resources.resw` and `en-US/Resources.resw` must receive entries.

### A1 — Next-window label
| Key | DE | EN |
|-----|----|----|
| `FiveHourNextWindowStart.Text` | `Neues Fenster: {0}` | `Next window: {0}` |
| (no second key — the `{0}` is the formatted DateTime via `CultureInfo.CurrentUICulture`) | | |

### A2 — Session renaming
| Key | DE | EN |
|-----|----|----|
| `RenameSessionTooltip` (pencil button) | `Sitzung umbenennen` | `Rename session` |
| `RenameSessionDialogTitle` | `Sitzung umbenennen` | `Rename session` |
| `RenameSessionDialogPlaceholder` | `Eigener Name…` | `Custom name…` |
| `RenameSessionDialogSave` | `Speichern` | `Save` |
| `RenameSessionDialogCancel` | `Abbrechen` | `Cancel` |
| `RenameSessionDialogClear` (button to remove custom name) | `Auf Standard zurücksetzen` | `Reset to default` |
| `SettingsSessionsTabHeader` (Segmented tab tooltip / AutomationProperties.Name) | `Sitzungen` | `Sessions` |
| `SettingsSessionsHeader` (panel header, mirrors `SettingsGeneralHeader` style) | `SITZUNGEN` | `SESSIONS` |
| `SettingsSessionsListEmpty` | `Keine Sitzungen gefunden` | `No sessions found` |
| `SettingsSessionsCustomNameLabel` | `Eigener Name` | `Custom name` |
| `SettingsSessionsOriginalNameLabel` | `Ursprünglicher Name` | `Original name` |

### B1 — Session visibility window
| Key | DE | EN |
|-----|----|----|
| `SessionVisibilityWindow.Header` | `Sichtbarkeitszeitraum` | `Visibility window` |
| `SessionVisibilityWindow.7d` | `7 Tage` | `7 days` |
| `SessionVisibilityWindow.30d` | `30 Tage` | `30 days` |
| `SessionVisibilityWindow.90d` | `90 Tage` | `90 days` |
| `SessionVisibilityWindow.Unlimited` | `Unbegrenzt` | `Unlimited` |
| `SessionVisibilityMigrationToast.Text` (one-time on upgrade) | `Sitzungen älter als 30 Tage werden jetzt ausgeblendet. In den Einstellungen änderbar.` | `Sessions older than 30 days are now hidden. Adjustable in Settings.` |

### B2 — Org-ID picker
| Key | DE | EN |
|-----|----|----|
| `SettingsOrganizationLabel` | `Organisation` | `Organization` |
| `SettingsOrganizationReDetectButton` | `Erneut erkennen` | `Re-detect` |
| `OrgMismatchWarning.Title` | `Möglicherweise falsche Organisation` | `Possibly wrong organization` |
| `OrgMismatchWarning.Message` | `Alle Werte zeigen 0 %. Möglicherweise wird die falsche Organisation verfolgt. In den Einstellungen wechseln.` | `All values show 0%. The wrong organization may be tracked. Switch in Settings.` |
| `OrgMismatchWarning.OpenSettingsButton` | `Einstellungen öffnen` | `Open Settings` |

### B3 — Pricing-error surface (couples with M-2 EN-string fix)
| Key | DE | EN |
|-----|----|----|
| `PricingError.Title` | `Preisdaten nicht verfügbar` | `Pricing data unavailable` |
| `PricingError.Message` | `Kostenanalyse läuft mit Fallback-Werten. Verbindung prüfen.` | `Cost analysis is using fallback values. Check connection.` |
| `PricingError.RetryButton` | `Erneut versuchen` | `Retry` |
| `LastFetch.Never` (M-2: currently hardcoded EN, must localize) | `Nie` | `Never` |
| `LastFetch.JustNow` (M-2: relative-time strings) | `Gerade eben` | `Just now` |
| `LastFetch.MinutesAgo` (M-2: format `{0} min ago`) | `vor {0} Min.` | `{0} min ago` |
| `LastFetch.HoursAgo` | `vor {0} Std.` | `{0} h ago` |
| `LastFetch.DaysAgo` | `vor {0} Tagen` | `{0} d ago` |
| `CostUnavailable.Placeholder` (cost columns when pricing failed) | `—` | `—` |

**Total new keys: ~30** (A1: 1, A2: 11, B1: 6, B2: 5, B3+M-2: 9). All require entries in both `de-DE` and `en-US` resw files. `ResourceCoverageTests` (added v1.4) must be extended to assert all new keys present in both locales.

---

## MVP Recommendation (phase ordering for roadmap)

Suggested phase clustering (one phase per cluster of similar surface, per downstream-consumer note):

1. **Phase: A1 + foundational localization.** Smallest, lowest-risk. Establishes the v1.5 cadence. Includes 1 resw key, 1 ObservableProperty, 1 XAML row, 1 unit test.
2. **Phase: B1 (Cwd hardening + visibility window).** Critical UX bug; standalone surface (`JsonlService` + `MainViewModel.RefreshSessions` + Settings General). Independent of A2.
3. **Phase: B3 + C-2 + M-2 (pricing-error + DispatcherQueue + LastFetch localization).** Same surface (`MainViewModel` pricing block + `SettingsViewModel.LastFetchRelativeTime`); coupling reduces test setup.
4. **Phase: A2 (session renaming).** Largest, most architectural. Last in v1.5 because it touches the most surfaces and needs scope-question resolution. Includes new service + helper override + 2 UI surfaces + 5th Settings tab.
5. **Phase: B2 (org-ID picker) — research-flagged.** Depends on Anthropic API endpoint discovery; roadmap should mark as research-flagged. Surface: ClaudeApiService + Settings Account tab + heuristic counter.
6. **Phase: C-cluster cleanup (C-1, M-1, M-3, Nits).** Bundled at end as quality debt; no user-visible behavior. Could be folded as a "wave" inside another phase per v1.4 precedent (Plan 22-04 gap-closure pattern).

**Defer to V2:** none from current backlog. PROJECT.md's V2-* items remain V2.

---

## Sources

- **Upstream RELEASENOTES.md v1.12.0** (2026-05-02) — verbatim quotes verified via WebFetch. HIGH confidence.
- **Backlog memory files** (`backlog_next_window_start_label.md`, `backlog_session_dropdown_recent_sessions.md`, `backlog_org_id_picker.md`, `backlog_pricing_never_loaded.md`) — authoritative for B-cluster behavior. HIGH confidence (user-authored).
- **`SettingsView.xaml`** — current Segmented Control structure (4 tabs at 360px). HIGH confidence (verified 2026-05-07).
- **`SessionNameHelper.cs`** — current 3-step fallback chain. HIGH confidence (verified 2026-05-07).
- **PROJECT.md** — milestone definition, v1.4 baseline, Out-of-Scope policy. HIGH confidence.
- **Architecture memory** (`architecture_weakreferencemessenger_with_transient_vms.md`) — informs the "avoid WeakReferenceMessenger for rename refresh" anti-feature. HIGH confidence.

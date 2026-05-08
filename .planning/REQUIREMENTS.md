# Requirements: ccInfo Windows v1.5

**Milestone:** v1.5 macOS v1.12.0 Feature Parity + Hardening
**Created:** 2026-05-08
**Source:** `.planning/PROJECT.md` (current milestone), `.planning/research/SUMMARY.md` (decisions), `C:\Users\DanielMielke\.claude\projects\D--myProjects-ccInfoWin\memory\backlog_*.md` (verified root causes)

## Goal

Bring CCInfoWindows to upstream stefanlange/ccInfo v1.12.0 feature parity (next 5h-window start time label + session renaming with persistent custom names) while remediating all six v1.4 code-review findings and fixing three reproducible cold-start / silent-failure bugs surfaced during v1.4 UAT.

## v1.5 Requirements

### Cluster A — macOS v1.12.0 Feature Parity

#### NEXTWIN — Next 5h-Window Start Time Label (A1)

- [ ] **NEXTWIN-01**: User sees the absolute reset time of the current 5-hour window displayed below the existing countdown ("Mo 1.5. 16:30" or "Wed 14:30" depending on locale). Format: weekday + 24h clock; cross-day clarity via `"ddd d. MMM HH:mm"` when reset is more than 12h away or after midnight.
- [ ] **NEXTWIN-02**: When `UsageResponse.FiveHour.ResetsAt` is null (no current window), the label is hidden — no "—" placeholder.
- [ ] **NEXTWIN-03**: Label text auto-switches between DE and EN via `CultureInfo.CurrentUICulture` matching the app's localization toggle (`l:Uids.Uid` pattern).

#### RENAME — Session Renaming with Persistent Custom Names (A2)

- [ ] **RENAME-01**: User can click a pencil button next to the session switcher in MainView; this opens a `ContentDialog` with a TextBox pre-filled with the current display name, plus Save and Cancel buttons. Save persists the new name immediately.
- [ ] **RENAME-02**: A new "Sessions" Settings tab (5th segment in the existing Segmented Control, between Account and About) lists all known sessions with inline-editable name fields. Edits save on focus-loss or Enter; clear-name reverts to auto-derived display name.
- [ ] **RENAME-03**: Custom session names persist across app restarts via a JSON file at `%LOCALAPPDATA%\CCInfoWindows\session-names.json`. Schema: `Dictionary<string projectDirName, string customName>`. Storage key is encoded `projectDirName` (= `SessionInfo.Id`), NOT decoded `Cwd`.
- [ ] **RENAME-04**: A renamed session's display name immediately reflects in the MainView session switcher and in any other open Settings Sessions tab without app restart, via `ISessionNameStore.NameChanged` event marshalled through `IDispatcherQueue.TryEnqueue`.
- [ ] **RENAME-05**: Custom names support the same Unicode ranges as the auto-derived display names; control characters U+0000..U+001F and U+007F are stripped before persistence (CVE-2021-42574 mitigation, mirroring macOS reference).
- [ ] **RENAME-06**: A session whose JSONL files are deleted from disk leaves its custom name orphaned in `session-names.json`; orphans are kept across app launches (no auto-prune in v1.5).
- [ ] **RENAME-07**: `ISessionNameStore` follows the G-2 convention (`SemaphoreSlim` write guard, sync + async write methods, atomic-rename via `tmp + File.Move`, `_lastSavedSnapshot` cache), mirroring `IUsageHistoryService`.
- [ ] **RENAME-08**: Display-layer integration: `MainViewModel.RefreshSessionList` resolves the final display name as `_sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName` — `JsonlService` stays storage-free.

### Cluster B — Bug Hardening

#### DROPDOWN — Cwd Hydration + Configurable Visibility Window (B1)

- [ ] **DROPDOWN-01**: After cold start, the "Aktive Sitzung" / "Active Session" ComboBox lists ALL sessions whose JSONL files exist within the configured visibility window — not just sessions that received new tool events since launch.
- [ ] **DROPDOWN-02**: `JsonlService.ParseFileIntoProject` resolves `data.Cwd` from the FIRST non-empty `cwd` field across ALL parsed entries (not just the first entry); when no entry carries `cwd`, fall back to `SessionNameHelper.DecodeProjectDirectory(projectDirName)` as Cwd surrogate.
- [ ] **DROPDOWN-03**: `RebuildSessionsList` no longer drops sessions solely because `IsValidProjectDirectory(s.Cwd)` returns false on empty Cwd; sessions are kept when a display name can be derived from `projectDirName`. The `Directory.Exists`-based filter for *deleted project directories* remains intact.
- [ ] **DROPDOWN-04**: A new Settings option `SessionVisibilityWindowDays` (default 30, options 7 / 30 / 90 / 0=unlimited) appears in the General Settings tab as a ComboBox. Changing it triggers `SessionVisibilityChangedMessage`, and the next session-list refresh applies the new filter at the display layer in `MainViewModel.RefreshSessionList` — NOT in `JsonlService` (stats/cost aggregation must keep all data).
- [ ] **DROPDOWN-05**: Existing installs see a one-time toast notification on first launch after upgrade ("Sessions older than 30 days are now hidden — adjustable in Settings"); tracked by a `SessionVisibilityMigrationShown` boolean in `AppSettings`.
- [ ] **DROPDOWN-06**: The cold-start data-loss race in `JsonlService` (lines written between `Directory.GetFiles` and `stream.Length` capture marked "already read" but never consumed) is fixed: either start the FileSystemWatcher BEFORE `DiscoverSessions`, or use `stream.Position` after final `ReadLine` instead of `stream.Length`. Verified by an explicit data-loss regression test.

#### ORGID — Multi-Account Org-ID Picker (B2)

- [ ] **ORGID-01**: A new "Re-detect organization" button on the Settings Account tab calls `IClaudeApiService.ListAvailableOrganizationsAsync` (using the existing `/api/organizations` endpoint at `ClaudeApiService.cs:163`), shows the available orgs in a `ContentDialog` with name + uuid, and lets the user pick one.
- [ ] **ORGID-02**: Selecting a different org persists the new org-id to `claude-org` Credential Manager key, then triggers the existing `MainViewModel.Logout` sequence — switching orgs requires re-authentication because the WebView2 cookie jar is per-org-context.
- [ ] **ORGID-03**: After 5 consecutive polls returning `utilization: 0` while an active session exists, a dismissible `InfoBar` soft-prompt appears in MainView ("Detected possible organization mismatch — re-resolve?") with a button that opens the same Settings Account → Re-detect dialog. Threshold is a tuneable code constant `OrgMismatchPollThreshold = 5`.
- [ ] **ORGID-04**: The soft-prompt is dismissable with a "Don't show again this session" checkbox; dismissal state lives in-memory only (resets on app restart) — NOT persisted, so a true mismatch reappears next session.
- [ ] **ORGID-05**: All ORGID UI strings are localized in DE and EN (label "Organisation neu erkennen" / "Re-detect organization", InfoBar warning text, dialog headers).

#### PRICING — Pricing-Service Silent-Failure Surfacing (B3)

- [ ] **PRICING-01**: When `_pricingService.EnsurePricesLoadedAsync()` throws, the exception is caught and a dedicated `IsPricingError` flag in `MainViewModel` is set to true; an `InfoBar` (warning level) appears in MainView with text "Pricing data unavailable — cost figures may be inaccurate".
- [ ] **PRICING-02**: When pricing succeeds on a subsequent retry (manual refresh or auto-poll), `IsPricingError` clears and the `InfoBar` disappears.
- [ ] **PRICING-03**: Banner stack policy: at most 2 banners visible simultaneously; `IsPricingError` is suppressed when `IsSessionExpired == true` (auth banner takes priority). Documented as Key Decision in PROJECT.md after Phase 27 ships.

### Cluster C — v1.4 Code-Review Remediation + Dispatcher Foundation

#### DISPATCH — Dispatcher Marshaling Foundation (C-1 + C-2)

- [ ] **DISPATCH-01**: A new `IDispatcherQueue` interface (`Services/Interfaces/IDispatcherQueue.cs`) exposes `bool TryEnqueue(Action action)` and `bool HasThreadAccess { get; }`, mirroring the v1.4 `IDispatcherTimer` pattern.
- [ ] **DISPATCH-02**: A `WinuiDispatcherQueueAdapter` production implementation wraps `Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()` and is registered as singleton in `App.xaml.cs` DI.
- [ ] **DISPATCH-03**: A `FakeDispatcherQueue` test double (in test project) executes actions inline (or queues for explicit pump, configurable per test); replaces every `DispatcherQueue.TryEnqueue` test seam in headless xUnit tests.
- [ ] **DISPATCH-04**: `MainViewModel.Receive(AuthStateChangedMessage)` is refactored: the entire body wraps in `_dispatcherQueue.TryEnqueue(() => HandleCore(...))` (always-TryEnqueue, no `if (!HasThreadAccess)` shortcut). The fire-and-forget Task that previously swallowed exceptions is replaced with an awaited / continuation-handled call that surfaces failures via existing `HasApiError` (C-1 fix).
- [ ] **DISPATCH-05**: A documented project-wide convention G-1 lands in `CLAUDE.md`: every `IRecipient<T>.Receive(T)` body that mutates `[ObservableProperty]`, calls `INavigationService`, or touches XAML controls MUST wrap the body in `IDispatcherQueue.TryEnqueue`. Exception only with `[ThreadSafeReceive]` attribute or inline justification comment.
- [ ] **DISPATCH-06**: A `MessengerThreadingConventionTests` xUnit class enforces G-1 via reflection (or `[RequiresMarshal]` attribute pair if pure reflection proves insufficient — Phase 24 30-min spike decides). Test fails the build when a new `IRecipient<>` handler bypasses the rule.

#### CLEANUP — Trivial Cleanup Wave (M-1 + M-3 + Nits)

- [ ] **CLEANUP-01**: `Messages/LogoutRequestedMessage.cs` is deleted — orphan dead code from reverted Plan 21-03 (M-1).
- [ ] **CLEANUP-02**: `MainViewModel._contextModelBadgeColor = null!` is replaced with a real default initializer (e.g. `ParseHexBrush(...)` matching the gray fallback rendered when no model is yet detected). Aligns with new project-wide convention G-3 ("no `null!` defaults on `[ObservableProperty]`"). (M-3)
- [ ] **CLEANUP-03**: Three opportunistic minor cleanups bundled from the v1.4 code review's Nits list, applied in a single commit (per `.planning/todos/pending/2026-05-07-nits-v14-code-review-cleanups.md`).
- [ ] **CLEANUP-04**: Convention G-3 is documented in `CLAUDE.md`: prefer `= string.Empty;`, `= "--";`, or `= ParseHexBrush(...)` initializers over `null!` for `[ObservableProperty]` fields. M-3 is the precedent fix.

#### L10N — Localization Correctness (M-2 — bundles with PRICING)

- [ ] **L10N-01**: `SettingsViewModel.LastFetchRelativeTime` no longer returns hardcoded EN strings ("just now", "X minutes ago", "X hours ago", "X days ago", "Never"); instead reads from new resw keys `LastFetchRelative.JustNow`, `LastFetchRelative.MinutesAgo`, `LastFetchRelative.HoursAgo`, `LastFetchRelative.DaysAgo`, `LastFetchRelative.Never` in both DE and EN.
- [ ] **L10N-02**: All ~30 new resw keys across NEXTWIN/RENAME/DROPDOWN/ORGID/PRICING/L10N exist in both `Strings/de-DE/Resources.resw` AND `Strings/en-US/Resources.resw`.
- [ ] **L10N-03**: `ResourceCoverageTests` xUnit class (added in v1.4) is extended to validate the v1.5 keys structurally — same XDocument-based check pattern.

## Out of Scope

These were explicitly considered and excluded:

- **Session deletion / archiving from the rename UI** — separate concern; visibility window already gives users a way to hide stale sessions.
- **Cloud sync of custom session names** — local-only persistence is sufficient; DPAPI not needed because names aren't secrets.
- **Per-session colors, icons, or tags** — not in upstream v1.12.0; revisit only if requested.
- **Removing the `IsValidProjectDirectory` `Directory.Exists` check entirely** — sessions whose project directory was deleted should still drop; only "Cwd not yet resolved" gets tolerant.
- **Full multi-account architecture** (separate Credential Manager namespace per account) — out of scope; scope ends at "let the user pick the right org-id".
- **`WeakReferenceMessenger`-based session-rename refresh** — explicitly forbidden per v1.4 D-13 hotfix lesson; use direct DI + singleton `.NET event` instead.
- **Roslyn analyzer for G-1 marshaling rule** — Tier-1 (CLAUDE.md) + Tier-2 (reflection-based xUnit test) is sufficient for v1.5; defer Roslyn analyzer to v1.6+.
- **WinAppSDK 2.0 major-version bump** — defer to v1.6+ or align with future `V2-05` (.NET 10 LTS migration).
- **Pre-existing test failures** (2 `ClaudeApiServiceTests`, 13 `JsonlServiceTests` — parameter naming mismatch, production unaffected) — unchanged from v1.0/v1.3 baselines, future cleanup item.

## Future

Carried forward unchanged from PROJECT.md "Future" section (V2-01 through V2-05) — no new additions in v1.5 scope:

- V2-01: System tray icon
- V2-02: Keyboard shortcuts
- V2-03: Configurable color thresholds
- V2-04: Historical usage trends
- V2-05: .NET 10 LTS migration (couples with WinAppSDK 2.0 bump)

## Traceability

Phase mapping is filled in by the roadmapper in the next workflow step (Step 10). Expected approximate mapping per `SUMMARY.md` Decision 2 build order:

| Category | Items | Expected Phase |
|----------|-------|----------------|
| DISPATCH | DISPATCH-01..06 | Phase 24 (Foundation) |
| DROPDOWN | DROPDOWN-01..06 | Phase 25 |
| RENAME | RENAME-01..08 | Phase 26 |
| NEXTWIN, ORGID, PRICING, L10N | NEXTWIN-01..03, ORGID-01..05, PRICING-01..03, L10N-01..03 | Phase 27 |
| CLEANUP | CLEANUP-01..04 | Phase 28 |

(Roadmapper writes the actual mapping. Above is anticipated structure for cross-checking.)

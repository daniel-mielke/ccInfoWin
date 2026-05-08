# Phase 25: Cold-Start Session Hydration & Visibility Window - Context

**Gathered:** 2026-05-08
**Status:** Ready for planning
**Mode:** Smart-discuss auto-resolved (autonomous run; defaults from research/PITFALLS.md + backlog memory)

<domain>
## Phase Boundary

Phase 25 fixes the cold-start "Active Session" ComboBox empty bug by hardening Cwd hydration in `JsonlService.ParseFileIntoProject` (resolve from FIRST non-empty cwd across ALL parsed entries), softening the `RebuildSessionsList` filter (drop only on deleted project directory, not on empty Cwd), adding a configurable `SessionVisibilityWindowDays` ComboBox in the General Settings tab (7/30/90/0=unlimited; default 30), wiring a `SessionVisibilityChangedMessage` through `IDispatcherQueue.TryEnqueue` (G-1 compliant), shipping a one-time migration toast for existing installs, and closing a cold-start data-loss race in `JsonlService` between `Directory.GetFiles` and `stream.Length` capture.

**Strict scope:** Display-layer filter for visibility window — `JsonlService` continues to aggregate stats across ALL sessions (cost/quota totals stay intact). No subagent-file behavior change. No `IsValidProjectDirectory` `Directory.Exists` removal — only the empty-Cwd path is softened.

</domain>

<decisions>
## Implementation Decisions

### D-01: Cwd hydration mechanism (DROPDOWN-02)
- **Resolve `data.Cwd` per-entry inside `ApplyEntryToProjectData`**, taking the FIRST non-empty `cwd` across ALL parsed entries (not just `entries[0]` at JsonlService.cs:577-578). Tail-window reads frequently land on entries without `cwd` field; per-entry resolution stabilizes hydration.
- **Fallback when no entry carries `cwd`:** derive from `SessionNameHelper.DecodeProjectDirectory(projectDirName)` (already exists; produces usable label like `D--myProjects-ccInfoWin` → `D:\myProjects\ccInfoWin`). Apply only after parsing all entries; if still empty, mark in `Debug.WriteLine` for diagnostics.

### D-02: Filter softening (DROPDOWN-03)
- **`RebuildSessionsList` (line 798):** retain `Directory.Exists(Cwd)` check ONLY when Cwd is non-empty. When Cwd is empty/unresolvable, KEEP session if a display name can be derived from `projectDirName`. Sessions whose project directory was deleted still drop (Directory.Exists check on resolved Cwd path).
- **`IsValidProjectDirectory` rule:** stays semantically the same; the call site logic changes — short-circuit on empty Cwd to skip the filesystem check, but still allow display-name fallback path.

### D-03: SessionVisibilityWindowDays mechanism (DROPDOWN-04)
- **Config:** `AppSettings.cs` adds `int SessionVisibilityWindowDays = 30;` (default 30, options 7/30/90/0=unlimited).
- **Settings UI:** General-tab ComboBox (NOT slider — discrete options match macOS v1.12.0 style; keyboard-navigable). Resw keys: `SessionVisibilityWindow.Header`, `.7d`, `.30d`, `.90d`, `.Unlimited`.
- **Filter location:** display layer in `MainViewModel.RefreshSessionList` (line 664). `JsonlService` keeps aggregating ALL sessions — stats/cost totals must not lose data.
- **Reactive refresh:** new `SessionVisibilityChangedMessage(int newWindowDays)`. `MainViewModel` becomes `IRecipient<SessionVisibilityChangedMessage>`. Receive body wraps in `_dispatcherQueue.TryEnqueue(RefreshSessionList)` per G-1.
- **Filter expression** (display layer):
  ```csharp
  var cutoff = settings.SessionVisibilityWindowDays > 0
      ? DateTimeOffset.UtcNow.AddDays(-settings.SessionVisibilityWindowDays)
      : DateTimeOffset.MinValue;
  var visible = latestSessions.Where(s => s.LastActivity >= cutoff);
  ```

### D-04: Migration toast (DROPDOWN-05)
- **Trigger condition:** existing install + first launch on this version + `SessionVisibilityMigrationShown == false`.
- **Storage:** new `bool SessionVisibilityMigrationShown` in `AppSettings` (default `false`). Set `true` after toast dismiss.
- **Toast text (DE):** "Sitzungen älter als 30 Tage werden jetzt ausgeblendet — anpassbar in Einstellungen."
- **Toast text (EN):** "Sessions older than 30 days are now hidden — adjustable in Settings."
- **Display mechanism:** WinUI 3 `InfoBar` in MainView (top of dashboard area), severity Informational, `IsClosable=true`. NOT a Windows Toast Notification — keeps the surface in-app and consistent with `IsPricingError` InfoBar pattern (Phase 27 PRICING-01).

### D-05: Cold-start data-loss race fix (DROPDOWN-06)
- **Root cause:** Between `Directory.GetFiles` enumeration and `stream.Length` capture in `ApplyEntryToProjectData`, the file may grow. Lines written in the gap are marked "already read" by the position-capture but never consumed.
- **Fix:** Use `stream.Position` after the final `ReadLine` instead of `stream.Length` (JsonlService.cs:444, :458, :470). After all lines drain from the buffer, `stream.Position` equals the byte offset of the last successfully-parsed line; subsequent file growth is correctly handled by FileSystemWatcher.
- **Alternative considered:** Start `FileSystemWatcher` BEFORE `DiscoverSessions`. Rejected — increases the buffered-event window during DI startup; events fire before `_projectData` is initialized, requiring extra null-check defensive code.
- **Regression test (DROPDOWN-06):** explicit xUnit test that writes new lines to a JSONL file DURING the parse window (between mock `Directory.GetFiles` return and `stream.Position` capture) and asserts those lines are NOT silently dropped. Use a barrier or controlled event pump in `FakeJsonlSource` to deterministically reproduce the race.

### Carrying Forward (locked from prior phases — not re-discussed)

- **L-01:** `IDispatcherQueue` is constructor-injected into `MainViewModel` (Phase 24 D-01). Phase 25 reuses the existing field — no constructor surgery.
- **L-02:** G-1 convention applies to `Receive(SessionVisibilityChangedMessage)`. The body MUST call `_dispatcherQueue.TryEnqueue(RefreshSessionList)` — no shortcut. `MessengerThreadingConventionTests` (Phase 24 DISPATCH-06) catches violations.
- **L-03:** Memory note `architecture_v1_5_dispatcher_marshaling_conventions.md` G-2 (SemaphoreSlim JSON store) is NOT consumed in Phase 25 — `AppSettings` writes already use the existing settings persistence path. Phase 26 is the first G-2 consumer.
- **L-04:** `SessionActivityThresholdMinutes` semantics stay unchanged (active/inactive label control only). Phase 25 does NOT touch the active/inactive distinction — purely visibility filter on top.

### Out of Scope (explicit)

- **O-01:** Removing `IsValidProjectDirectory` `Directory.Exists` check entirely. Deleted-directory sessions still drop per DROPDOWN-03 wording.
- **O-02:** Subagent file handling (`IsSubagentFile` at JsonlService.cs:642-644). Stays excluded from session list.
- **O-03:** Performance optimization for huge `_projectData` dictionaries. Current ≤10 projects is fine; revisit if cold-start exceeds 1s.
- **O-04:** Persistent dismissal of migration toast across reinstalls. The `SessionVisibilityMigrationShown` flag lives in `AppSettings` only — re-shown on settings.json reset.
- **O-05:** Auto-prune of orphan custom session names (Phase 26 RENAME-06).

### Claude's Discretion (with anchored guidance)

- **CD-01: ComboBox vs Segmented Control for SessionVisibilityWindow.** Anchor: PROJECT.md key decisions favor ComboBox for >3 discrete options (already used for `SessionTimeoutMinutes`). Recommendation: ComboBox. Plan Phase confirms or overrides based on Settings tab visual density.
- **CD-02: Migration toast lifecycle.** When toast is dismissed, `SessionVisibilityMigrationShown = true` is persisted IMMEDIATELY (not on app shutdown). Reason: app crash between dismiss and shutdown re-shows the toast on next launch — annoyance bug. Plan Phase verifies the dismiss handler triggers `_settingsService.SaveSettings()` synchronously.
- **CD-03: How to reproduce the data-loss race in test.** Two viable patterns: (a) abstract `IFileSystemAdapter` to inject a `Read` that yields control mid-stream; (b) use a `MemoryStream` with controlled `Position` mutations during `Read`. Plan Phase decides — (b) is simpler (no production interface change), (a) is closer to real-world (filesystem timing). Recommendation: (b) for test simplicity; document as known-test-shape limitation.
- **CD-04: SessionVisibilityChangedMessage receipt site.** Two options: (a) MainViewModel directly handles via `IRecipient<SessionVisibilityChangedMessage>`; (b) JsonlService handles and re-emits a derived `SessionsRefreshedMessage`. Recommendation: (a) — mirrors `SessionTimeoutChangedMessage` precedent (Phase 22 D-08). JsonlService stays storage-only.
- **CD-05: Where the migration check fires.** Two options: (a) `App.xaml.cs OnLaunched` (before MainWindow shows); (b) `MainViewModel.InitializeAsync` first call. Recommendation: (b) — settings already loaded at that point; UI thread guaranteed; toast InfoBar lives in MainView so visibility surface is ready. Trigger via setting `IsSessionVisibilityMigrationToastVisible = true` and binding `InfoBar.IsOpen` to it.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase 25 deliverable scope
- `.planning/REQUIREMENTS.md` §"Cluster B1 — DROPDOWN" lines 60-66 — DROPDOWN-01..06 are the 6 locked requirements.
- `.planning/ROADMAP.md` §"Phase 25" success criteria — 5 criteria.

### Architectural research and decisions
- `.planning/research/PITFALLS.md` §"B1 Cluster" — B1-P1 fragile Cwd hydration, B1-P2 over-restrictive filter, B1-P3 file-watcher race.
- Memory note `backlog_session_dropdown_recent_sessions.md` — full root-cause analysis with disk-state evidence and fix plan structure.
- `.planning/research/SUMMARY.md` Decision 4 (visibility window default 30d, options 7/30/90/0=unlimited).

### In-tree code anchors (must read before edits)
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:542-580` — `ParseFileIntoProject` (Cwd hydration site).
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:577-578` — current `firstEntry.Cwd` assignment (the bug).
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:766-779` — `IsValidProjectDirectory` definition.
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:779-803` — `RebuildSessionsList` filter site (line 798).
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:412-470` — `stream.Length` capture sites for DROPDOWN-06 race fix.
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:499-540` — `DiscoverSessions` cold-start path.
- `CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs` — `DecodeProjectDirectory` and `GetDisplayName` helpers (Cwd surrogate source).
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` — add `SessionVisibilityWindowDays` and `SessionVisibilityMigrationShown` properties.
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` and `SettingsViewModel.cs` — ComboBox surface for new setting.
- `CCInfoWindows/CCInfoWindows/Messages/` — new `SessionVisibilityChangedMessage.cs` (mirror `SessionTimeoutChangedMessage` shape).
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:50` — add `IRecipient<SessionVisibilityChangedMessage>` declaration.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:316` — register the new message in `InitializeAsync`.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:664` — `RefreshSessionList` (filter site for visibility window).
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — InfoBar surface for migration toast.

### Localization targets
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` and `en-US/Resources.resw` — add 5 keys for `SessionVisibilityWindow.*` plus 2 for migration toast text.

### Test target
- `CCInfoWindows.Tests/Services/JsonlServiceColdStartTests.cs` — new file. Cold-start hydration tests + DROPDOWN-06 data-loss regression test.
- `CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs` — used to verify `SessionVisibilityChangedMessage` Receive marshaling.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`SessionTimeoutChangedMessage` precedent (Phase 22 D-08):** exact pattern to mirror for `SessionVisibilityChangedMessage`. Existing receive at MainViewModel.cs:1043 already G-1 compliant (uses `_dispatcherQueue.TryEnqueue(RefreshSessionList)`).
- **`IDispatcherQueue` (Phase 24):** constructor-injected, non-null. Receive body wrapper boilerplate already in place.
- **`InfoBar` pattern from auth banner:** `IsSessionExpired` drives the existing auth InfoBar in MainView. New `IsSessionVisibilityMigrationToastVisible` follows the same shape — IsOpen binding + Severity=Informational.
- **`SessionNameHelper.DecodeProjectDirectory`:** already exists, fully tested. Phase 25 only needs to call it as Cwd surrogate when no entry carries `cwd`.
- **`MessengerThreadingConventionTests` (Phase 24 DISPATCH-06):** automatically validates the new `Receive(SessionVisibilityChangedMessage)` is G-1 compliant.

### Established Patterns
- **`[ObservableProperty]` for new MainViewModel state** (`IsSessionVisibilityMigrationToastVisible`).
- **Settings ComboBox-bound `[ObservableProperty]` in `SettingsViewModel`:** mirrors `SessionTimeoutMinutes` ComboBox shape.
- **Display-layer filter (D-08 precedent):** filter ComboBox sources, NOT JsonlService aggregations.

### Integration Points
- **`MainViewModel.InitializeAsync`:** add `Register<SessionVisibilityChangedMessage>(this)` next to existing register at line 316. Add migration check after settings load.
- **`MainViewModel.RefreshSessionList` (line 664):** apply visibility cutoff filter at the end of the LINQ pipeline, before assigning to `SortedSessions`.
- **`JsonlService.ParseFileIntoProject`:** modify per-entry Cwd update (move from `entries[0]`-only assignment to per-entry inside `ApplyEntryToProjectData`).
- **`JsonlService` post-parse:** if `data.Cwd` still empty, set to `SessionNameHelper.DecodeProjectDirectory(projectDirName)`. Log via `Debug.WriteLine`.
- **`JsonlService.RebuildSessionsList` line 798:** change filter from `IsValidProjectDirectory(s.Cwd)` to `string.IsNullOrEmpty(s.Cwd) || Directory.Exists(s.Cwd)` (keep when Cwd is empty OR exists; drop only when Cwd is non-empty and directory doesn't exist).
- **`SettingsViewModel`:** add `SelectedVisibilityWindowIndex` (ComboBox-bound) and `OnSelectedVisibilityWindowIndexChanged` partial method that emits `SessionVisibilityChangedMessage` via `WeakReferenceMessenger.Default.Send`.
- **`SettingsView.xaml`:** new ComboBox in General tab between SessionTimeout and Sonnet-context settings.

</code_context>

<specifics>
## Specific Ideas

- **AppSettings additions:**
  ```csharp
  public int SessionVisibilityWindowDays { get; set; } = 30;
  public bool SessionVisibilityMigrationShown { get; set; }
  ```

- **`SessionVisibilityChangedMessage` shape (mirror SessionTimeoutChangedMessage):**
  ```csharp
  namespace CCInfoWindows.Messages;
  public sealed record SessionVisibilityChangedMessage(int NewWindowDays);
  ```

- **MainViewModel.Receive (G-1 compliant):**
  ```csharp
  public void Receive(SessionVisibilityChangedMessage message)
  {
      // Dispatched to UI thread — RefreshSessionList requires it.
      _dispatcherQueue.TryEnqueue(RefreshSessionList);
  }
  ```

- **Filter sketch in RefreshSessionList:**
  ```csharp
  var settings = _settingsService.LoadSettings();
  var cutoff = settings.SessionVisibilityWindowDays > 0
      ? DateTimeOffset.UtcNow.AddDays(-settings.SessionVisibilityWindowDays)
      : DateTimeOffset.MinValue;
  var displayItems = latestSessions
      .Where(s => s.LastActivity >= cutoff)
      .OrderByDescending(s => s.LastActivity)
      .Select(s => new SessionDisplayItem { ... })
      .ToList();
  ```

- **DROPDOWN-06 regression test sketch:**
  ```csharp
  [Fact]
  public async Task ParseFileIntoProject_LinesWrittenDuringRace_AreNotDropped()
  {
      // Arrange: write 3 lines, capture initial position, write 2 more lines mid-parse
      var (path, initialContent) = WriteThreeLinesToTempJsonl();
      var streamProxy = new ControllableStreamProxy(File.OpenRead(path));
      // Act: parse, inject 2 new lines BETWEEN line 3 read and Position capture
      streamProxy.OnAfterReadLine = i => { if (i == 3) AppendTwoLines(path); };
      var data = new ProjectData("test");
      _service.ParseFileIntoProject(path, data, forceFullRead: true);
      // Assert: subsequent re-parse picks up the 2 injected lines (no silent drop)
      _service.ParseFileIntoProject(path, data, forceFullRead: false);
      Assert.Equal(5, data.EntryCount);
  }
  ```

- **Migration toast XAML sketch:**
  ```xml
  <InfoBar IsOpen="{x:Bind ViewModel.IsSessionVisibilityMigrationToastVisible, Mode=TwoWay}"
           Severity="Informational"
           IsClosable="True"
           Message="{x:Bind l:Localizer.Get('Toast.SessionVisibilityMigration')}"
           Closed="OnMigrationToastClosed" />
  ```

</specifics>

<deferred>
## Deferred Ideas

- **G-2 SemaphoreSlim pattern:** Phase 26 is first consumer (ISessionNameStore JSON persistence).
- **Session list paging for very large `_projectData`:** out of scope; current scale is fine.
- **Persistent migration-toast dismissal across reinstalls:** out of scope; settings.json reset re-shows.
- **NEXTWIN / ORGID / PRICING / L10N features:** Phase 27.
- **Subagent file inclusion in dropdown:** out of scope; never user-facing.

</deferred>

---

*Phase: 25-Cold-Start-Session-Hydration-Visibility-Window*
*Context gathered: 2026-05-08 (smart-discuss auto-resolved from backlog memory + REQUIREMENTS.md + research artifacts)*

# Phase 26: Persistent Session Renaming - Context

**Gathered:** 2026-05-08
**Status:** Ready for planning
**Mode:** Smart-discuss auto-resolved (autonomous run; defaults from research/SUMMARY.md + ROADMAP success criteria + memory architecture notes)

<domain>
## Phase Boundary

Phase 26 ships persistent session renaming via TWO entry points (pencil button next to MainView session switcher + new "Sessions" Settings tab as 5th segment between Account and About) with custom names persisted to `%LOCALAPPDATA%\CCInfoWindows\session-names.json`. The store is a new `ISessionNameStore` service following convention G-2 (SemaphoreSlim write-guard, sync+async APIs, atomic-rename via tmp+File.Move, _lastSavedSnapshot cache). Display-layer integration is a single-line resolution `_sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName` in `MainViewModel.RefreshSessionList`. Cross-VM rename → refresh propagation uses an `ISessionNameStore.NameChanged` .NET event marshalled through `IDispatcherQueue.TryEnqueue` — NOT a `WeakReferenceMessenger` broadcast (D-13 lesson honored).

**Strict scope:** No auto-prune of orphaned custom names (RENAME-06 explicitly defers). Storage key is encoded `projectDirName` (= `SessionInfo.Id`), NOT decoded `Cwd` — so renames survive Cwd hydration changes from Phase 25. Control characters U+0000..U+001F and U+007F stripped before persistence (CVE-2021-42574 mitigation). JsonlService stays storage-free.

</domain>

<decisions>
## Implementation Decisions

### D-01: ISessionNameStore shape and convention G-2 (RENAME-07)
- **Interface (`Services/Interfaces/ISessionNameStore.cs`):**
  ```csharp
  public interface ISessionNameStore
  {
      string? GetCustomName(string sessionId);
      void SetCustomName(string sessionId, string customName);
      void ClearCustomName(string sessionId);
      Task<bool> SaveAsync(CancellationToken ct = default);
      bool Save();  // sync — for shutdown handlers
      event EventHandler<SessionNameChangedEventArgs>? NameChanged;
  }
  ```
- **Production implementation (`Services/SessionNameStore.cs`):** mirrors `UsageHistoryService` G-2 pattern exactly:
  - `SemaphoreSlim _writeLock = new(1, 1);`
  - In-memory `Dictionary<string, string> _names` snapshot
  - `_lastSavedSnapshot` cache to avoid redundant disk writes
  - Atomic rename via `tmp + File.Move` for crash-safe writes
  - Sync `Save()` and async `SaveAsync()` both acquire the same `_writeLock`
- **DI registration:** Singleton in `App.xaml.cs ConfigureServices` next to `IUsageHistoryService`.

### D-02: JSON file location and schema (RENAME-03)
- **Path:** `%LOCALAPPDATA%\CCInfoWindows\session-names.json` (mirrors `usage-history.json` neighbor).
- **Schema:** `Dictionary<string, string>` where key = encoded `projectDirName` (= `SessionInfo.Id`, e.g. `D--myProjects-ccInfoWin`) and value = custom name (e.g. `"My Important Project"`).
- **Why encoded key:** Cwd resolution can change (Phase 25 D-01 introduces fallback). The encoded `projectDirName` is the stable filesystem-derived identifier — survives Cwd hydration changes.
- **Empty value semantics:** `customName == ""` is treated as "cleared" (display falls back to auto-derived name). `_sessionNameStore.GetCustomName(id)` returns `null` for cleared/unset entries.

### D-03: Pencil button + ContentDialog (RENAME-01)
- **Button placement:** immediately right of the session ComboBox in MainView. Icon: `` (Pencil glyph from Segoe MDL2 Assets) — matches WinUI 3 idiom for inline edit affordance.
- **ContentDialog content:**
  - Title: localized "Rename Session" / "Sitzung umbenennen"
  - TextBox pre-filled with the current displayed name (custom OR auto-derived)
  - PrimaryButton: "Save" / "Speichern"
  - SecondaryButton: "Cancel" / "Abbrechen"
  - Tertiary "Reset" button: clears custom name (reverts to auto-derived). Shown only if a custom name currently exists.
- **MaxLength:** 100 chars (matches typical session-name length).
- **Save flow:** strip control chars per RENAME-05 → call `_sessionNameStore.SetCustomName(s.Id, sanitized)` → `_sessionNameStore.SaveAsync()` (background) → `NameChanged` event fires → `MainViewModel` handler marshals via `_dispatcherQueue.TryEnqueue(RefreshSessionList)`.

### D-04: Sessions Settings tab (RENAME-02)
- **Tab position:** 5th segment in existing `SegmentedControl`, between "Account" (4th) and "About" (would-be-5th, becomes 6th).
- **Layout per session row:** `Grid` with 3 columns: `[ProjectDirName | TextBox(custom name) | ClearButton]`.
- **Edit semantics:**
  - TextBox `LostFocus` event commits the edit (call `SetCustomName` + `SaveAsync`).
  - `Enter` key commits (handler in code-behind delegates to `[RelayCommand]` on `SettingsViewModel`).
  - Empty TextBox on commit clears the custom name (calls `ClearCustomName`).
- **Initial population:** `SettingsViewModel.SessionRenameItems` = `_jsonlService.Sessions.Select(s => new SessionRenameItem { Id, DefaultName, CustomName })`. Refresh on `NameChanged` event AND on tab switch.
- **Layout fit at 360px:** Per ROADMAP success criterion, badges 30×30 with documented 28×28 fallback if clipping observed. Plan Phase verifies via `Window.Width=360` measurement during the 30-min layout spike.

### D-05: Display-layer resolution (RENAME-08)
- **Site:** `MainViewModel.RefreshSessionList` (line 664).
- **Expression (after the existing display name derivation):**
  ```csharp
  var displayName = _sessionNameStore.GetCustomName(s.Id) ?? autoDerivedName;
  ```
- **Single source of truth:** `JsonlService.SessionInfo.DisplayName` continues to be the auto-derived name; `ISessionNameStore` ONLY layers on top in the display layer. JsonlService never reads or writes `session-names.json`.

### D-06: Cross-VM propagation via .NET event, NOT WeakReferenceMessenger (RENAME-04)
- **Mechanism:** `ISessionNameStore.NameChanged` is a standard `EventHandler<SessionNameChangedEventArgs>`. `MainViewModel.InitializeAsync` subscribes via `_sessionNameStore.NameChanged += OnSessionNameChanged;`. The handler wraps in `_dispatcherQueue.TryEnqueue(RefreshSessionList)` per G-1.
- **Why not WeakReferenceMessenger:** D-13 lesson — `MainViewModel` is `AddTransient`, `WeakReferenceMessenger` would silently drop the recipient on GC. Singleton-service .NET event is the correct exactly-once delivery mechanism (CLAUDE.md cross-VM communication priority: direct DI > singleton-service .NET event > `WeakReferenceMessenger`).
- **Lifecycle:** `MainViewModel.Dispose()` (or equivalent) unsubscribes via `-=`. Phase 26 verifies disposal hook exists or adds it.
- **`MessengerThreadingConventionTests`:** This test only inspects `IRecipient<>` — does NOT cover .NET event subscriptions. Plan Phase considers a separate convention test for event subscriptions, OR documents the rule in CLAUDE.md without test enforcement (pragma: low pattern frequency in v1.5).

### D-07: Control character stripping (RENAME-05)
- **Sanitizer:** `SessionNameSanitizer.Strip(string input)` — pure helper in `Helpers/`. Removes Unicode codepoints U+0000..U+001F and U+007F.
- **Implementation sketch:**
  ```csharp
  public static string Strip(string input)
  {
      if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
      var sb = new StringBuilder(input.Length);
      foreach (var c in input)
          if (c >= 0x20 && c != 0x7F) sb.Append(c);
      return sb.ToString();
  }
  ```
- **Application sites:** every `SetCustomName` entry point — pencil ContentDialog Save handler AND Settings tab TextBox commit handler. Belt-and-suspenders: also apply in `SessionNameStore.SetCustomName` itself.
- **CVE reference:** Trojan Source / Bidi-control characters (CVE-2021-42574) — stripping U+0000..U+001F + U+007F covers the C0/C1 attack surface. Bidi codepoints (U+202A..U+202E, U+2066..U+2069) are NOT stripped — same scope as macOS reference; Plan Phase notes this as known gap if a stricter policy is desired in v1.6+.

### D-08: Orphan retention (RENAME-06)
- **Behavior:** Sessions whose JSONL files are deleted from disk leave their entry in `session-names.json` orphaned. Orphans are kept across app launches.
- **Auto-prune deferred:** to v1.6+. Rationale: a deleted JSONL might reappear later (file restore, project re-open); prematurely pruning loses the user's rename effort.
- **Visibility:** orphan entries are invisible in MainView (no matching session) but visible in Settings Sessions tab as greyed-out rows with a "Session not found" subtitle. Plan Phase decides whether to ship this UX nuance in Phase 26 or defer.

### Carrying Forward (locked from prior phases)

- **L-01:** `IDispatcherQueue` is constructor-injected into `MainViewModel` (Phase 24). Phase 26 reuses the existing field.
- **L-02:** G-1 — handler subscribed to `ISessionNameStore.NameChanged` MUST wrap mutations in `_dispatcherQueue.TryEnqueue`. Documented inline.
- **L-03:** G-2 (SemaphoreSlim JSON store pattern) — Phase 26 is the FIRST consumer beyond `UsageHistoryService`. Pattern ratified.
- **L-04:** Phase 25 stable session list (Cwd hydration + visibility filter) is the substrate the rename UI binds to. No re-litigation of Cwd resolution.

### Out of Scope (explicit)

- **O-01:** Auto-prune of orphan custom names — deferred to v1.6+.
- **O-02:** Bidi-control character stripping (U+202A..U+202E, U+2066..U+2069) — same scope as macOS reference.
- **O-03:** Bulk rename / multi-select rename UI — not in macOS v1.12.0 reference.
- **O-04:** Rename history / undo — not in scope.
- **O-05:** Custom-name export/import (e.g. as part of Settings backup) — out of scope.
- **O-06:** Convention test for .NET event subscription threading — Plan Phase decides; default is "documented in CLAUDE.md, not test-enforced".
- **O-07:** NEXTWIN / ORGID / PRICING / L10N features — Phase 27.
- **O-08:** CLEANUP wave — Phase 28.

### Claude's Discretion (with anchored guidance)

- **CD-01: Settings tab insertion mechanism.** The existing `SegmentedControl` has 4 items (General, Account, About, plus one more). Plan Phase needs to: (1) inspect the existing XAML structure, (2) decide between adding a 5th `SegmentedControlItem` directly OR wrapping in a TabControl-like view-switcher. Recommendation: direct 5th item — minimal surface change. Verify the 360px width fits 5 items without overlap.
- **CD-02: Pencil-button visual integration.** The ComboBox already has a width budget. Plan Phase decides between (a) shrinking ComboBox by ~32px to fit pencil button on the same row, OR (b) absolute-positioning the pencil button overlapping the ComboBox right edge. Recommendation: (a) — accessibility-friendly (separate hit targets, screen-reader-friendly labels).
- **CD-03: Settings tab data binding.** `SessionRenameItems` could be (a) a snapshot collection refreshed on tab activation, OR (b) a live `ObservableCollection<SessionRenameItem>` synchronized with `_jsonlService.Sessions`. Recommendation: (a) — simpler; tab activation is a natural refresh point. Avoid live ObservableCollection sync complexity (PITFALLS Cluster A precedent for stale-snapshot bugs).
- **CD-04: NameChanged event arg shape.** `SessionNameChangedEventArgs` could include just `string SessionId` (and consumers re-resolve), OR include both `string SessionId` + `string? NewCustomName + string? OldCustomName`. Recommendation: just `SessionId` — consumers re-resolve via `_sessionNameStore.GetCustomName`. Simpler event payload; no stale-data risk if multiple changes pile up.
- **CD-05: Disposal of NameChanged subscription in MainViewModel.** Two options: (a) implement `IDisposable` on `MainViewModel` (already considered in v1.4 work), (b) rely on app-lifetime singleton retention (no explicit unsubscribe). Recommendation: (a) — explicit `Dispose()` in `MainViewModel` with `-=` cleanup. Ensures no zombie subscriptions if MainViewModel is ever re-resolved (theoretical with `AddTransient`).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase 26 deliverable scope
- `.planning/REQUIREMENTS.md` §"Cluster A2 — RENAME" lines 76-83 — RENAME-01..08 are the 8 locked requirements.
- `.planning/ROADMAP.md` §"Phase 26" success criteria — 5 criteria.

### Architectural research and decisions
- `.planning/research/PITFALLS.md` §"Cluster A" — A2 cluster pitfalls; §"Cross-Cluster" — G-2.
- `.planning/research/SUMMARY.md` Decision 5 — ISessionNameStore + G-2 pattern.
- `.planning/PROJECT.md` Key Decisions table — D-13 (WeakReferenceMessenger + AddTransient pitfall — drives D-06).
- Memory note `architecture_v1_5_dispatcher_marshaling_conventions.md` — G-2 pattern spec.
- Memory note `architecture_weakreferencemessenger_with_transient_vms.md` — D-13 root cause.

### G-2 template (must read before implementing SessionNameStore)
- `CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs:11-29` — D-05 SemaphoreSlim pattern declaration.
- `CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs:58-79` — sync `Save()` implementation.
- `CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs:81-102` — async `SaveAsync()` implementation with the same lock.

### In-tree code anchors (must read before edits)
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:_sessionInfoConstructor` — `SessionInfo.Id` uses encoded projectDirName (D-02 stable key rationale).
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/ISessionNameStore.cs` — NEW interface file.
- `CCInfoWindows/CCInfoWindows/Services/SessionNameStore.cs` — NEW implementation file.
- `CCInfoWindows/CCInfoWindows/Helpers/SessionNameSanitizer.cs` — NEW helper file.
- `CCInfoWindows/CCInfoWindows/App.xaml.cs:ConfigureServices` — DI registration site.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:50` — add NO new IRecipient (D-06 uses .NET event).
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:308-340` — InitializeAsync subscribe site for NameChanged.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:664` — RefreshSessionList display-layer resolution site.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — add `[RelayCommand] OpenRenameDialog()` for pencil button.
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` — add `SessionRenameItems` collection + commit RelayCommands.
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — add pencil button next to ComboBox.
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` — add ContentDialog show handler if XAML-driven dialog isn't sufficient.
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` — add 5th SegmentedControlItem + Sessions panel.
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` and `en-US/Resources.resw` — add ~10-15 new keys for ContentDialog, Settings tab labels, button captions.

### Localization targets
- New resw key prefix: `Settings.Sessions.*` for tab content, `Dialog.RenameSession.*` for ContentDialog, `MainView.RenameButton.*` for pencil tooltip.
- ResourceCoverageTests (Phase 24-extended) auto-validates DE+EN parity.

### Test target
- `CCInfoWindows.Tests/Services/SessionNameStoreTests.cs` — new file. G-2 invariants: SemaphoreSlim non-recursive, atomic-rename crash-safety, sync+async equivalence, control-char stripping.
- `CCInfoWindows.Tests/Helpers/SessionNameSanitizerTests.cs` — new file. Strip behavior for U+0000..U+001F + U+007F; preservation of valid Unicode (incl. emoji, CJK, RTL non-control).
- Optional: `CCInfoWindows.Tests/ViewModels/MainViewModelRenameTests.cs` — verify display-layer resolution + NameChanged → RefreshSessionList propagation.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`UsageHistoryService` G-2 template:** exact pattern to mirror. SessionNameStore implementation should be ~80 LOC (interface 10 + impl 60 + DI 1 + tests).
- **`SegmentedControl` precedent:** Settings tabs are already navigable; adding a 5th item is well-trodden.
- **`ContentDialog` precedent:** existing logout confirmation dialog as template for rename dialog.
- **`IDispatcherQueue` (Phase 24):** for marshaling NameChanged event handler.

### Established Patterns
- **`[RelayCommand]` for view actions:** OpenRenameDialog, SaveCustomName, ClearCustomName.
- **`[ObservableProperty]` for SessionRenameItem state in SettingsViewModel.**
- **`x:Bind`/`Mode=TwoWay`** for TextBox input binding in Settings tab.

### Integration Points
- **`App.xaml.cs ConfigureServices`** — `services.AddSingleton<ISessionNameStore, SessionNameStore>();` next to `IUsageHistoryService` registration.
- **`MainViewModel` constructor** — add `ISessionNameStore _sessionNameStore` parameter (12th, after IDispatcherQueue from Phase 24).
- **`MainViewModel.InitializeAsync`** — `_sessionNameStore.NameChanged += OnSessionNameChanged;`. Handler: `_dispatcherQueue.TryEnqueue(RefreshSessionList);`.
- **`MainViewModel.RefreshSessionList`** — display-layer resolution `_sessionNameStore.GetCustomName(s.Id) ?? autoDerivedName`.
- **`MainViewModel.OpenRenameDialogCommand`** — opens ContentDialog, on Save: sanitize + persist + (NameChanged fires automatically).
- **`SettingsViewModel`** — `ObservableCollection<SessionRenameItem> SessionRenameItems`. Refresh on tab activation + on NameChanged.
- **`MainView.xaml`** — pencil `Button` right of ComboBox, bound to `RelayCommand`. ToolTipService.ToolTip with localized text.
- **`SettingsView.xaml`** — 5th SegmentedControlItem "Sessions"; `ItemsControl` of SessionRenameItem rows.

### Constraints from CLAUDE.md
- Bash rule: every command its own bash call.
- MVVM: no code-behind logic; use [RelayCommand] for view actions.
- Test rule: F.I.R.S.T. — SessionNameStoreTests must use temp directories, dispose properly, no shared state.

</code_context>

<specifics>
## Specific Ideas

- **AppSettings does NOT change** in Phase 26 — `SessionRenameItems` lives in SettingsViewModel only; persistence is via SessionNameStore JSON file, not settings.json.
- **JSON file shape:**
  ```json
  {
    "D--myProjects-ccInfoWin": "Mein Hauptprojekt",
    "D--SAP-Testo-testo-frontend-nextjs": "Testo Frontend"
  }
  ```
- **NameChanged event subscription site** in InitializeAsync (after IDispatcherQueue is set):
  ```csharp
  _sessionNameStore.NameChanged += (sender, args) =>
  {
      _dispatcherQueue.TryEnqueue(RefreshSessionList);
  };
  ```
- **Layout spike resolution:** if 5-tab Segmented Control clips at 360px width, fall back to 28×28 badge size from default 30×30 — documented in PROJECT.md Key Decisions after the spike.

</specifics>

<deferred>
## Deferred Ideas

- **Auto-prune orphan custom names** — v1.6+.
- **Bidi-control character handling** — same scope as macOS ref; not in v1.5.
- **Bulk rename UI** — not in macOS v1.12.0.
- **Rename undo / history** — out of scope.
- **Settings export/import including custom names** — out of scope.
- **Convention test for .NET event subscription threading** — pragma: low pattern frequency, document in CLAUDE.md only.
- **Live ObservableCollection sync of SessionRenameItems** — defer to v1.6+ if perceived snappiness is an issue.

</deferred>

---

*Phase: 26-Persistent-Session-Renaming*
*Context gathered: 2026-05-08 (smart-discuss auto-resolved from REQUIREMENTS, ROADMAP, research artifacts, memory architecture notes)*

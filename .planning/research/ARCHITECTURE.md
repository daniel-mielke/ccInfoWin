# Architecture Research — v1.5 macOS v1.12.0 Feature Parity + Hardening

**Project:** CCInfoWindows v1.5
**Researched:** 2026-05-07
**Mode:** Project Research — subsequent milestone (architectural deltas only)
**Overall confidence:** HIGH (all evidence is in-tree code or v1.4 memory artifacts)

---

## Existing Architecture (treated as fixed)

Verbatim baseline from `.planning/PROJECT.md` and the v1.4-shipped codebase. **No re-research.** Listed only for the integration points the 11 v1.5 items must hook into.

| Layer | Mechanism | Source |
|------|-----------|--------|
| MVVM | `[ObservableProperty]` / `[RelayCommand]` source generators (CommunityToolkit.Mvvm 8.4) | `CCInfoWindows.csproj`, `MainViewModel.cs` |
| DI | `Microsoft.Extensions.DependencyInjection`; services registered in `App.ConfigureServices()` | `App.xaml.cs:137-178` |
| Layout | `Models / ViewModels / Views / Services / Services.Interfaces / Messages / Helpers / Converters` | `CLAUDE.md` |
| Cross-VM messaging | `WeakReferenceMessenger.Default` (broadcast only) | `MainViewModel.cs:301-302`, all `Messages/*.cs` |
| **Pitfall A** | `MainViewModel` is `AddTransient` → `WeakReferenceMessenger` recipient GC silently drops messages | `architecture_weakreferencemessenger_with_transient_vms.md` (v1.4 hotfix) |
| **Pitfall B** | `WeakReferenceMessenger.Send(...)` invokes recipients **synchronously on sender's thread**; HTTP/ThreadPool senders + UI mutations in `Receive` = thread-affinity bug | same memory file, Pitfall #2 |
| Cloudflare bypass | `WebViewBridge` JS `fetch()` → `postMessage` → `WebMessageReceived` → `ConcurrentDictionary<requestId, TaskCompletionSource>` | `Services/WebViewBridge.cs` (referenced from `App.xaml.cs:149-150`) |
| Credentials | `AdysTech.CredentialManager` 3.1, keys `claude-token` and `claude-org` | `App.xaml.cs:148`, memory `cloudflare-fix.md` |
| Navigation | `INavigationService` frame-navigates `LoginView ↔ MainView ↔ SettingsView` | `App.xaml.cs:147` |
| Timer abstraction (precedent) | `IDispatcherTimer` interface + `WinuiDispatcherTimerAdapter` for headless test fakeability | `Services/Interfaces/IDispatcherTimer.cs`, `Services/WinuiDispatcherTimerAdapter.cs` |
| Settings UI | Segmented control, 4 tabs, 360px width | `Views/SettingsView.xaml:35-82` |
| JsonlService | Scans `%USERPROFILE%\.claude\projects\<encoded-dir>\*.jsonl` → `Dictionary<string, ProjectData>` keyed by **encoded `projectDirName`**; raises `DataUpdated` event | `Services/JsonlService.cs:91, 506-540, 779-801` |
| History persistence | `IUsageHistoryService` async/sync hybrid + `SemaphoreSlim` write guard (v1.4) | PROJECT.md "Validated" |
| Pricing | `IPricingService` (LiteLLM); `EnsurePricesLoadedAsync` exception currently swallowed at `MainViewModel.cs:374` and `MainViewModel.cs:811` | `MainViewModel.cs`, B3 backlog |

---

## Architectural Decisions Required

### Decision 1 — A2 Session-Rename Architecture

**Question:** Where does `ISessionNameStore` hook in, how does change-propagation work, and what's the storage key?

#### 1a. Hook point: `ISessionNameStore` resolves at the **display layer (`MainViewModel.RefreshSessionList`)**, NOT inside `JsonlService`.

**Rationale:**
- `JsonlService` is the I/O layer over Claude Code's filesystem; mixing user-preference storage into it violates SRP (Clean Code rule from CLAUDE.md, "Wrap external libraries — never embed third-party API calls directly in business logic"). The "external library" here is the Claude CLI's on-disk format — JsonlService owns reading it; it should not own user-overrides on top of it.
- `JsonlService` already takes an **optional** `ISettingsService` (`JsonlService.cs:106-120`) — the v1.2 "Optional settingsService in JsonlService" Key Decision (PROJECT.md:185) calls this out: optional dependency was added defensively to preserve 13+ test constructors. Adding a second optional dependency `ISessionNameStore` doubles the surface area and re-opens that test-breakage risk.
- The display name is computed in **two** places: `JsonlService.RebuildSessionsList()` line 785 (`SessionNameHelper.GetDisplayName(...)`) AND `MainViewModel.RefreshSessionList()` line 688 (`DisplayName = s.DisplayName`). The simpler intervention is to override at `RefreshSessionList` time:
  ```
  DisplayName = _sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName
  ```
- This keeps `SessionInfo.DisplayName` (from `JsonlService`) as the **derived/auto** name and treats the custom name as a pure UI-layer override. JsonlService stays storage-format-only.

#### 1b. Change propagation: **Direct DI invocation** on `ISessionNameStore`, refresh via existing `_jsonlService.DataUpdated` event OR a direct call.

**Rationale (decisive — references v1.4 hotfix):**
- The v1.4 hotfix lesson (`architecture_weakreferencemessenger_with_transient_vms.md`, "How to apply") is unambiguous: **for exactly-once delivery to a `MainViewModel` registered `AddTransient`, do NOT use `WeakReferenceMessenger`.** A `SessionRenamedMessage` would replicate the exact failure mode that broke logout in production (Plan 21-03).
- `ISessionNameStore` is a `Singleton` service (it owns persistent JSON storage in `%LOCALAPPDATA%\CCInfoWindows\session-names.json`). The "Sessions" Settings tab's ViewModel writes to it; `MainViewModel` reads from it on every `RefreshSessionList`. Push-vs-pull resolution:
  - **Recommended:** `ISessionNameStore` exposes a `.NET event NameChanged`. `MainViewModel.InitializeAsync()` subscribes alongside the existing `_dataUpdatedHandler` and routes through the same `dispatcherQueue.TryEnqueue(RefreshSessionList)` pump.
  - This mirrors the existing pattern at `MainViewModel.cs:355-356` (`_jsonlService.DataUpdated += _dataUpdatedHandler`) — **same pattern, same thread-marshal, no new abstraction.**
- **Anti-pattern to avoid:** Re-introducing a `SessionRenamedMessage` via `WeakReferenceMessenger`. Would re-open Pitfall A.

#### 1c. Storage key: **encoded `projectDirName`** (= `SessionInfo.Id`), NOT decoded `Cwd`.

**Rationale:**
- `SessionInfo.Id` is already the encoded project directory name (`JsonlService.cs:791` — `Id = kvp.Key` where `kvp.Key` is the dict key from `_projectData[projectDirName]`).
- `Cwd` is **derived from the first JSONL entry** (`JsonlService.cs:577-578`) and can be:
  - Empty (file not yet read; cold-start hydration race — exactly the B1 bug)
  - Inconsistent across CLI versions if Claude Code ever changes how `cwd` is serialized
  - Different between subagent and main session files within the same project
- `projectDirName` is the **filesystem-stable identifier** — `%USERPROFILE%\.claude\projects\D--myProjects-ccInfoWin` exists as a directory regardless of file content; if the user moves the project, the encoded dirname follows because Claude CLI re-encodes it.
- **Tradeoff acknowledged:** if a user renames their project directory, the custom name dissociates. This is correct behavior — the encoded dirname encodes the path; if the path changes, it's a new project. Document the tradeoff in `ISessionNameStore.cs` XMLdoc; do not paper over it.

#### Concrete contract sketch

```csharp
namespace CCInfoWindows.Services.Interfaces;

public interface ISessionNameStore
{
    /// <summary>Returns custom name for projectDirName, or null if none set.</summary>
    string? GetCustomName(string projectDirName);

    /// <summary>Sets or clears a custom name. Pass null/empty to remove.</summary>
    void SetCustomName(string projectDirName, string? customName);

    /// <summary>Returns all current custom-name overrides for the Sessions Settings tab.</summary>
    IReadOnlyDictionary<string, string> GetAll();

    /// <summary>Raised after any Set or bulk reset; subscribers should refresh derived UI.</summary>
    event EventHandler? NameChanged;
}
```

Implementation: JSON file `%LOCALAPPDATA%\CCInfoWindows\session-names.json`, registered as `AddSingleton<ISessionNameStore, SessionNameStore>()` in `App.ConfigureServices()`.

---

### Decision 2 — C-2 `IDispatcherQueue` Adapter

**Question:** Sketch the interface, identify receivers needing injection.

#### 2a. Interface (mirrors `IDispatcherTimer.cs` shape)

```csharp
// Services/Interfaces/IDispatcherQueue.cs
namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Abstraction over Microsoft.UI.Dispatching.DispatcherQueue for headless test
/// fakeability. Mirror of IDispatcherTimer (v1.4 precedent). Production code uses
/// WinuiDispatcherQueueAdapter; tests supply FakeDispatcherQueue.
/// </summary>
public interface IDispatcherQueue
{
    /// <summary>Returns true if executing on the queue's thread.</summary>
    bool HasThreadAccess { get; }

    /// <summary>
    /// Schedules a callback on the queue. Returns false if queue is shut down.
    /// Mirrors DispatcherQueue.TryEnqueue(DispatcherQueueHandler).
    /// </summary>
    bool TryEnqueue(Action callback);
}
```

Two action overloads aren't needed — every existing call site uses the no-priority overload (`MainViewModel.cs:803, 820, 982-986, 1032`). YAGNI — add `DispatcherQueuePriority` later if a use case appears.

#### 2b. Production adapter

```csharp
// Services/WinuiDispatcherQueueAdapter.cs
internal sealed class WinuiDispatcherQueueAdapter : IDispatcherQueue
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _inner;
    public WinuiDispatcherQueueAdapter(Microsoft.UI.Dispatching.DispatcherQueue inner) => _inner = inner;
    public bool HasThreadAccess => _inner.HasThreadAccess;
    public bool TryEnqueue(Action callback) => _inner.TryEnqueue(() => callback());
}
```

#### 2c. Test double — **inline-execute** semantics

```csharp
internal sealed class FakeDispatcherQueue : IDispatcherQueue
{
    public bool HasThreadAccess => true;       // tests run on a single thread
    public bool TryEnqueue(Action callback) { callback(); return true; }
}
```

Inline execution is correct for headless tests because:
- Tests are synchronous — they don't have a UI message pump to drain.
- The whole point of marshaling is to land on the UI thread; in tests, the test thread *is* the only thread, so inline is equivalent to "marshaled."
- A `QueueForExplicitPump` variant adds complexity with no current consumer — defer until a test demonstrably needs it.

#### 2d. Receivers requiring injection (audit of every UI-state mutation in `MainViewModel`)

| Site | File:Line | Off-thread sender? | Action |
|------|-----------|---------------------|--------|
| `Receive(AuthStateChangedMessage)` | `MainViewModel.cs:997-1026` | **YES** — `ClaudeApiService.FetchUsageAsync` (line 88) and `TryMigrateOrgIdAsync` (line 184) send from HTTP error path on ThreadPool | **Wrap entire body in `_dispatcher.TryEnqueue(...)`** (C-2 fix) |
| `Receive(SessionTimeoutChangedMessage)` | `MainViewModel.cs:1028-1033` | already wrapped | leave as is, swap `_dispatcherQueue` field type to `IDispatcherQueue` |
| `RefreshIntervalChangedMessage` lambda | `MainViewModel.cs:318-321` | sender is `SettingsViewModel` (UI thread), but defensive marshal is cheap | mutate via `IDispatcherQueue` for consistency |
| `SonnetContextChangedMessage` lambda | `MainViewModel.cs:324-332` | already wrapped | leave behavior, swap field type |
| `OnUpdateAvailable` | `MainViewModel.cs:977-987` | **YES** — `IUpdateService.UpdateAvailable` fires from `Task.Run` background poll | already wrapped — swap field type |
| `_dataUpdatedHandler` | `MainViewModel.cs:355-356` | **YES** — `JsonlService.RaiseDataUpdated()` fires from FileSystemWatcher debounce timer thread | already wrapped — swap field type |
| `AggregateStatisticsAsync` continuation | `MainViewModel.cs:803, 812, 820` | **YES** — `Task.Run` | already wrapped — swap field type |
| **NEW for v1.5:** `ISessionNameStore.NameChanged` handler | (to be added) | UI thread (Sessions Settings tab UI) — but symmetric with `DataUpdated` and defensive | wrap via `IDispatcherQueue` |

**DI registration:**
```csharp
services.AddSingleton<IDispatcherQueue>(sp =>
    new WinuiDispatcherQueueAdapter(
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("DispatcherQueue not available — call after MainWindow.Activate.")));
```

The `GetForCurrentThread()` resolution must happen on the UI thread. `App.OnLaunched` calls `Services = ConfigureServices()` at line 48 **before** `_window.Activate()` at line 61 — currently the dispatcher is captured lazily inside `MainViewModel.InitializeAsync` at line 310. Two options:

- **Option A (recommended):** Keep lazy capture. Register `IDispatcherQueue` as `AddSingleton` with a factory that throws if called before UI thread is ready, and resolve it inside `MainViewModel.InitializeAsync` (which runs from `MainView.Loaded`, guaranteed UI thread). Replaces the existing `_dispatcherQueue = DispatcherQueue.GetForCurrentThread()` line.
- **Option B:** Move `Services = ConfigureServices()` after `_window.Activate()`. Risky — `RouteOnStartupAsync` already depends on services being available.

Use Option A.

---

### Decision 3 — Project-Wide Thread-Marshaling Rule for `IRecipient<>`

**Proposed rule (canonical text for repo):**

> **Rule: every `IRecipient<T>.Receive(T)` body that mutates `[ObservableProperty]` fields, calls `INavigationService`, touches XAML controls, or invokes any API requiring `DispatcherQueue.HasThreadAccess` MUST wrap its body in `_dispatcher.TryEnqueue(() => { ... })`.**
>
> Rationale: `WeakReferenceMessenger.Send(...)` invokes recipients synchronously on the sender's thread. Senders include HTTP error handlers (`ClaudeApiService`), `Task.Run` continuations, and `FileSystemWatcher` debounce timers — all ThreadPool. Off-thread mutation of `ObservableProperty` fires `PropertyChanged` from the wrong thread; XAML bindings can throw `RPC_E_WRONG_THREAD` or silently corrupt state.
>
> Exception: if you can prove every sender of `T` is already on the UI thread (e.g. only fired by a `[RelayCommand]` invoked from XAML), you may skip. Document the proof in a comment.

#### Enforcement — minimum viable for v1.5

**Three-tier defense, in order of cost:**

1. **Code-review checklist line** (zero-cost, immediate). Add to `CLAUDE.md` under "MVVM Conventions":
   > Every `IRecipient<>.Receive` that mutates `[ObservableProperty]` must marshal via `IDispatcherQueue.TryEnqueue` first. See `architecture_weakreferencemessenger_with_transient_vms.md`.

2. **Convention in test surface** (low-cost). Add a `MessengerThreadingConventionTests` test class that uses reflection to assert: every `Receive` method in `CCInfoWindows.ViewModels` either (a) has its first statement be a `TryEnqueue` call, or (b) has a `[ThreadSafeReceive]` attribute marking it as proven UI-thread-only. Reflection-based, executes in <100ms, fails the build if a future contributor forgets.

3. **Roslyn analyzer** — DEFER to v1.6+. Writing a custom analyzer is a multi-day effort; the convention test catches 95% of violations at near-zero cost.

**Recommendation: ship Tier 1 + Tier 2 in v1.5.** Tier 2 turns the v1.4 hotfix lesson into a regression test — exactly the F.I.R.S.T. test discipline (CLAUDE.md "Clean Code Rules"). Tier 3 is over-investment until/unless the codebase grows substantially.

---

### Decision 4 — Settings Tab Insertion (A2 Sessions tab + B1/B2 settings)

#### 4a. A2 Sessions tab → **5th segment, inserted between Account and About**

**Rationale:**
- Adding a 5th segment is the **least disruptive** change. The Segmented Control at `SettingsView.xaml:35-82` is a flat list of `controls:SegmentedItem` — appending one is a 10-line XAML diff (one new segment + one new visibility-bound `<StackPanel>` panel + one new ResourceDictionary brush for the badge color).
- Position **between Account and About**: logical grouping — General/Updates are app-config, Account/Sessions are user-data, About is meta. Sessions is a sub-feature of "what does this app see?" (the Account-adjacent grouping).
- **Reject "subgroup under Account":** adds nesting hierarchy that doesn't exist anywhere else in the app; UX inconsistency.
- **Reject "replace existing":** all existing tabs serve distinct functions; no candidate for replacement.
- **Width concern:** 5 × 30px badges + spacing fits within 360px width. Verified by inspection of the existing 4-tab layout (each badge is `Width="30" Height="30"`, `HorizontalAlignment="Stretch"` distributes them evenly).

**Badge color:** purple/violet (e.g. `SettingsBadgePurpleBrush`) to round out the rainbow (green/blue/red/orange already in use). Add to `AppTheme.xaml` as part of the same phase.

#### 4b. B1 visibility-window dropdown → **General tab**

**Rationale:**
- The General tab already hosts all "what does the app show me?" toggles: refresh interval, session timeout, dark mode, language, Sonnet context, reset window size (`SettingsView.xaml:88-237`). Visibility window is the same conceptual category — "configure what data is displayed."
- A2 Sessions tab is for **per-session overrides** (custom names, possibly future per-session settings). The visibility window is a **global filter** — wrong tab.
- Adds one more `<Grid Height="40">` row to the existing General card; pure additive change.

#### 4c. B2 Org-ID override → **Account tab**

**Rationale:**
- Account tab currently has only Token Status + Logout (`SettingsView.xaml:308-356`) — under-utilized; org-id is unambiguously account-scoped data.
- Org-id is paired with the auth token in Credential Manager (`claude-org` key per memory `cloudflare-fix.md`); putting its UI override anywhere else would split the mental model.
- The "Force Re-resolve" button and the picker dropdown both belong with "Logout" semantically (account-mutation operations).

**Summary table:**

| v1.5 setting | Tab | Position | Rationale |
|--------------|-----|----------|-----------|
| A2 Custom session names list | NEW Sessions (5th tab) | Between Account and About | Per-session data, distinct from global |
| B1 Visibility window | General | After Session Timeout row | Global display filter |
| B2 Org-ID picker + Force re-resolve | Account | After Token Status, above Logout | Account-scoped data |
| B3 Pricing-error banner | (not a settings UI — main view banner) | n/a | See Integration Points |

---

### Decision 5 — Build Order Recommendation

**Dependencies (derived from above):**

- C-2 (`IDispatcherQueue`) is a **foundation primitive** — every new `IRecipient<>` handler in v1.5 (B1's `SessionVisibilityChangedMessage`, A2's `ISessionNameStore.NameChanged`) should follow the new convention from day one. Landing C-2 last would force rework.
- B1 (Cwd hydration robustness) **must land before A2** — A2's display layer reads `SessionInfo` collections that B1 makes reliable. Building A2 on top of fragile cold-start hydration would surface as "custom name not appearing on cold start" bugs that look like A2 regressions but are actually B1 issues.
- A1 is a pure ObservableProperty addition (PROJECT.md cluster A description) — zero architectural dependencies, can land anytime. Bundle with low-risk cleanup phase.
- B2 (Org-ID picker) depends on `ClaudeApiService` extensions — independent of A2/B1.
- B3 (Pricing error surfacing) couples with M-2 (LastFetchRelativeTime localization) per PROJECT.md "Key context"; bundle them.
- C-1 (fire-and-forget try/catch) is in the **same `Receive(AuthStateChangedMessage)` body** as C-2 — should be fixed in the **same commit** as C-2 (single edit, single test).
- M-1 (delete `LogoutRequestedMessage.cs`) is risk-free dead-code deletion — bundle with M-3 + Nits in a final cleanup phase.
- M-2 (LastFetchRelativeTime localization) couples with B3 (same property, same XAML row).

**Recommended phase sequence:**

| Phase | Scope | Why this position |
|-------|-------|-------------------|
| **24** | **C-1 + C-2 + IDispatcherQueue + Tier 1/2 enforcement** | Foundation. Fix the v1.4 critical bugs first; land the abstraction; document the rule; add the convention test. New phases 25-28 then *use* `IDispatcherQueue` from day one. |
| **25** | **B1 — JsonlService Cwd hydration + visibility-window setting + `SessionVisibilityChangedMessage`** | Stabilizes session list. A2 and the rest of v1.5 read this list. New `IRecipient<SessionVisibilityChangedMessage>` follows the rule from Phase 24. |
| **26** | **A2 — Session renaming (`ISessionNameStore` + Sessions Settings tab + MainView pencil button)** | Builds on B1's stable session list. Uses `IDispatcherQueue` for `NameChanged` event marshaling. |
| **27** | **A1 + B2 + B3 + M-2** | Mid-risk feature trio. A1 is trivial (pure ObservableProperty); B2 is independent (ClaudeApiService extension + Account tab UI); B3+M-2 share the LastFetchRelativeTime surface. |
| **28** | **M-1 + M-3 + Nits + final UAT pass** | Pure cleanup. Deletes `LogoutRequestedMessage.cs`, restores `_contextModelBadgeColor` default, applies opportunistic cleanups. Lowest risk; ships last to keep test surface stable. |

**Rebase-pain rationale:**
- Phase 24 modifies `MainViewModel.cs` (every `_dispatcherQueue` reference becomes `_dispatcher`) AND `App.xaml.cs` (DI). Doing this **first** means subsequent phases edit only their own files plus small, predictable additions to `MainViewModel.cs` — no cross-phase merges in the central VM.
- B1 before A2: A2's "rename a session" UI iterates `SessionInfo` — if B1 changes which sessions appear (visibility window) mid-A2, you rebase A2's tests.
- Phase 27's three items don't touch each other's files (A1 = MainView XAML row; B2 = ClaudeApiService + SettingsView Account panel; B3+M-2 = SettingsView Updates panel + MainView banner) → **safe to parallelize across multiple PRs within the phase** if desired.

---

## Integration Points

### Files modified by phase

| Phase | Modified | Created |
|-------|----------|---------|
| 24 | `App.xaml.cs` (DI), `MainViewModel.cs` (`Receive` body wrap, field type swap), tests | `Services/Interfaces/IDispatcherQueue.cs`, `Services/WinuiDispatcherQueueAdapter.cs`, `Tests/FakeDispatcherQueue.cs`, `Tests/MessengerThreadingConventionTests.cs`, `CLAUDE.md` (rule line) |
| 25 | `Services/JsonlService.cs` (Cwd hydration on cold start), `Models/AppSettings.cs` (visibility window field), `SettingsViewModel.cs`, `Views/SettingsView.xaml` (General tab row), `MainViewModel.cs` (new `IRecipient`) | `Messages/SessionVisibilityChangedMessage.cs` |
| 26 | `App.xaml.cs` (DI for ISessionNameStore), `MainViewModel.cs` (`RefreshSessionList` override hook + event subscription), `Views/MainView.xaml` (pencil button), `Views/SettingsView.xaml` (5th segment + Sessions panel), `AppTheme.xaml` (purple badge brush), resw files (DE/EN keys) | `Services/Interfaces/ISessionNameStore.cs`, `Services/SessionNameStore.cs`, `ViewModels/SessionsTabViewModel.cs` (or extend SettingsViewModel) |
| 27 | `MainViewModel.cs` (A1 ObservableProperty, B3 `HasPricingError`), `Services/ClaudeApiService.cs` (B2 `ListOrganizations`, `ForceReResolveOrgAsync`), `SettingsViewModel.cs` (B2 picker, M-2 localized strings), `Views/MainView.xaml` (A1 next-window label, B3 banner), `Views/SettingsView.xaml` (Account tab Org-ID), resw files | (none) |
| 28 | `MainViewModel.cs` (`_contextModelBadgeColor` default), various nit cleanups | (none) |
| 28 | (delete) | `Messages/LogoutRequestedMessage.cs` (removed) |

### DI surface delta (`App.ConfigureServices()`)

```csharp
// New in v1.5
services.AddSingleton<IDispatcherQueue>(sp => new WinuiDispatcherQueueAdapter(
    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
    ?? throw new InvalidOperationException("...")));   // Phase 24
services.AddSingleton<ISessionNameStore, SessionNameStore>();             // Phase 26

// MainViewModel constructor gains IDispatcherQueue and ISessionNameStore parameters
```

### Message surface delta

- **Add:** `Messages/SessionVisibilityChangedMessage.cs` (Phase 25, mirror of `SessionTimeoutChangedMessage`)
- **Delete:** `Messages/LogoutRequestedMessage.cs` (Phase 28, M-1)
- **Do NOT add:** `SessionRenamedMessage` — A2 uses direct event subscription per Decision 1b.

### B3 pricing-error UI surface

Reuse existing `HasApiError` pattern (`MainViewModel.cs:171-174`). Add parallel:
```csharp
[ObservableProperty] private bool _hasPricingError;
[ObservableProperty] private string _pricingErrorMessage = string.Empty;
```
Set inside the existing `_ = Task.Run(...)` block at `MainViewModel.cs:371-375` and the `AggregateStatisticsAsync` catch at line 809-813. Banner placement: above the existing 5-hour-window InfoBar, dismissible, persisted-dismissal via `AppSettings.DismissedPricingErrorVersion`.

---

## Conventions to Standardize

These graduate from "informal practice" to "documented convention" in v1.5 (added to `CLAUDE.md`):

1. **Thread-marshaling rule for `IRecipient<>`** — every `Receive` mutating UI state wraps in `IDispatcherQueue.TryEnqueue`. Enforced by `MessengerThreadingConventionTests`. (Decision 3.)

2. **Cross-VM communication priority** — direct DI > singleton service event > `WeakReferenceMessenger`. Use `WeakReferenceMessenger` only for true broadcast (multiple unknown receivers, eventual-consistency-OK). For exactly-once delivery to `AddTransient` ViewModels, use direct DI or an event on a `Singleton` service. (Codifies the v1.4 hotfix lesson.)

3. **External-source storage keys** — when persisting overrides keyed against external data, key on the **filesystem-stable identifier** (e.g. encoded `projectDirName`), not on derived/parsed data (e.g. `Cwd`). (Decision 1c, generalized.)

4. **Adapter precedent for WinRT singletons** — any WinRT-singleton API used inside a ViewModel should have an `I…` adapter for headless test fakeability. Existing: `IDispatcherTimer` (v1.4). New: `IDispatcherQueue` (v1.5). Future candidates: `IClipboard`, `IAppNotificationManager` — defer until tested need arises.

5. **Optional-DI dependencies in services** — services like `JsonlService` that take optional DI parameters (`ISettingsService? settingsService = null`) must not accumulate further optional parameters. New cross-cutting concerns (e.g. `ISessionNameStore`) hook in at the **consumer** layer (ViewModel), not the service layer. (Decision 1a, generalized.)

---

## Confidence Assessment

| Area | Confidence | Evidence |
|------|------------|----------|
| Existing architecture inventory | HIGH | All files read in-tree; no inferred behavior |
| Decision 1 (A2 hook point + key choice) | HIGH | Cites JsonlService.cs:577-578, RebuildSessionsList:779-801, SessionInfo.Id provenance |
| Decision 1b (no SessionRenamedMessage) | HIGH | Direct application of v1.4 hotfix lesson; same lifetime constraint |
| Decision 2 (IDispatcherQueue shape) | HIGH | Mirror of shipped IDispatcherTimer; all use sites enumerated from MainViewModel.cs |
| Decision 3 (enforcement tier) | MEDIUM | Tier 2 reflection-test feasibility relies on standard reflection — but specific "first statement is TryEnqueue" detection requires Roslyn parse, not pure reflection. **Caveat:** if reflection-only proves insufficient, fall back to `[ThreadSafeReceive]`-attribute-or-`[RequiresMarshal]`-attribute pattern with a simpler "every Receive has one or the other" check. |
| Decision 4 (5th tab + General/Account placement) | HIGH | Confirmed Segmented Control flat structure at SettingsView.xaml:35-82; existing tab semantics support the placement |
| Decision 5 (build order) | HIGH | Dependencies derived from concrete file overlap analysis |

### Open question flagged for Roadmapper

- **Phase 24 sizing.** C-1 + C-2 + `IDispatcherQueue` + convention test could be one phase or split into "24a fix + 24b abstraction + 24c convention." Recommendation: **one phase, 3 plans inside it** (per the v1.4 phase-with-multiple-plans pattern). Roadmapper should confirm against milestone size budget.
- **Decision 3 Tier 2 implementation detail.** "Reflection-only verification of marshaling" is technically MEDIUM confidence — the cheapest reliable enforcement is an attribute pair (`[ThreadSafeReceive]` vs `[RequiresMarshal]`) with reflection asserting every `Receive` has one. Worth a 30-minute spike in Phase 24 before committing to the exact mechanism.

---

*All file paths verified against working tree at HEAD = `d77da3a` (2026-05-07). Memory references: `architecture_weakreferencemessenger_with_transient_vms.md` (both pitfalls), `cloudflare-fix.md` (credential keys).*

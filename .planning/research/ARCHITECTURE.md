# Architecture Patterns

**Domain:** WinUI 3 desktop app — macOS v1.8.3 feature parity integration
**Researched:** 2026-04-12

---

## v1.2 Integration Analysis

All five v1.2 features integrate cleanly into the existing MVVM layer structure. No new services, no new View pages, and no changes to the DI registration are required. The dependency chain is linear from the bottom up:

```
Phase 1: ModelContextLimits (Helper) — core logic rewrite
    ↓ signature change propagates to ContextWindowData callers
Phase 2: AppSettings + SettingsViewModel + SonnetContextChangedMessage + JsonlService
    (no dependency on phases below)
Phase 3: JsonlService.RebuildSessionsList() — one-line filter
Phase 4: JsonlService.BuildSubagentContext() — one-line sort
Phase 5: MainView.xaml + localization files — XAML only
```

---

## Component Map: Modified vs New

### Modified Components

| Component | File | What Changes |
|-----------|------|--------------|
| `ModelContextLimits` | `Helpers/ModelContextLimits.cs` | Remove 3 obsolete constants, add `ExtendedContextLimit` and `AutocompactWarningBuffer`, add `ModelFamily` enum, add `GetModelFamily()`, rewrite `GetMaxContextTokens()` to accept `sonnetContextSize` parameter, simplify `GetEffectiveMaxTokens()` to single-parameter flat buffer, rewrite `ShouldWarnAutocompact()` to flat 20K buffer |
| `ContextWindowData` | `Models/ContextWindowData.cs` | `Utilization` property calls `GetEffectiveMaxTokens(TotalTokens, MaxTokens)` — must update call site when signature drops `currentTokens` parameter |
| `SubagentContextData` | `Models/ContextWindowData.cs` | Same `Utilization` call site propagation as above |
| `AppSettings` | `Models/AppSettings.cs` | Add `SonnetContextSize` int property with `[JsonPropertyName("sonnetContextSize")]` and default value `200_000` |
| `JsonlService` | `Services/JsonlService.cs` | (1) Constructor: add `ISettingsService` parameter; (2) `GetContextWindow()`: pass `settings.SonnetContextSize` to `GetMaxContextTokens()`; (3) `BuildSubagentContext()`: add `.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList()` before return; (4) `RebuildSessionsList()`: extend the `.Where()` filter to include `Directory.Exists(s.Cwd)` |
| `SettingsViewModel` | `ViewModels/SettingsViewModel.cs` | Add `_selectedSonnetContextIndex` `[ObservableProperty]`, initialize in `Initialize()`, persist and send `SonnetContextChangedMessage` in `OnSelectedSonnetContextIndexChanged()` |
| `MainViewModel` | `ViewModels/MainViewModel.cs` | Register handler for `SonnetContextChangedMessage` → call the existing local data refresh path (`UpdateSessionData` or equivalent) |
| `MainView.xaml` | `Views/MainView.xaml` | Add `ToolTipService.ToolTip` (localized via l:Uids.Uid) and `AutomationProperties.Name` (static English) to the three footer buttons |
| `SettingsView.xaml` | `Views/SettingsView.xaml` | Add Sonnet Context `ComboBox` section (label + two-item picker) after the Language selector |
| `Resources.resw` (both locales) | `Strings/de-DE/` and `Strings/en-US/` | Phase 2: Sonnet context label keys; Phase 5: three footer tooltip keys |

### New Components

| Component | Type | Purpose |
|-----------|------|---------|
| `ModelFamily` | Enum (nested in `ModelContextLimits` or standalone in `Helpers/`) | Represents `Opus`, `Sonnet`, `Haiku`, `Unknown` — consumed by `GetModelFamily()` and `GetMaxContextTokens()` |
| `SonnetContextChangedMessage` | `Messages/SonnetContextChangedMessage.cs` | Typed messenger notification that Sonnet context size changed; follows the `ValueChangedMessage<int>` pattern of the existing `RefreshIntervalChangedMessage` |

No new services. No new Views. No DI registration changes.

---

## Detailed Data Flow per Feature

### Phase 1: Model-Based Context Detection

```
JsonlService.GetContextWindow(sessionId)
  → ResolveModelName() → string modelName
  → ISettingsService.LoadSettings().SonnetContextSize → int sonnetContextSize
  → ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize) → long maxTokens
  → ModelContextLimits.ShouldWarnAutocompact(totalTokens, maxTokens)
       uses: totalTokens >= maxTokens - AutocompactWarningBuffer (20K flat)
  → ContextWindowData { MaxTokens = maxTokens, ... }

ContextWindowData.Utilization (computed property)
  → ModelContextLimits.GetEffectiveMaxTokens(maxTokens)
       returns: Math.Max(1, maxTokens - StandardAutocompactBuffer) // 33K flat
  → Math.Clamp(TotalTokens / effectiveMax, 0.0, 1.0)
```

The `GetEffectiveMaxTokens()` signature drops the `currentTokens` parameter (it was only used for the heuristic that is being removed). Both `ContextWindowData.Utilization` and `SubagentContextData.Utilization` contain call sites — both must be updated in the same commit as the helper change to avoid a build break.

### Phase 2: Sonnet Context Setting

```
SettingsView.xaml ComboBox (index 0 = 200K, 1 = 1M)
  → SettingsViewModel._selectedSonnetContextIndex [ObservableProperty]
  → OnSelectedSonnetContextIndexChanged()
       → settings.SonnetContextSize = index == 0 ? 200_000 : 1_000_000
       → ISettingsService.SaveSettings(settings)
       → WeakReferenceMessenger.Default.Send(new SonnetContextChangedMessage(settings.SonnetContextSize))

MainViewModel receives SonnetContextChangedMessage
  → calls the local data refresh path (re-reads context window data for current session)

JsonlService.GetContextWindow(sessionId)
  → reads AppSettings.SonnetContextSize via cached ISettingsService.LoadSettings()
  → passes value to ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize)
```

**ISettingsService injection into JsonlService:** The cleanest approach is constructor injection — `JsonlService` already accepts `IPricingService` via constructor for cost calculations. Adding `ISettingsService` follows the same pattern. Settings are read fresh on each `GetContextWindow()` call by reading from the service (which already caches the deserialized `AppSettings` in memory). No stale-value risk. The `IJsonlService` public interface does not need to change.

An alternative is caching the sonnet context size as a field in `JsonlService` and updating it when `SonnetContextChangedMessage` is received (by registering on the messenger in the constructor). This avoids one service dependency but adds messenger coupling in the service layer, which is less clean. Prefer the ISettingsService injection approach.

### Phase 3: Session Filtering

```
JsonlService.RebuildSessionsList()
  current:  .Where(s => s is not null)
  change:   .Where(s => s is not null
                     && !string.IsNullOrEmpty(s.Cwd)
                     && Directory.Exists(s.Cwd))
```

`Sessions` is already an `IReadOnlyList<SessionInfo>` read by `MainViewModel` on the `DataUpdated` event. The filtered list propagates through the existing event-driven refresh path without any ViewModel or UI changes.

`s.Cwd` originates from JSONL file content (external data). `Directory.Exists()` is safe on NTFS paths and is already used elsewhere in WinUI 3 apps for this pattern. The existing path-validation guard in `DiscoverSessions()` already filters paths during initial indexing, so `Cwd` values stored in `_projectData` have already passed basic path sanitization.

### Phase 4: Subagent Sorting

```
JsonlService.BuildSubagentContext(subagentFiles)
  current:  return result;
  change:   return result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList();
```

One line before the return. The returned `IReadOnlyList<SubagentContextData>` is consumed by `MainViewModel` without any re-sorting. The `SubagentContextData.AgentId` values are already strings (e.g. `"abc123"` extracted from `agent-abc123.jsonl`), so ordinal sort produces a stable alphabetical order.

### Phase 5: Footer Accessibility

```
MainView.xaml — three existing footer Button elements:
  Add: ToolTipService.ToolTip="{l:Uids.Uid FooterRefreshTooltip}"
  Add: AutomationProperties.Name="Refresh"   (static, not localized)

Repeat for Settings ("Einstellungen" / "Settings") and Quit ("Beenden" / "Quit").

Resources.resw (both locales) — add:
  FooterRefreshTooltip.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip
  FooterSettingsTooltip.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip
  FooterQuitTooltip.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip
```

No ViewModel changes. No messenger messages. Pure XAML and resource file edits.

**WinUI3Localizer ToolTipService.ToolTip syntax — verify before writing XAML.** The existing localization pattern for text properties uses the short form (`Label.Text = "value"`). For attached properties like `ToolTipService.ToolTip`, WinUI3Localizer requires the full namespace-qualified property path in the `.resw` key. Confirm the exact key format expected by the installed WinUI3Localizer version against the library's own documentation or test suite before committing the XAML. An incorrect Uid path silently falls back to an empty tooltip.

---

## Component Boundary Diagram

```
┌─────────────────────────────────────────────────────┐
│ Views                                                │
│  MainView.xaml        Phase 5: add tooltip/a11y     │
│  SettingsView.xaml    Phase 2: add Sonnet ComboBox  │
└────────────────┬────────────────────────────────────┘
                 │ x:Bind (compiled bindings)
┌────────────────▼────────────────────────────────────┐
│ ViewModels                                           │
│  MainViewModel        Phase 2: register msg handler │
│  SettingsViewModel    Phase 2: add ObservableProperty│
└──────────┬──────────────────────┬───────────────────┘
           │ service calls        │ messenger
┌──────────▼───────┐   ┌──────────▼─────────────────┐
│ Messages         │   │ Services                    │
│ SonnetContext-   │   │  JsonlService               │
│ ChangedMessage   │   │    Phase 2: ISettingsService│
│ (NEW)            │   │    Phase 3: Cwd filter      │
└──────────────────┘   │    Phase 4: agentId sort    │
                       └──────────┬──────────────────┘
                                  │ calls
┌─────────────────────────────────▼───────────────────┐
│ Models                                               │
│  AppSettings          Phase 2: SonnetContextSize    │
│  ContextWindowData    Phase 1: Utilization call site │
│  SubagentContextData  Phase 1: Utilization call site │
└─────────────────────────────────┬───────────────────┘
                                  │ calls
┌─────────────────────────────────▼───────────────────┐
│ Helpers                                              │
│  ModelContextLimits   Phase 1: core rewrite          │
│  ModelFamily enum     Phase 1: NEW                   │
└─────────────────────────────────────────────────────┘
```

---

## Suggested Build Order

Dependency-driven. Each phase is an atomic commit. Phases 3, 4, 5 are independent of Phase 2 and can be done in any order after Phase 1.

### Step 1 — ModelContextLimits rewrite (Phase 1)

Files: `ModelContextLimits.cs`, `ContextWindowData.cs`

Must be first. Removes `ExtendedAutocompactBuffer`, `ExtendedContextDetectionThreshold`, and the token-count heuristic from `GetEffectiveMaxTokens()`. Adds `ModelFamily` enum, `GetModelFamily()`, updated `GetMaxContextTokens(modelName, sonnetContextSize)`, simplified `GetEffectiveMaxTokens(maxTokens)`, and flat-buffer `ShouldWarnAutocompact()`. Both `ContextWindowData.Utilization` and `SubagentContextData.Utilization` call `GetEffectiveMaxTokens()` — update both call sites in this same commit to keep the build green.

At this point `GetMaxContextTokens()` accepts a `sonnetContextSize` parameter. Temporary call sites in `JsonlService` can pass the hardcoded constant `200_000` until Phase 2 wires up the live setting.

### Step 2 — Sonnet Context Setting end-to-end (Phase 2)

Files: `AppSettings.cs`, `SonnetContextChangedMessage.cs` (new), `JsonlService.cs` (ISettingsService constructor injection + GetMaxContextTokens call update), `SettingsViewModel.cs`, `MainViewModel.cs`, `SettingsView.xaml`, `Strings/de-DE/Resources.resw`, `Strings/en-US/Resources.resw`

Requires Step 1 because it passes a live `sonnetContextSize` to the updated `GetMaxContextTokens()` signature. After this step, Opus, Sonnet, and Haiku all display the correct context limits, and the Settings view exposes the picker.

### Step 3 — Session Filtering (Phase 3)

Files: `JsonlService.cs` (one filter expression in `RebuildSessionsList()`)

Independent of Step 2. Can be done any time after Step 1. Low-risk single-method change.

### Step 4 — Subagent Sorting (Phase 4)

Files: `JsonlService.cs` (one `.OrderBy()` call in `BuildSubagentContext()`)

Independent of everything. Can be batched with Step 3 into a single "JsonlService polish" commit if desired, or kept separate for cleaner git history.

### Step 5 — Footer Accessibility (Phase 5)

Files: `MainView.xaml`, `Strings/de-DE/Resources.resw`, `Strings/en-US/Resources.resw`

Purely additive. No logic changes. Zero regression risk. Do last.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Leaving the `currentTokens` parameter on `GetEffectiveMaxTokens`

**What goes wrong:** Keeping `GetEffectiveMaxTokens(long currentTokens, long maxTokens)` "for backwards compatibility." The `isExtended = currentTokens > 180_000` heuristic path survives and silently overrides model-based detection for Opus sessions with low current token counts (e.g. an Opus session at 50K tokens would use the standard 200K limit instead of 1M).

**Prevention:** Remove all three obsolete constants (`ExtendedAutocompactBuffer`, `ExtendedContextDetectionThreshold`, `LargeModelAutocompactThreshold`, `SmallModelAutocompactThreshold`, `LargeModelThresholdTokens`) and both parameters in one atomic commit. Update both `Utilization` call sites in the same commit.

### Anti-Pattern 2: Passing `sonnetContextSize` through the IJsonlService public interface

**What goes wrong:** Adding `sonnetContextSize` to `GetContextWindow(string sessionId, int sonnetContextSize)`. Every caller — `MainViewModel` has at least two call sites — must retrieve and pass the value. The `IJsonlService` interface changes, breaking any existing mock implementations used in tests.

**Prevention:** Inject `ISettingsService` into `JsonlService` constructor. The setting is an implementation detail of the service layer.

### Anti-Pattern 3: Performing file I/O under the sessions lock on every context window read

**What goes wrong:** Calling `ISettingsService.LoadSettings()` (which reads `settings.json`) inside `GetContextWindow()` while `_sessionsLock` is held. `GetContextWindow()` is called on every UI refresh cycle. File I/O under lock blocks other threads from accessing session data.

**Prevention:** `ISettingsService.LoadSettings()` already deserializes from an in-memory cache in the current implementation (verify, but this is the standard pattern). If it does cache, no action needed. If it does disk I/O, read the setting before acquiring the lock and pass it in as a local variable.

### Anti-Pattern 4: Localizing `AutomationProperties.Name` for footer buttons

**What goes wrong:** Attempting to drive `AutomationProperties.Name` through WinUI3Localizer using the same Uid pattern as tooltips. Screen readers use the OS locale, not the app's runtime locale. The localization switch would not affect what the screen reader announces.

**Prevention:** Use static English strings (`"Refresh"`, `"Settings"`, `"Quit"`) for `AutomationProperties.Name`. Localize only the visible `ToolTipService.ToolTip`.

### Anti-Pattern 5: Filtering sessions inside `GetContextWindow()` instead of `RebuildSessionsList()`

**What goes wrong:** Adding the `Directory.Exists(s.Cwd)` check inside `GetContextWindow()` or `UpdateSessionData()`. The session would still appear in the dropdown but return `ContextWindowData.Empty` when selected, which would look like a data error to the user.

**Prevention:** Filter in `RebuildSessionsList()` so deleted sessions never enter the `_sessions` list at all. The dropdown stays clean.

---

## Scalability Considerations

These are all read-path or additive changes for a single-user desktop app. No scalability concerns.

| Concern | Before v1.2 | After v1.2 |
|---------|-------------|------------|
| Session list rebuild | O(n) `OrderByDescending` | O(n) `OrderByDescending` + O(n) `Directory.Exists` per session |
| Subagent list build | O(n) unordered | O(n log n) `OrderBy agentId` |
| Context window reads | O(1) in-memory, `GetMaxContextTokens` dict lookup | O(1) same + O(1) settings cache read |

`Directory.Exists` on local NTFS paths is sub-millisecond per call. With 5–30 sessions, `RebuildSessionsList()` overhead is negligible. UNC/network drive paths are a known edge case — acceptable since the method is only called on `FileSystemWatcher` events and initial scan, not on every UI tick.

---

## Retained: Existing Architecture (v1.0–v1.1)

The sections below document the stable architectural foundation that v1.2 builds on.

### Component Responsibilities

| Component | Responsibility | Communicates With |
|-----------|----------------|-------------------|
| **MainView + MainViewModel** | Primary dashboard: 5h chart, weekly usage, context window, session picker, token stats, cost display | ClaudeApiService, JsonlService, PricingService, NavigationService |
| **LoginView + LoginViewModel** | WebView2 login flow, cookie extraction from claude.ai | CredentialService, NavigationService |
| **SettingsView + SettingsViewModel** | App preferences (refresh interval, theme, language, autostart, sonnet context) | SettingsService, NavigationService |
| **ClaudeApiService** | HTTP polling for 5h/weekly usage data from claude.ai via WebView2 bridge | CredentialService |
| **JsonlService** | Parses JSONL log files for token/session/cost data, FileSystemWatcher | PricingService, ISettingsService |
| **PricingService** | Fetches and caches LiteLLM pricing, tiered pricing calculation | Local cache file, GitHub raw content |
| **CredentialService** | Win32 CredRead/CredWrite for session token storage | Windows Credential Manager via AdysTech.CredentialManager |
| **SettingsService** | Read/write settings.json | Local JSON files in %LOCALAPPDATA% |
| **UpdateService** | Periodic GitHub Releases API check, version comparison | GitHub API, MainViewModel (banner notification) |
| **NavigationService** | Frame-based page navigation within single window | All Views |

### Key Architectural Patterns

**DI-Based MVVM:** All services registered as singletons in `Microsoft.Extensions.DependencyInjection`. ViewModels receive services via constructor injection. Source generators (`[ObservableProperty]`, `[RelayCommand]`) eliminate boilerplate.

**WeakReferenceMessenger:** Cross-ViewModel communication via typed messages. `SonnetContextChangedMessage` follows the exact same pattern as the existing `RefreshIntervalChangedMessage`.

**Timer-Driven Polling + FileSystemWatcher:** Claude API data polled at configurable interval (30s–10min). JSONL data reactive via debounced FileSystemWatcher (2s debounce). Both paths marshal to UI thread via `DispatcherQueue.TryEnqueue()`.

**DispatcherQueue.TryEnqueue():** Mandatory for all observable property updates originating from background threads. WinUI 3 does not auto-marshal (unlike WPF).

---

## Sources

- `spec-release-from-1.7.1-to-1.8.3.md` — Authoritative implementation spec (HIGH confidence, authored for this milestone)
- `CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs` — Current implementation, direct code read
- `CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs` — Utilization computation, direct code read
- `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — `RebuildSessionsList()`, `BuildSubagentContext()`, `GetContextWindow()`, direct code read
- `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` — Current settings schema, direct code read
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` — Settings property and messenger patterns, direct code read
- `CCInfoWindows/CCInfoWindows/Messages/RefreshIntervalChangedMessage.cs` — Messenger message pattern reference, direct code read
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs` — Public service contract, direct code read

# Stack Research

**Domain:** WinUI 3 Desktop App — v1.2 macOS v1.8.3 Feature Parity (Milestone-specific)
**Researched:** 2026-04-12
**Confidence:** HIGH

## Executive Answer

**Zero new NuGet packages required.** All five phases use exclusively existing stack capabilities. This milestone is entirely pure C# logic, XAML binding, and resw string additions. The `.csproj` does not change.

---

## Existing Stack — No Changes

| Technology | Current Version | Relevant Capability for This Milestone |
|------------|----------------|----------------------------------------|
| C# 13 / .NET 9 | net9.0-windows10.0.19041.0 | Enum types, LINQ `OrderBy`, `Directory.Exists`, string parsing |
| Windows App SDK | 1.8.260209005 | `ToolTipService.ToolTip`, `AutomationProperties.Name` (WinUI 3 attached properties) |
| CommunityToolkit.Mvvm | 8.4.0 | `[ObservableProperty]`, `ValueChangedMessage<T>`, `WeakReferenceMessenger` |
| WinUI3Localizer | 2.3.0 | Property path syntax for attached properties in resw keys |
| System.Text.Json | (bundled .NET 9) | Auto-default for new `SonnetContextSize` property — no migration needed |
| System.IO | (BCL) | `Directory.Exists()` for session filtering |
| System.Linq | (BCL) | `OrderBy(a => a.AgentId, StringComparer.Ordinal)` for subagent sorting |

---

## Phase-by-Phase Stack Analysis

### Phase 1: 1M Context Window Support

**Scope:** `Helpers/ModelContextLimits.cs`, `Models/ContextWindowData.cs`

Pure C# refactor. No new APIs used.

| Change | API / Pattern | Already in Codebase? |
|--------|--------------|---------------------|
| `ModelFamily` enum (Opus/Sonnet/Haiku/Unknown) | C# enum definition | Yes — pattern exists (e.g., `PricingSource` enum) |
| `GetModelFamily(string? modelName)` | `string.Contains()` case-insensitive | Yes — identical to `GetBadgeColorHex()` |
| `GetMaxContextTokens(string? modelName, long sonnetContextSize)` | Method overload | Yes — existing method gets new overload |
| `GetEffectiveMaxTokens(long maxTokens)` | Simplified: remove `currentTokens` param | Yes — BCL math only |
| `ShouldWarnAutocompact()` flat threshold | `totalTokens >= maxTokens - 20_000` | Yes — replaces percentage math |
| `ContextWindowData.Utilization` / `SubagentContextData.Utilization` call site update | Propagated from `GetEffectiveMaxTokens` signature change | Yes — two call sites, compile-time enforced |

**Gotcha:** `GetEffectiveMaxTokens(currentTokens, maxTokens)` → `GetEffectiveMaxTokens(maxTokens)` is a **breaking signature change**. Both call sites are in `ContextWindowData.cs` and will not compile until updated — catch is guaranteed by the build.

### Phase 2: Sonnet Context Window Setting

**Scope:** `Models/AppSettings.cs`, `ViewModels/SettingsViewModel.cs`, `Services/JsonlService.cs`, `Views/SettingsView.xaml`, both `.resw` files, new `Messages/SonnetContextChangedMessage.cs`

| Change | API / Pattern | Already in Codebase? |
|--------|--------------|---------------------|
| `int SonnetContextSize { get; set; } = 200_000` on `AppSettings` | `[JsonPropertyName]` + default value | Yes — identical to `RefreshIntervalSeconds` |
| Settings migration on load | None needed — `System.Text.Json` returns default for missing keys | Yes — proven by all existing settings properties |
| `SonnetContextChangedMessage : ValueChangedMessage<int>` | `CommunityToolkit.Mvvm.Messaging.Messages` | Yes — same shape as `RefreshIntervalChangedMessage` |
| `[ObservableProperty] private int _selectedSonnetContextIndex` | CommunityToolkit.Mvvm source generator | Yes — identical to `_selectedLanguageIndex` pattern |
| `partial void OnSelectedSonnetContextIndexChanged(int value)` | CommunityToolkit.Mvvm partial method callback | Yes — identical to `OnSelectedLanguageIndexChanged` |
| ComboBox in SettingsView XAML | WinUI 3 `ComboBox` with `ComboBoxItem` children | Yes — identical to `SessionTimeoutComboBox` pattern |
| New resw keys for Sonnet context labels | WinUI3Localizer `l:Uids.Uid` | Yes — standard resw addition |
| `JsonlService.GetContextWindow()` reads `SonnetContextSize` setting | `ISettingsService.LoadSettings()` already injected | Verify: check if `JsonlService` has `ISettingsService` injection |

**Dependency:** Phase 1 must complete first — `GetMaxContextTokens(modelName, sonnetContextSize)` overload is required.

**Data flow:** User changes picker → `SettingsViewModel` saves to `AppSettings` → sends `SonnetContextChangedMessage` → `MainViewModel` receives → calls `RefreshLocalData()` → `JsonlService.GetContextWindow()` reads updated setting → `ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize)` returns correct limit.

### Phase 3: Session Filtering

**Scope:** `Services/JsonlService.cs` — `RebuildSessionsList()` only

| Change | API | Already in Codebase? |
|--------|-----|---------------------|
| `Directory.Exists(s.Cwd)` filter | `System.IO.Directory.Exists()` | Yes — BCL |
| Null/empty `Cwd` guard | `!string.IsNullOrEmpty(s.Cwd)` | Yes — same null-guard pattern used throughout |

**Security note from spec cross-cutting concerns:** `Directory.Exists()` on the `Cwd` field is appropriate — `Cwd` is the Claude Code session working directory (any path on disk), not a user-supplied path for file access. Existing `IsPathWithinProjectsDirectory` pattern only applies to JSONL file reads.

**Edge cases requiring no new code:**
- Empty/null Cwd → filtered by `!string.IsNullOrEmpty` guard
- Network drives (UNC paths) → `Directory.Exists()` may be slow but is only called during session list rebuild (not per UI tick)
- Inaccessible directory → `Directory.Exists()` returns `false`, correctly filtered out

### Phase 4: Subagent Sorting Stabilization

**Scope:** `Services/JsonlService.cs` — `BuildSubagentContext()` return statement only

| Change | API | Already in Codebase? |
|--------|-----|---------------------|
| `result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList()` | LINQ + `StringComparer.Ordinal` | Yes — LINQ already used throughout `JsonlService` |

`StringComparer.Ordinal` is the correct comparer for agent IDs (byte-level, culture-independent, deterministic). `StringComparer.OrdinalIgnoreCase` would also work but is unnecessary since agent IDs are lowercase by convention.

### Phase 5: Footer Tooltip and Accessibility

**Scope:** Verification only — code is already complete.

**Finding from direct codebase inspection:** Both `Strings/de-DE/Resources.resw` and `Strings/en-US/Resources.resw` already contain all six required entries:

```
FooterRefreshButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip
FooterRefreshButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name
FooterSettingsButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip
FooterSettingsButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name
FooterQuitButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip
FooterQuitButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name
```

`MainView.xaml` already applies `l:Uids.Uid="FooterRefreshButton"`, `"FooterSettingsButton"`, `"FooterQuitButton"` to the respective buttons.

The attached-property path syntax `[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` is proven correct by the existing production entry at `de-DE/Resources.resw` line 47:
```
SessionComboBox.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name
```

**Remaining work:** Runtime smoke test only. No code changes expected.

---

## What NOT to Add

| Avoid | Why | Evidence |
|-------|-----|---------|
| Any new NuGet package | All required capabilities exist in the current dependency graph | Direct codebase inspection confirms — zero gaps |
| `System.IO.Path.GetFullPath()` normalization for Cwd | `Directory.Exists()` handles both normalized and non-normalized paths | BCL docs: `Directory.Exists` calls `NormalizePath` internally |
| Reactive extensions (Rx.NET) | `WeakReferenceMessenger` is sufficient for one new message type | Already used for `RefreshIntervalChangedMessage` — same pattern |
| SQLite/database for settings migration | `System.Text.Json` handles missing JSON keys via C# default values | Proven by all existing `AppSettings` properties |
| Separate `IModelContextService` abstraction | Overkill for static helper methods with no external I/O | `ModelContextLimits` is already a pure static class — adding an interface adds indirection with no testability benefit for pure math |

---

## Integration Points Requiring Attention

| Point | Risk | Mitigation |
|-------|------|-----------|
| `JsonlService` needs `SonnetContextSize` at `GetContextWindow()` call time | `JsonlService` may not currently have `ISettingsService` injected | Check constructor — if missing, inject via DI registration in `App.xaml.cs` |
| `GetEffectiveMaxTokens` signature change | Two call sites in `ContextWindowData.cs` | Build failure catches both — fix is mechanical |
| `ModelContextLimits.GetMaxContextTokens` overload | Original single-param overload must remain for callers that don't need Sonnet setting | Add as overload (default sonnet=200K), not replacement |
| `MainViewModel` subscription to `SonnetContextChangedMessage` | Must call `RefreshLocalData()` not `RefreshApiData()` | Context window is JSONL-derived (local), not API-derived |

---

## Sources

- Direct inspection: `CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs` — HIGH
- Direct inspection: `CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs` — HIGH
- Direct inspection: `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` — HIGH
- Direct inspection: `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` — HIGH
- Direct inspection: `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` (lines 640–780) — HIGH
- Direct inspection: `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` (footer section, lines 579–625) — HIGH
- Direct inspection: `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` (lines 100–118) — HIGH
- Direct inspection: `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` (lines 100–118) — HIGH
- `spec-release-from-1.7.1-to-1.8.3.md` — authoritative upgrade specification — HIGH

---
*Stack research for: ccInfoWin v1.2 macOS v1.8.3 feature parity*
*Researched: 2026-04-12*

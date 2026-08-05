# Phase 13: Sonnet Context Window Setting - Research

**Researched:** 2026-04-12
**Domain:** WinUI 3 / MVVM settings persistence — ComboBox binding, CommunityToolkit messenger, DI wiring
**Confidence:** HIGH

## Summary

Phase 13 is a pure wiring task. Every building block already exists in the codebase: `ModelContextLimits.GetMaxContextTokens` already accepts `sonnetContextSize`, the `SelectedLanguageIndex` ComboBox pattern in `SettingsViewModel` is the exact template to follow, and `RefreshIntervalChangedMessage` shows how to create a messenger message that triggers a UI refresh in `MainViewModel`. The only net-new work is: one `AppSettings` property, one `ObservableProperty` + partial method in `SettingsViewModel`, one message type, one messenger registration in `MainViewModel`, and wiring `ISettingsService` into `JsonlService`.

The one non-trivial decision is how `MainViewModel` responds to `SonnetContextChangedMessage`. There is no existing `RefreshLocalData()` method — the correct target is `UpdateSessionData(SelectedSession.Session)` called on the dispatcher queue, which is exactly what happens during a normal JSONL `DataUpdated` event. This must be called on the UI thread via `_dispatcherQueue.TryEnqueue`.

The DI registration for `JsonlService` in `App.xaml.cs` currently uses a factory lambda (`new JsonlService(pricingService: ...)`). Adding `ISettingsService` injection requires extending that lambda — no architectural change needed.

**Primary recommendation:** Follow `SelectedLanguageIndex` pattern for ViewModel/XAML; follow `RefreshIntervalChangedMessage` pattern for the messenger; extend the `JsonlService` constructor and update the DI lambda.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Settings Data Model
- Add `SonnetContextSize` property to `AppSettings` with `[JsonPropertyName("sonnetContextSize")]`, type `int`, default `200000`
- Valid values: `200000` or `1000000` — no enum needed, JSON deserialization auto-defaults to 200000 for missing keys (no migration)
- Phase 12 already added `sonnetContextSize` parameter to `GetMaxContextTokens(string?, long sonnetContextSize = 200_000)` — Phase 13 wires the setting

#### Settings UI
- Add `SelectedSonnetContextIndex` property to `SettingsViewModel` (int, 0=200K, 1=1M) — matches existing `SelectedLanguageIndex` pattern
- ComboBox position: after Language picker, before Pricing information section
- ComboBox items: "200K" and "1M" (localized labels)
- Initialize from `AppSettings.SonnetContextSize` in `SettingsViewModel.Initialize()`

#### Messenger Pattern
- Create new `SonnetContextChangedMessage` in `Messages/` directory — dedicated message type (not reusing RefreshIntervalChangedMessage)
- `SettingsViewModel.OnSelectedSonnetContextIndexChanged()` → save to settings → send `SonnetContextChangedMessage`
- `MainViewModel` registers for `SonnetContextChangedMessage` → calls `RefreshLocalData()` which re-reads context window data

#### JsonlService Integration
- Inject `ISettingsService` into `JsonlService` constructor — read `SonnetContextSize` setting in `GetContextWindow()` and `GetSubagentContext()`
- Pass `settings.SonnetContextSize` as `sonnetContextSize` parameter to `ModelContextLimits.GetMaxContextTokens()`
- Update DI registration in `App.xaml.cs` to add `ISettingsService` to `JsonlService` constructor

#### Localization
- Add to both `de-DE/Resources.resw` and `en-US/Resources.resw`:
  - Label: "Sonnet-Kontext" / "Sonnet Context"
  - Options: "200K" / "1M" (same in both languages)

### Claude's Discretion
- Whether ComboBox uses x:Uid or l:Uids.Uid for localization (existing pattern: l:Uids.Uid for runtime language switch)
- Internal method ordering in SettingsViewModel

### Deferred Ideas (OUT OF SCOPE)
None — Phase 13 is fully scoped by the spec.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SET-01 | User can configure Sonnet context size (200K or 1M) via ComboBox in Settings | `SelectedSonnetContextIndex` + XAML ComboBox with `SelectedIndex` binding |
| SET-02 | User sees default of 200K when no setting has been configured | `AppSettings.SonnetContextSize` defaults to `200000`; JSON missing-key deserialization returns default |
| SET-03 | User sees context window display update immediately after changing the Sonnet setting | `SonnetContextChangedMessage` → `MainViewModel` receiver → `UpdateSessionData` on dispatcher queue |
| SET-04 | User's Sonnet context setting persists across app restarts | `_settingsService.SaveSettings(settings)` in partial method; `Initialize()` reads from settings on page load |
| SET-05 | User sees localized labels for the Sonnet context picker (de-DE and en-US) | `l:Uids.Uid` on label TextBlock + ComboBoxItems; entries in both `.resw` files |
</phase_requirements>

---

## Standard Stack

### Core (all already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| CommunityToolkit.Mvvm | 8.4 | `[ObservableProperty]`, `partial void OnXChanged`, `WeakReferenceMessenger` | Project-standard MVVM; source generators generate boilerplate |
| WinUI3Localizer | in use | `l:Uids.Uid` runtime language switching | Existing pattern for all localized labels and ComboBoxItems |
| Microsoft.Extensions.DependencyInjection | in use | DI container wiring in `App.xaml.cs` | Project-standard DI |
| System.Text.Json | in use | `[JsonPropertyName]` on `AppSettings` | All settings serialization already uses this |

### No new packages required
All libraries needed are already in the project.

---

## Architecture Patterns

### Pattern 1: SelectedIndex ComboBox binding (exact template to follow)

From `SettingsViewModel.cs` — `SelectedLanguageIndex`:

```csharp
// SettingsViewModel.cs
[ObservableProperty]
private int _selectedLanguageIndex;

private static readonly string[] LanguageCodes = ["de-DE", "en-US"];

// In Initialize():
_selectedLanguageIndex = settings.Language == "en-US" ? 1 : 0;
OnPropertyChanged(nameof(SelectedLanguageIndex));

// Partial method (auto-called by source generator on property set):
partial void OnSelectedLanguageIndexChanged(int value)
{
    if (value >= 0 && value < LanguageCodes.Length)
    {
        var code = LanguageCodes[value];
        var settings = _settingsService.LoadSettings();
        settings.Language = code;
        _settingsService.SaveSettings(settings);
    }
}
```

For Sonnet context, use:
```csharp
private static readonly int[] SonnetContextSizes = [200_000, 1_000_000];

[ObservableProperty]
private int _selectedSonnetContextIndex;

// In Initialize():
_selectedSonnetContextIndex = settings.SonnetContextSize == 1_000_000 ? 1 : 0;
OnPropertyChanged(nameof(SelectedSonnetContextIndex));

partial void OnSelectedSonnetContextIndexChanged(int value)
{
    if (value >= 0 && value < SonnetContextSizes.Length)
    {
        var settings = _settingsService.LoadSettings();
        settings.SonnetContextSize = SonnetContextSizes[value];
        _settingsService.SaveSettings(settings);
        WeakReferenceMessenger.Default.Send(new SonnetContextChangedMessage(SonnetContextSizes[value]));
    }
}
```

### Pattern 2: ValueChangedMessage (exact template to follow)

From `RefreshIntervalChangedMessage.cs`:
```csharp
// Source: existing Messages/RefreshIntervalChangedMessage.cs
public class RefreshIntervalChangedMessage : ValueChangedMessage<int>
{
    public RefreshIntervalChangedMessage(int intervalSeconds) : base(intervalSeconds) { }
}
```

`SonnetContextChangedMessage` carries the new size as `long` (matches `GetMaxContextTokens` parameter type):
```csharp
// Messages/SonnetContextChangedMessage.cs
public class SonnetContextChangedMessage : ValueChangedMessage<long>
{
    public SonnetContextChangedMessage(long contextSize) : base(contextSize) { }
}
```

### Pattern 3: MainViewModel messenger registration

Existing pattern for `RefreshIntervalChangedMessage` (line 285 of MainViewModel.cs):
```csharp
WeakReferenceMessenger.Default.Register<RefreshIntervalChangedMessage>(this, (r, m) =>
{
    ((MainViewModel)r).UpdateRefreshInterval(m.Value);
});
```

For `SonnetContextChangedMessage`, the receiver must dispatch to the UI thread before touching observable properties:
```csharp
WeakReferenceMessenger.Default.Register<SonnetContextChangedMessage>(this, (r, m) =>
{
    var vm = (MainViewModel)r;
    vm._dispatcherQueue?.TryEnqueue(() =>
    {
        if (vm.SelectedSession != null)
            vm.UpdateSessionData(vm.SelectedSession.Session);
    });
});
```

**Critical:** `UpdateSessionData` touches `ContextUtilization`, `ContextPercentage`, etc. — all `[ObservableProperty]` fields that must be set on the UI thread. `_dispatcherQueue` is set during `InitializeAsync`. If `SelectedSession` is null (no active session), no update is needed.

### Pattern 4: JsonlService ISettingsService injection

Current constructor (line 104):
```csharp
public JsonlService(
    string? projectsDirectoryOverride = null,
    string? cacheDirectoryOverride = null,
    IPricingService? pricingService = null)
```

Extended constructor:
```csharp
public JsonlService(
    string? projectsDirectoryOverride = null,
    string? cacheDirectoryOverride = null,
    IPricingService? pricingService = null,
    ISettingsService? settingsService = null)
{
    _settingsService = settingsService ?? new NullSettingsService();
    // ... existing init
}
```

**Note:** A `NullSettingsService` returning default `AppSettings` (with `SonnetContextSize = 200000`) is needed for test isolation — existing tests construct `JsonlService` without a settings service. Alternatively, the parameter can default to `null` and be guarded: `_settingsService?.LoadSettings()?.SonnetContextSize ?? 200_000`.

The DI factory lambda in `App.xaml.cs` (line 141):
```csharp
// Current:
services.AddSingleton<IJsonlService>(sp =>
    new JsonlService(pricingService: sp.GetRequiredService<IPricingService>()));

// Updated:
services.AddSingleton<IJsonlService>(sp =>
    new JsonlService(
        pricingService: sp.GetRequiredService<IPricingService>(),
        settingsService: sp.GetRequiredService<ISettingsService>()));
```

### Pattern 5: XAML ComboBox with l:Uids.Uid (exact pattern from SettingsView.xaml)

Language ComboBox (existing, lines 135–141):
```xml
<ComboBox Grid.Column="1"
          l:Uids.Uid="LanguageComboBox"
          Width="120"
          SelectedIndex="{x:Bind ViewModel.SelectedLanguageIndex, Mode=TwoWay}">
    <ComboBoxItem Content="Deutsch" />
    <ComboBoxItem Content="English" />
</ComboBox>
```

Sonnet context ComboBox — options are same in both languages ("200K" / "1M"), so Content can be hardcoded; only the label TextBlock needs localization:
```xml
<!-- Sonnet context row — after Language row, before Reset window size -->
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock l:Uids.Uid="SettingsSonnetContextLabel"
               FontSize="13" Foreground="{ThemeResource PrimaryTextBrush}"
               VerticalAlignment="Center" />
    <ComboBox Grid.Column="1"
              l:Uids.Uid="SonnetContextComboBox"
              Width="120"
              SelectedIndex="{x:Bind ViewModel.SelectedSonnetContextIndex, Mode=TwoWay}">
        <ComboBoxItem Content="200K" />
        <ComboBoxItem Content="1M" />
    </ComboBox>
</Grid>
```

**Discretion resolved:** Use `l:Uids.Uid` (not `x:Uid`) — this is the project-standard pattern for runtime language switching. The ComboBox options ("200K" / "1M") are language-neutral and can be hardcoded in XAML.

### Recommended Localization Keys

In `de-DE/Resources.resw`:
```xml
<data name="SettingsSonnetContextLabel.Text" xml:space="preserve">
  <value>Sonnet-Kontext:</value>
</data>
<data name="SonnetContextComboBox.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
  <value>Sonnet-Kontextgröße wählen</value>
</data>
```

In `en-US/Resources.resw`:
```xml
<data name="SettingsSonnetContextLabel.Text" xml:space="preserve">
  <value>Sonnet Context:</value>
</data>
<data name="SonnetContextComboBox.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
  <value>Select Sonnet context size</value>
</data>
```

### XAML placement: after Language row, before Reset window size row

Current order in ANWENDUNG StackPanel:
1. Autostart row
2. Language row
3. Reset window size row

New order:
1. Autostart row
2. Language row
3. **Sonnet context row** ← insert here
4. Reset window size row

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Property-change-to-save pipeline | Custom INotifyPropertyChanged callbacks | `partial void OnXChanged` from CommunityToolkit source generators | Generators handle thread safety, null guards, EqualityComparer |
| Messenger publish/subscribe | Custom event bus | `WeakReferenceMessenger` | Already in project; prevents memory leaks via weak references |
| Settings JSON round-trip | Manual serialization | `[JsonPropertyName]` + `SettingsService.LoadSettings/SaveSettings` | Already handles file I/O, defaults, error recovery |

---

## Common Pitfalls

### Pitfall 1: Setting observable properties outside UI thread
**What goes wrong:** `UpdateSessionData` assigns `[ObservableProperty]` fields — WinUI 3 binding engine requires mutations on the UI thread. Calling from a messenger handler (which runs on the sender's thread — the settings ViewModel UI thread — but safer to be explicit) without `_dispatcherQueue.TryEnqueue` can cause cross-thread binding exceptions.
**Why it happens:** Messenger handlers fire synchronously on the thread that sends the message. SettingsViewModel sends from a `partial void` callback which is on the UI thread, so in practice it is safe here — but the guard is cheap and defensive.
**How to avoid:** Always wrap `UpdateSessionData` calls in `_dispatcherQueue?.TryEnqueue(...)` when triggered from messenger.
**Warning signs:** `System.Runtime.InteropServices.COMException` or `UnauthorizedAccessException` from WinUI binding at runtime.

### Pitfall 2: `_selectedSonnetContextIndex` set during Initialize() fires OnChanged partial method
**What goes wrong:** Setting `_selectedSonnetContextIndex` (backing field directly) in `Initialize()` does NOT trigger the partial method — that's correct. But using the public property `SelectedSonnetContextIndex = ...` in Initialize would trigger `OnSelectedSonnetContextIndexChanged`, causing a premature save + send during initialization.
**Why it happens:** CommunityToolkit source generators fire the partial method when the PUBLIC property setter is used, not when the backing field is set directly.
**How to avoid:** Always set the backing field `_selectedSonnetContextIndex` in `Initialize()`, then call `OnPropertyChanged(nameof(SelectedSonnetContextIndex))` explicitly — same pattern as `_selectedLanguageIndex` in the existing code (line 93-99).

### Pitfall 3: JsonlService tests break after adding ISettingsService parameter
**What goes wrong:** Existing tests construct `JsonlService` directly with only `projectsDirectoryOverride` — adding a required `settingsService` parameter breaks 13+ existing tests.
**Why it happens:** Constructor signature change.
**How to avoid:** Make `settingsService` an optional parameter with default `null`. In `GetContextWindow` and `BuildSubagentContext`, read: `var sonnetContextSize = _settingsService?.LoadSettings().SonnetContextSize ?? ModelContextLimits.DefaultContextLimit;`

### Pitfall 4: `SelectedSonnetContextIndex` initialized to wrong value for 1M setting
**What goes wrong:** If mapping is wrong (e.g., defaulting to 0 for any non-200K value instead of only mapping 1M → 1), a corrupted settings file with an unexpected value would reset silently.
**Why it happens:** Direct comparison without range check.
**How to avoid:** Use `settings.SonnetContextSize == 1_000_000 ? 1 : 0` — explicit equality, not `>` comparison. This matches the LanguageCodes pattern: `settings.Language == "en-US" ? 1 : 0`.

---

## Code Examples

### AppSettings — property to add
```csharp
// Source: existing AppSettings.cs pattern
[JsonPropertyName("sonnetContextSize")]
public int SonnetContextSize { get; set; } = 200_000;
```

### GetContextWindow — sonnetContextSize wiring
```csharp
// Source: JsonlService.cs GetContextWindow(), line ~151
var sonnetContextSize = _settingsService?.LoadSettings().SonnetContextSize
    ?? ModelContextLimits.DefaultContextLimit;
var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize);
```

### BuildSubagentContext — sonnetContextSize wiring
```csharp
// Source: JsonlService.cs BuildSubagentContext(), line ~693
var sonnetContextSize = _settingsService?.LoadSettings().SonnetContextSize
    ?? ModelContextLimits.DefaultContextLimit;
var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName, sonnetContextSize);
```

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 + Moq 4.20.72 |
| Config file | CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ModelContextLimits" -x` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SET-01 | ComboBox SelectedIndex binds to SelectedSonnetContextIndex | manual (WinUI UI test) | manual only — WinUI controls require running app | N/A |
| SET-02 | Default 200K when SonnetContextSize missing from JSON | unit | `dotnet test ... --filter "FullyQualifiedName~AppSettings"` | ❌ Wave 0 |
| SET-03 | Context display updates immediately after setting change | manual (WinUI UI test) | manual only — requires running app + DispatcherQueue | N/A |
| SET-04 | SonnetContextSize persists across restart | unit | `dotnet test ... --filter "FullyQualifiedName~SettingsService"` | existing (SettingsService tests if present) |
| SET-05 | Localized labels | manual | manual only — WinUI3Localizer requires running app | N/A |

**Additional unit tests to add:**

| Behavior | Test Type | File |
|----------|-----------|------|
| `GetMaxContextTokens("claude-sonnet-x", 1_000_000)` returns 1M (already passes — see ModelContextLimitsTests.cs line 43) | unit | ✅ Exists |
| `GetMaxContextTokens("claude-sonnet-x")` default returns 200K (already passes) | unit | ✅ Exists |
| `JsonlService.GetContextWindow` uses `settingsService.SonnetContextSize` for Sonnet | unit | ❌ Wave 0 |
| `AppSettings.SonnetContextSize` defaults to 200000 | unit | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ModelContextLimits" -x`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` — add test: `GetContextWindow_SonnetModel_UsesSonnetContextSizeFromSettings`
- [ ] `CCInfoWindows.Tests/Models/AppSettingsTests.cs` — add test: `SonnetContextSize_DefaultIs200000` and `SonnetContextSize_DeserializesFromJson`

---

## Environment Availability

Step 2.6: SKIPPED (no external dependencies — purely code/config changes within existing .NET 9 WinUI 3 project)

---

## Project Constraints (from CLAUDE.md)

| Directive | Impact on Phase 13 |
|-----------|-------------------|
| No @example blocks in JSDoc/TSDoc | No example blocks in any XML doc comments added |
| `[ObservableProperty]` for bindable properties | `SelectedSonnetContextIndex` uses `[ObservableProperty]` |
| No code-behind logic in Views | All index-to-size mapping stays in SettingsViewModel |
| `partial class` with source generators | SettingsViewModel is already `partial`; new property follows this |
| Conventional Commits | Commit: `feat(settings): add Sonnet context window size picker` |
| PascalCase public / _camelCase private | `SelectedSonnetContextIndex` / `_selectedSonnetContextIndex` |
| I-prefix interfaces | `ISettingsService` already exists |
| No magic numbers | Use named constants `ModelContextLimits.DefaultContextLimit` and `ModelContextLimits.ExtendedContextLimit` instead of `200_000` and `1_000_000` |
| Small functions (SRP) | `OnSelectedSonnetContextIndexChanged` does exactly one thing: save + send |
| Wrap external libraries | Already satisfied — settings access goes through ISettingsService |
| NEVER chain commands with `;`, `&&` in Bash | Applies to executor only |
| Security: no secrets in code | Not applicable to this feature |

---

## Open Questions

1. **`RefreshLocalData()` method referenced in CONTEXT.md does not exist**
   - What we know: CONTEXT.md says `MainViewModel` calls `RefreshLocalData()` on receiving `SonnetContextChangedMessage`. No such method exists in `MainViewModel.cs`.
   - What's unclear: Whether this is a method to create, or an alias for an existing method.
   - Recommendation: Implement as `UpdateSessionData(SelectedSession.Session)` wrapped in `_dispatcherQueue?.TryEnqueue(...)`. This is the correct existing equivalent — it re-reads context from JsonlService and updates all context observables. No separate `RefreshLocalData` method is needed; just inline the call in the messenger registration lambda (or extract to a named private method `RefreshContextDisplay()` for clarity).

2. **`ISettingsService` null guard vs. `NullSettingsService` for test isolation**
   - What we know: Existing `JsonlService` tests pass `null` for `pricingService` and get a `NullPricingService`. The same pattern would work for settings.
   - What's unclear: Whether to add a `NullSettingsService` class or use null-coalescing inline.
   - Recommendation: Use null-coalescing inline (`_settingsService?.LoadSettings().SonnetContextSize ?? ModelContextLimits.DefaultContextLimit`) — simpler, avoids a new class, consistent with the fact that NullSettingsService would only return defaults anyway.

---

## Sources

### Primary (HIGH confidence)
- Direct source code read: `SettingsViewModel.cs` — `SelectedLanguageIndex` pattern (lines 53-99, 130-136)
- Direct source code read: `RefreshIntervalChangedMessage.cs` — message template
- Direct source code read: `ModelContextLimits.cs` — `GetMaxContextTokens(string?, long sonnetContextSize)` already accepts parameter
- Direct source code read: `JsonlService.cs` — constructor (lines 104-116), `GetContextWindow` (lines 135-163), `BuildSubagentContext` (lines 679-716)
- Direct source code read: `App.xaml.cs` — DI registration lambda (lines 141-142)
- Direct source code read: `SettingsView.xaml` — Language ComboBox XAML pattern (lines 127-142)
- Direct source code read: `de-DE/Resources.resw` and `en-US/Resources.resw` — existing key patterns
- Direct source code read: `ModelContextLimitsTests.cs` — existing test coverage for `GetMaxContextTokens` with sonnetContextSize

### Secondary (MEDIUM confidence)
- CONTEXT.md Phase 13 — locked implementation decisions

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in use, no new dependencies
- Architecture patterns: HIGH — read directly from existing source code
- Pitfalls: HIGH — derived from reading actual constructor signatures and partial method behavior
- Test map: MEDIUM — unit tests for UI behavior (SET-01, SET-03, SET-05) are manual-only by WinUI 3 constraint

**Research date:** 2026-04-12
**Valid until:** Stable (no fast-moving dependencies — all within project codebase)

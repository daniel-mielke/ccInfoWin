# Phase 13: Sonnet Context Window Setting - Context

**Gathered:** 2026-04-12
**Status:** Ready for planning
**Mode:** Auto-generated (spec-driven decisions from spec-release-from-1.7.1-to-1.8.3.md Phase 2)

<domain>
## Phase Boundary

Add a Sonnet context window size picker (200K / 1M) to the Settings view. The setting persists to settings.json, triggers live refresh via messenger, and feeds into Phase 12's `sonnetContextSize` parameter in `ModelContextLimits.GetMaxContextTokens()`. End-to-end: AppSettings → Settings UI → Messenger → MainViewModel → JsonlService → ModelContextLimits.

</domain>

<decisions>
## Implementation Decisions

### Settings Data Model
- Add `SonnetContextSize` property to `AppSettings` with `[JsonPropertyName("sonnetContextSize")]`, type `int`, default `200000`
- Valid values: `200000` or `1000000` — no enum needed, JSON deserialization auto-defaults to 200000 for missing keys (no migration)
- Phase 12 already added `sonnetContextSize` parameter to `GetMaxContextTokens(string?, long sonnetContextSize = 200_000)` — Phase 13 wires the setting

### Settings UI
- Add `SelectedSonnetContextIndex` property to `SettingsViewModel` (int, 0=200K, 1=1M) — matches existing `SelectedLanguageIndex` pattern
- ComboBox position: after Language picker, before Pricing information section
- ComboBox items: "200K" and "1M" (localized labels)
- Initialize from `AppSettings.SonnetContextSize` in `SettingsViewModel.Initialize()`

### Messenger Pattern
- Create new `SonnetContextChangedMessage` in `Messages/` directory — dedicated message type (not reusing RefreshIntervalChangedMessage)
- `SettingsViewModel.OnSelectedSonnetContextIndexChanged()` → save to settings → send `SonnetContextChangedMessage`
- `MainViewModel` registers for `SonnetContextChangedMessage` → calls `RefreshLocalData()` which re-reads context window data

### JsonlService Integration
- Inject `ISettingsService` into `JsonlService` constructor — read `SonnetContextSize` setting in `GetContextWindow()` and `GetSubagentContext()`
- Pass `settings.SonnetContextSize` as `sonnetContextSize` parameter to `ModelContextLimits.GetMaxContextTokens()`
- Update DI registration in `App.xaml.cs` to add `ISettingsService` to `JsonlService` constructor

### Localization
- Add to both `de-DE/Resources.resw` and `en-US/Resources.resw`:
  - Label: "Sonnet-Kontext" / "Sonnet Context"
  - Options: "200K" / "1M" (same in both languages)

### Claude's Discretion
- Whether ComboBox uses x:Uid or l:Uids.Uid for localization (existing pattern: l:Uids.Uid for runtime language switch)
- Internal method ordering in SettingsViewModel

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `SettingsViewModel.cs` — existing ComboBox patterns: `RefreshOption` record + `SelectedRefreshOption`, `SelectedLanguageIndex` + `LanguageCodes` array
- `RefreshIntervalChangedMessage.cs` — template for creating `SonnetContextChangedMessage`
- `AppSettings.cs` — `[JsonPropertyName]` annotation pattern, all properties have defaults
- `ModelContextLimits.GetMaxContextTokens(string?, long sonnetContextSize = 200_000)` — the hook from Phase 12

### Established Patterns
- Settings change: `OnPropertyChanged` → `LoadSettings()` → mutate → `SaveSettings()` → send message
- Language switch uses `l:Uids.Uid` for runtime localization (not `x:Uid`)
- Messages are simple records in `Messages/` directory
- DI registration in `App.xaml.cs` with `services.AddSingleton<>()`

### Integration Points
- `App.xaml.cs` — DI registration for JsonlService constructor update
- `JsonlService.GetContextWindow()` line ~151 and `BuildSubagentContext()` line ~693 — pass sonnetContextSize
- `MainViewModel` — register for `SonnetContextChangedMessage`
- `SettingsView.xaml` — add ComboBox after Language picker

</code_context>

<specifics>
## Specific Ideas

- Spec reference: `spec-release-from-1.7.1-to-1.8.3.md` Phase 2 (lines 154-226)
- Follow `SelectedLanguageIndex` pattern exactly — it's the simplest ComboBox binding in the codebase
- `SonnetContextChangedMessage` should carry the new size as constructor parameter (like `RefreshIntervalChangedMessage(int Seconds)`)

</specifics>

<deferred>
## Deferred Ideas

None — Phase 13 is fully scoped by the spec.

</deferred>

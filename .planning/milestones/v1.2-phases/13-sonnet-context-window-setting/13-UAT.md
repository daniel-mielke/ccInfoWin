---
status: complete
phase: 13-sonnet-context-window-setting
source: [13-01-SUMMARY.md, 13-02-SUMMARY.md]
started: 2026-04-12T20:16:00+02:00
updated: 2026-04-12T20:16:00+02:00
---

## Current Test

[testing complete]

## Tests

### 1. Sonnet Context Size ComboBox in Settings
expected: Settings view shows a ComboBox for Sonnet context size after the language selection. Options are 200K and 1M. Default is 200K.
result: pass

### 2. Sonnet Context Setting Persists
expected: Change Sonnet context size to 1M, close and reopen Settings. The 1M selection is preserved.
result: pass

### 3. Localized Labels (DE/EN)
expected: Sonnet context label shows in the current language — German "Sonnet Kontextfenster" or English equivalent.
result: pass

### 4. Live Context Bar Refresh on Setting Change
expected: Change Sonnet context size from 200K to 1M while a Sonnet session is active. The context window progress bar updates immediately — max tokens changes from ~167K effective to ~967K effective without requiring a page refresh.
result: pass

### 5. DI Wiring — JsonlService Reads SonnetContextSize
expected: JsonlService correctly reads the user-configured SonnetContextSize from ISettingsService and passes it to GetMaxContextTokens. Sonnet sessions respect the configured value.
result: pass

### 6. Messenger Handler — UI Thread Safety
expected: Changing the Sonnet context setting triggers SonnetContextChangedMessage which updates the UI on the dispatcher thread. No thread-related crashes or frozen UI.
result: pass

## Summary

total: 6
passed: 6
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps

[none]

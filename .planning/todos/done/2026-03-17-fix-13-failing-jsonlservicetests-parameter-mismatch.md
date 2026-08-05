---
created: 2026-03-17T20:47:21.033Z
title: Fix 13 failing JsonlServiceTests parameter mismatch
area: testing
files:
  - CCInfoWindows.Tests/Services/JsonlServiceTests.cs
  - CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs
  - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
---

## Problem

13 of 22 `JsonlServiceTests` fail because tests pass UUID-format session IDs (e.g., `"session-ctx-1"`) to `GetContextWindow`/`GetTokenSummary`/`GetStatistics`, while the `JsonlService` implementation indexes by `projectDirName` (directory name from the JSONL file path).

The production app works correctly because `MainViewModel` always passes `session.Id` which equals the `projectDirName`. Only the test layer is broken.

Failing tests include:
- `GetContextWindow_ReturnsLastAssistantMessageTokens_NotCumulative`
- `GetContextWindow_IgnoresSidechainMessages`
- `GetContextWindow_IncludesSubagentData`
- `GetTokenSummary_*` (3 tests)
- `Sessions_*` (3 tests)
- `GetStatistics_*` (4 tests)

Identified in v1.0 milestone audit as INT-01 (WARNING severity).

## Solution

1. Rename `IJsonlService` parameter from `sessionId` to `projectDirName` in interface docs and method signatures
2. Update all 13 failing tests to use `projectDirName`-based keys that match the directory structure created by test setup
3. Ensure test helper creates project directories with names matching what `JsonlService.InitializeAsync()` discovers

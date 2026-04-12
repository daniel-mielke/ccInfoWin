# Pitfalls Research

**Domain:** Adding model-based context detection, settings-driven data logic, FS-state filtering, sort stabilization, and WinUI 3 tooltip localization to an existing MVVM app (ccInfoWin v1.2)
**Researched:** 2026-04-12
**Confidence:** HIGH — all findings derived from direct codebase analysis of the live implementation

---

## Critical Pitfalls

### Pitfall 1: `GetEffectiveMaxTokens` Signature Change Silently Breaks `ContextWindowData`

**What goes wrong:**
`GetEffectiveMaxTokens(long currentTokens, long maxTokens)` is called in two computed properties inside the immutable records `ContextWindowData.Utilization` and `SubagentContextData.Utilization`. If the Phase 1 refactor changes or removes this method without updating both records, the app compiles but calculates utilization against the old effective-max, producing wrong progress bar fills and percentage text — with no runtime error.

**Why it happens:**
Both computed properties live in `Models/ContextWindowData.cs`, not in `JsonlService` where most of the Phase 1 changes land. The refactor removes `currentTokens` from the signature (no longer needed after the buffer is flattened to 33K). Forgetting to update the model callers means they still pass two arguments, or — worse — the old two-argument overload is kept for "compatibility" and the model properties silently keep calling the heuristic path.

**How to avoid:**
Delete `ExtendedAutocompactBuffer` and `ExtendedContextDetectionThreshold` from `ModelContextLimits` in the same commit that changes the signature, so the compiler immediately flags all unresolved references in `ContextWindowData.cs`. Do not leave deprecated constants as stubs.

**Warning signs:**
- Opus session shows a context bar below 100% when tokens exceed 200K (capped at the old 200K effective max)
- Utilization for any session is negative or exceeds 1.0 (division by wrong effective max)

**Phase to address:** Phase 1

---

### Pitfall 2: `JsonlService` Reads `AppSettings` On Every `GetContextWindow` Call — Or Never

**What goes wrong:**
Phase 1 makes `GetMaxContextTokens` for Sonnet depend on the configured value. `JsonlService` does not currently take `ISettingsService` as a dependency (it only takes `IPricingService`). There are two bad implementations:

1. Calling `_settingsService.LoadSettings()` on each `GetContextWindow` invocation — which hits the disk on every UI refresh cycle inside `_sessionsLock`, causing read-under-lock on the hot path.
2. Reading the setting once in the constructor and caching it locally — which means a Phase 2 setting change never takes effect until app restart.

Both break a correctness or performance invariant.

**How to avoid:**
Inject `ISettingsService` into `JsonlService` via the constructor (update `App.xaml.cs` DI registration at line 141). Store `SonnetContextSize` in a private field. Add a `public void UpdateSonnetContextSize(long size)` method — or register for `SonnetContextChangedMessage` inside `JsonlService` — so the value can be updated without disk I/O on every call. Keep `_sessionsLock` free of any I/O.

**Warning signs:**
- `_sessionsLock` is held while `File.ReadAllText` or `LoadSettings()` is called
- Changing the Sonnet Context setting has no effect on the displayed context bar until app restart

**Phase to address:** Phase 1 (inject dependency), Phase 2 (wire up live update)

---

### Pitfall 3: `ContextLimits` Dictionary Still Has `200_000` for All Opus Keys

**What goes wrong:**
`ModelContextLimits.cs` contains a hard-coded dictionary with explicit entries like `["claude-opus-4-6"] = 200_000`. After Phase 1 adds `GetModelFamily` family-based logic, `GetMaxContextTokens` may route through the dictionary first for known model names. If the dictionary is not updated (or removed), a real Opus session returns 200K because the dictionary short-circuits the new family-based logic for exact-match model names.

**Why it happens:**
The dictionary was the original source of truth. After Phase 1, there are two competing sources: the dictionary (fast-path for known models) and `GetModelFamily` (family-based for all models). The refactor must make a clear decision: keep the dictionary and update all Opus entries to 1,000,000, or delete the dictionary entirely and rely only on family detection. Leaving the dictionary with stale 200K Opus values is the worst of both worlds.

**How to avoid:**
In Phase 1: decide once whether to keep or remove the dictionary. If keeping it, update every `"claude-opus-*"` entry to `1_000_000`. If removing it, delete the dictionary entirely. Add a unit test: `Assert.Equal(1_000_000, ModelContextLimits.GetMaxContextTokens("claude-opus-4-6"))`.

**Warning signs:**
- An Opus session shows ~167K effective context instead of ~967K
- The `ContextLimits` dictionary still contains `["claude-opus-4-6"] = 200_000` after Phase 1 is committed

**Phase to address:** Phase 1

---

### Pitfall 4: `Initialize()` in `SettingsViewModel` Fires `OnSelectedSonnetContextIndexChanged` Prematurely

**What goes wrong:**
`SettingsViewModel.Initialize()` (lines 85–100) follows a specific pattern: it assigns the *backing field* (`_selectedRefreshOption = ...`) then calls `OnPropertyChanged` explicitly. This avoids triggering the `partial void OnXChanged` side-effect handler during initialization. A developer adding the Sonnet picker may assign the generated property (`SelectedSonnetContextIndex = ...`) instead of the backing field. This fires `partial void OnSelectedSonnetContextIndexChanged` immediately during initialization, saving to settings and sending a messenger message before the view is ready — potentially overwriting a previously saved `1000000` value with the default `0 = 200K`.

**How to avoid:**
Follow the exact existing pattern in `Initialize()`: assign `_selectedSonnetContextIndex` (backing field), then call `OnPropertyChanged(nameof(SelectedSonnetContextIndex))`. Never assign the generated property setter inside `Initialize()`.

**Warning signs:**
- On app startup, a Sonnet session briefly shows 1M context then reverts to 200K
- `settings.json` shows `sonnetContextSize: 200000` even though the user previously saved `1000000`

**Phase to address:** Phase 2

---

### Pitfall 5: Session Filter Removes the Currently Selected Session Without UI Recovery

**What goes wrong:**
`RebuildSessionsList()` will filter out orphaned sessions on the next FileSystemWatcher tick or manual refresh. If `MainViewModel.SelectedSession` is one of the filtered sessions, the ComboBox binding removes it, but `MainViewModel` does not detect the removal: `SelectedSession` becomes null or stale, `UpdateSessionData` is called with a null session, and the UI displays empty context data indefinitely — or crashes on a null dereference.

**Why it happens:**
`RebuildSessionsList()` builds a new `_sessions` list and fires `DataUpdated`. `MainViewModel.RefreshSessionList()` re-selects based on `lastSelectedSessionId`. If the selected session's `Cwd` no longer exists, it disappears from the filtered list but `RefreshSessionList` may not recognize it as absent if the session ID still appears in `_settings.LastSelectedSessionId`.

**How to avoid:**
After applying the `Directory.Exists` filter in `RebuildSessionsList`, ensure the resulting `_sessions` list is the authoritative source. `MainViewModel.RefreshSessionList()` already handles the "not found" case by falling back to the most-recent session — this works correctly as long as the filtered `_sessions` list is consistent before `DataUpdated` fires. The key: do not keep the orphaned session in `_sessions` as a tombstone.

**Warning signs:**
- After deleting a project directory, the session dropdown shows "No active session" but context bars still show old non-zero values
- `NullReferenceException` in `UpdateSessionData` after project directory deletion

**Phase to address:** Phase 3

---

### Pitfall 6: `Directory.Exists` Called on Unvalidated External Path (NTLM Hash Leak)

**What goes wrong:**
`SessionInfo.Cwd` is populated from JSONL file content — external, untrusted data. Passing it directly to `Directory.Exists(s.Cwd)` violates the project's secure coding rule ("Restrict file paths — never pass user-supplied paths directly"). A crafted JSONL file with a UNC path (`\\attacker-server\share`) causes `Directory.Exists` to initiate an SMB connection attempt, leaking the Windows hostname and NTLM hash to a rogue server.

**Why it happens:**
The spec describes the filter as a one-liner `.Where(s => Directory.Exists(s.Cwd))`. The simplicity makes it easy to skip path validation. Claude Code writes the JSONL files, so in normal use the paths are benign — but the rule exists for any input that the app does not control.

**How to avoid:**
Guard the path before calling `Directory.Exists`: validate that `Cwd` is a rooted, non-UNC, local path: `!string.IsNullOrEmpty(s.Cwd) && Path.IsPathRooted(s.Cwd) && !s.Cwd.StartsWith(@"\\") && Directory.Exists(s.Cwd)`. This is two conditions, not a rewrite.

**Warning signs:**
- Absence of any path validation guard around the `Directory.Exists` call in `RebuildSessionsList`
- Wireshark shows SMB traffic on app startup when a UNC path is present in a JSONL file

**Phase to address:** Phase 3

---

### Pitfall 7: Subagent Sort Applied at Call Site Instead of Inside `BuildSubagentContext`

**What goes wrong:**
`BuildSubagentContext` is a private static method called from both `GetContextWindow` (line 153) and `GetSubagentContext` (line 174). If the Phase 4 `OrderBy` is added at one call site instead of inside the method, the list is sorted in one context and unsorted in the other — the same instability that Phase 4 aims to fix.

**How to avoid:**
Add `return result.OrderBy(a => a.AgentId, StringComparer.Ordinal).ToList();` as the final statement inside `BuildSubagentContext` before returning. No changes to `GetContextWindow` or `GetSubagentContext` are needed. After Phase 4: grep for `OrderBy.*AgentId` — it must appear exactly once, inside `BuildSubagentContext`.

**Warning signs:**
- Subagent bars jump order on FSW-triggered refreshes but not on manual refresh (or vice versa)
- The `OrderBy` appears in `GetContextWindow` or `GetSubagentContext` instead of inside `BuildSubagentContext`

**Phase to address:** Phase 4

---

### Pitfall 8: `ToolTipService.ToolTip` Set as XAML Attribute Overrides WinUI3Localizer

**What goes wrong:**
WinUI3Localizer applies `ToolTipService.ToolTip` by reading the resw key `ButtonUid.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` and setting it as an attached property at runtime. If a developer also adds `ToolTipService.ToolTip="Refresh"` as a XAML attribute on the button, this static value is applied at XAML load time and overwrites what the localizer sets at runtime. The tooltip freezes in the build-time language and never changes when the user switches languages.

**Why it happens:**
The spec example shows `<Button ToolTipService.ToolTip="{l:Uids.Uid FooterRefreshTooltip}" ...>` as a possible implementation. This is the wrong approach. The correct approach is the pure-resw method: only define the tooltip in resw with the attached-property key syntax, and let the existing `l:Uids.Uid="FooterRefreshButton"` pick it up automatically. The three footer buttons already have their Uid attributes — no XAML change is needed beyond verifying the resw entries exist.

**How to avoid:**
Do not add any `ToolTipService.ToolTip` attribute to `MainView.xaml`. Add only the `.ToolTipService.ToolTip` and `.AutomationProperties.Name` entries to both resw files. The `en-US/Resources.resw` entries already exist (confirmed at lines 101–118). Verify `de-DE/Resources.resw` also has all six entries before marking Phase 5 done.

**Warning signs:**
- Tooltip shows "Refresh" in German mode (static override beating the localizer)
- Language switch leaves tooltip in the previous language
- Grep of `MainView.xaml` shows `ToolTipService.ToolTip` as a XAML attribute

**Phase to address:** Phase 5

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Keep `ContextLimits` dictionary with partial updates (only Opus entries changed) | Low-effort Phase 1 | Dictionary and family-based logic diverge on new model releases; two places to update per new model | Never — pick one canonical source of truth |
| Pass `SonnetContextSize` as parameter without DI into `JsonlService` | Avoids constructor change | Callers must obtain the setting themselves; `MainViewModel` and `JsonlService` both read settings independently and can drift | Never for this app — DI is already wired |
| Call `Directory.Exists` on `Cwd` without path validation | One-liner implementation | NTLM hash leak to UNC paths in crafted JSONL files | Never — the validation guard is two conditions |
| Add `ToolTipService.ToolTip` as XAML attribute instead of resw entry | No resw file edits needed | Breaks runtime language switching; tooltip is frozen in build-time language | Never — defeats WinUI3Localizer entirely |
| Skip `de-DE` resw entries for Phase 5 | Half the work | German users see English tooltip strings on hover | Never — both resw files must be in sync |
| Keep deprecated constants (`ExtendedAutocompactBuffer`, `ExtendedContextDetectionThreshold`) as stubs | No compiler errors during refactor | Dead code in static class; callers may revert to using them; heuristic path silently lives on | Never — delete them in the same commit |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| WinUI3Localizer + `ToolTipService.ToolTip` | Setting the attached property as a XAML attribute overrides what the localizer writes at runtime | Only define it in resw with the attached-property path syntax; the button's existing `l:Uids.Uid` picks it up automatically |
| WinUI3Localizer + `AutomationProperties.Name` | Wrong namespace prefix in resw key | Copy the exact pattern from `en-US/Resources.resw` line 47: `SessionComboBox.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` |
| `ModelContextLimits` static helper + runtime setting | Static methods have no DI access; embedding `ISettingsService` in a static class breaks testability | Pass `sonnetContextSize` as a parameter to `GetMaxContextTokens`; the DI-injected `JsonlService` holds the setting |
| CommunityToolkit `[ObservableProperty]` + `Initialize()` | Assigning the generated property setter in `Initialize()` fires `OnXChanged` before view is ready | Assign the backing field (`_x`) in `Initialize()`, then call `OnPropertyChanged(nameof(X))` explicitly — match the existing pattern on lines 95–99 |
| `FileSystemWatcher` + session filter + `Directory.Exists` | Calling `Directory.Exists` inside the FSW callback (hot path, called on every file change) | Only call `Directory.Exists` inside `RebuildSessionsList`, which runs after the 2-second debounce — never in `OnFileChanged` |
| `IJsonlService` interface + new `UpdateSonnetContextSize` method | Adding a method to `JsonlService` without updating `IJsonlService` | Update the interface and any test doubles; the interface is the contract, not the concrete class |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| `LoadSettings()` (disk read) inside `_sessionsLock` in `GetContextWindow` | UI thread stalls on refresh; lock contention under rapid FSW events | Load setting once into a field; update via messenger | Any FSW burst (10+ file changes/second during active Claude session) |
| `Directory.Exists` on every entry in `RebuildSessionsList` | Slow rebuild with many orphaned network-path projects | Acceptable per spec — only called on rebuild, not every tick; guard against UNC to avoid SMB latency | Negligible at expected scale; only problematic if >100 orphaned UNC-path sessions |
| Calling `BuildSubagentContext` twice per `GetContextWindow` + `GetSubagentContext` | Double disk read for subagent files | Pre-existing; Phase 4 does not make it worse — the sort is O(n log n) on a tiny list | No new degradation from Phase 4 |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Passing `Cwd` from JSONL (external data) directly to `Directory.Exists` | SMB NTLM hash leak to crafted UNC path | `Path.IsPathRooted(cwd) && !cwd.StartsWith(@"\\")` guard before `Directory.Exists` |
| Storing `SonnetContextSize` as an unvalidated integer in settings JSON | Invalid value (e.g., `999`) causes nonsensical context display or divide-by-near-zero | On load, clamp/reject values not in `{200_000, 1_000_000}`; fall back to `DefaultContextLimit` |
| Sending `SonnetContextChangedMessage` with an unvalidated value from `SelectedSonnetContextIndex` | Out-of-bounds index maps to wrong context size | Use a fixed-size array `long[] { 200_000L, 1_000_000L }` indexed by `SelectedSonnetContextIndex`; bounds-check before access |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Context bar stays at 0% after changing Sonnet setting to 1M | User thinks the setting did not apply | `MainViewModel` subscribes to `SonnetContextChangedMessage` and calls `UpdateSessionData` immediately on receipt |
| Selected session vanishes silently after project deletion | User sees empty context bars with no explanation | After filtering, auto-select the most recent remaining session — do not leave the ComboBox blank without a placeholder |
| Subagent bars reorder on every FSW tick | Visual instability during active coding session | Phase 4 sort must be inside `BuildSubagentContext`, not at call sites |
| Tooltips frozen in one language after runtime language switch | German user sees "Refresh" after switching to DE | Never hard-code `ToolTipService.ToolTip` in XAML — resw-only approach lets WinUI3Localizer control the value |

---

## "Looks Done But Isn't" Checklist

- [ ] **Phase 1 — Opus context limit:** `GetMaxContextTokens("claude-opus-4-6")` returns `1_000_000`. The `ContextLimits` dictionary has an explicit 200K entry for this model — it must be updated or the dictionary must be removed.
- [ ] **Phase 1 — `GetEffectiveMaxTokens` callers:** Search for `GetEffectiveMaxTokens` after Phase 1 — zero two-argument calls should remain. Both `ContextWindowData.Utilization` and `SubagentContextData.Utilization` must use the new single-parameter signature.
- [ ] **Phase 1 — `ShouldWarnAutocompact` updated:** Confirm the new flat-buffer warning (`maxTokens − 20K`) is in place and the two-tier utilization percentage logic is removed.
- [ ] **Phase 1 — Deprecated constants deleted:** `ExtendedAutocompactBuffer` and `ExtendedContextDetectionThreshold` do not appear anywhere in the codebase after Phase 1.
- [ ] **Phase 2 — `de-DE` resw:** `SettingsSonnetContextLabel.Text`, `SettingsSonnetContext200K.Content`, `SettingsSonnetContext1M.Content` exist in both `de-DE` and `en-US` resource files.
- [ ] **Phase 2 — Settings persistence:** Open Settings, change to 1M, close app, reopen — the picker must still show 1M, not reset to 200K.
- [ ] **Phase 2 — `IJsonlService` interface updated:** If `JsonlService` gains a `UpdateSonnetContextSize` method, the `IJsonlService` interface must also declare it.
- [ ] **Phase 3 — Null/empty `Cwd` guard:** Sessions with `Cwd = null` or `Cwd = ""` are filtered out explicitly, not passed to `Directory.Exists` (which silently returns false for empty strings).
- [ ] **Phase 3 — Path validation guard present:** The `RebuildSessionsList` filter includes a `!cwd.StartsWith(@"\\")` UNC guard before calling `Directory.Exists`.
- [ ] **Phase 4 — Sort inside the method:** Search for `OrderBy.*AgentId` — exactly one occurrence, inside `BuildSubagentContext`. Zero occurrences at call sites.
- [ ] **Phase 5 — `de-DE` resw completeness:** Six entries required: `FooterRefreshButton`, `FooterSettingsButton`, `FooterQuitButton` × (`ToolTipService.ToolTip` + `AutomationProperties.Name`). All six exist in `de-DE/Resources.resw` (the `en-US` entries already exist).
- [ ] **Phase 5 — No XAML attribute override:** Grep `MainView.xaml` for `ToolTipService.ToolTip` — zero matches.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Opus sessions showing 200K after Phase 1 | LOW | Update or remove `ContextLimits` dictionary; rebuild and verify unit test |
| Sonnet setting not persisting (reverts to 200K on restart) | LOW | Debug `Initialize()` — confirm backing-field assignment pattern; verify `JsonPropertyName("sonnetContextSize")` on `AppSettings` property |
| Setting change not reflected live (requires restart) | LOW | Ensure `JsonlService` subscribes to `SonnetContextChangedMessage` or has an `UpdateSonnetContextSize` method; verify `MainViewModel` calls the update method on message receipt |
| Session filter removes selected session without fallback | LOW | Add guard in `MainViewModel.RefreshSessionList()` — check if `LastSelectedSessionId` still exists in the new filtered `_sessions` before selecting; fallback is already implemented for the "not found" case |
| Tooltip hard-coded in XAML breaks language switch | LOW | Remove `ToolTipService.ToolTip` XAML attribute; verify resw entry exists for both languages; rebuild |
| `GetEffectiveMaxTokens` called with wrong signature | LOW | Compiler error catches this immediately if deprecated constants are deleted in same commit as signature change |
| Path validation missing on `Directory.Exists` | MEDIUM | Add `Path.IsPathRooted` + UNC guard; this is a correctness fix even if no exploit has occurred in practice |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| `GetEffectiveMaxTokens` signature mismatch in records | Phase 1 | Zero two-argument calls to `GetEffectiveMaxTokens` in codebase |
| Opus still showing 200K (dictionary not updated) | Phase 1 | `GetMaxContextTokens("claude-opus-4-6")` returns 1,000,000 |
| Deprecated constants not deleted | Phase 1 | `ExtendedAutocompactBuffer` and `ExtendedContextDetectionThreshold` absent from codebase |
| `JsonlService` reads settings on every call under lock | Phase 1 | No `LoadSettings()` call inside `_sessionsLock` in `GetContextWindow` |
| `Initialize()` fires `OnSelectedSonnetContextIndexChanged` prematurely | Phase 2 | Sonnet 1M setting persists across app restarts; no spurious 200K override on startup |
| Sonnet setting change not reflected live | Phase 2 | Changing setting immediately updates context bar without app restart |
| Session filter removes selected session without fallback | Phase 3 | After deleting project directory, app auto-selects next session cleanly |
| `Directory.Exists` called on UNC/unvalidated path | Phase 3 | Path validation guard present in `RebuildSessionsList` |
| Sort applied at call site instead of inside method | Phase 4 | Single `OrderBy.*AgentId` in codebase, inside `BuildSubagentContext` |
| `ToolTipService.ToolTip` set as XAML attribute | Phase 5 | Grep `MainView.xaml` for `ToolTipService.ToolTip` — zero matches |
| Missing `de-DE` resw entries for Phase 5 | Phase 5 | Language switch to German: all three footer button tooltips show German text |

---

## Sources

- Direct codebase analysis: `ModelContextLimits.cs` (lines 1–157), `JsonlService.cs` (lines 666–779), `ContextWindowData.cs` (lines 1–54), `AppSettings.cs`, `SettingsViewModel.cs` (lines 85–100), `MainViewModel.cs`, `MainView.xaml` (lines 579–624), `SettingsView.xaml`, `en-US/Resources.resw` (lines 100–118), `App.xaml.cs` (lines 141–142)
- `spec-release-from-1.7.1-to-1.8.3.md` — implementation spec for all five phases including cross-cutting concerns
- `.planning/PROJECT.md` — current milestone context, known tech debt, key decisions log
- `MEMORY.md` — Cloudflare fix architecture and Phase 2 execution history

---
*Pitfalls research for: ccInfoWin v1.2 — macOS v1.8.3 feature parity additions*
*Researched: 2026-04-12*

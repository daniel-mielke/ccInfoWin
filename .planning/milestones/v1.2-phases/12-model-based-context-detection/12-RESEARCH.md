# Phase 12: Model-Based Context Detection - Research

**Researched:** 2026-04-12
**Domain:** C# static helper refactor — ModelContextLimits, ContextWindowData, JsonlService integration
**Confidence:** HIGH

## Summary

Phase 12 is a pure logic refactor of `ModelContextLimits.cs` and its consumers. The current implementation uses a token-count heuristic to detect extended (1M) context windows, which gives wrong results for Opus sessions with fewer than 180K tokens. The fix is to replace the heuristic entirely with model-family detection via `string.Contains("opus"/"sonnet"/"haiku")` — a pattern already proven correct in `GetBadgeColorHex`.

Three files require edits. `ModelContextLimits.cs` is the primary target: add a `ModelFamily` enum, a `GetModelFamily` method, update `GetMaxContextTokens` to return 1M for Opus, simplify `GetEffectiveMaxTokens` to a flat 33K buffer (removing the `currentTokens` parameter), and replace percentage-based `ShouldWarnAutocompact` with a flat 20K-remaining threshold. `ContextWindowData.cs` propagates the simplified `GetEffectiveMaxTokens(long maxTokens)` signature in two `Utilization` computed properties. `JsonlService.cs` propagates the new `GetMaxContextTokens(string? modelName, long sonnetContextSize = 200_000)` parameter at two call sites.

All changes are self-contained within these three files. No DI changes, no XAML changes, no ViewModel logic changes are needed for Phase 12. The ViewModel already reads `context.Utilization` and `context.ShouldWarnAutocompact` from the model — those bindings continue to work unchanged as long as the underlying model produces correct values.

**Primary recommendation:** Edit `ModelContextLimits.cs` first (enum + method + constant cleanup), then propagate the signature changes to `ContextWindowData.cs` and `JsonlService.cs`. Update `ModelContextLimitsTests.cs` and `ContextWindowTests.cs` to assert new behavior.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- Add `ModelFamily` enum nested inside `ModelContextLimits` (Opus, Sonnet, Haiku, Unknown) — tightly coupled, only used by this helper
- Add `GetModelFamily(string? modelName)` method — parse from model name string (contains "opus" / "sonnet" / "haiku")
- Replace `ContextLimits` dictionary with `ModelFamily`-based switch — the dictionary has wrong values (200K for all), a switch on family is simpler and extensible
- Add `sonnetContextSize` parameter to `GetMaxContextTokens(string? modelName, long sonnetContextSize = 200_000)` — Phase 13 will pass the setting value; Phase 12 uses default
- Simplify `GetEffectiveMaxTokens` to `GetEffectiveMaxTokens(long maxTokens)` — remove `currentTokens` param since buffer is now flat 33K
- Replace percentage-based thresholds (90%/95%) with flat buffer: `totalTokens >= maxTokens - 20_000`
- Add constant `AutocompactWarningBuffer = 20_000`
- Delete `LargeModelAutocompactThreshold`, `SmallModelAutocompactThreshold` — no longer needed
- Delete `ExtendedAutocompactBuffer` (165K) and `ExtendedContextDetectionThreshold` (180K) — the heuristic is the problem being fixed
- Add constant `ExtendedContextLimit = 1_000_000`
- Subagents resolve model family from `lastEntry.Message.Model` in JSONL — existing pattern in `BuildSubagentContext()`
- Update `SubagentContextData.Utilization` to use simplified `GetEffectiveMaxTokens(maxTokens)`
- Null model name falls back to 200K (DefaultContextLimit) — existing behavior, safe default

### Claude's Discretion

- Internal code structure within `ModelContextLimits` (method ordering, doc comments)
- Whether to inline `GetModelFamily()` or keep as separate method

### Deferred Ideas (OUT OF SCOPE)

- Sonnet context configuration UI (Phase 13)
- ISettingsService injection into JsonlService for reading Sonnet setting (Phase 13)
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CTX-01 | User sees 1M context limit for Opus sessions (effective: ~967K after 33K buffer) | `GetModelFamily` returns `Opus` → `GetMaxContextTokens` returns 1_000_000 → effective = 1_000_000 − 33_000 = 967_000 |
| CTX-02 | User sees 200K context limit for Haiku sessions (effective: ~167K) | `GetModelFamily` returns `Haiku` → `GetMaxContextTokens` returns 200_000 → effective = 200_000 − 33_000 = 167_000 |
| CTX-03 | User sees context limit based on configured Sonnet setting (200K or 1M) | `GetMaxContextTokens(modelName, sonnetContextSize)` parameter; Phase 12 passes default 200_000; Phase 13 passes setting value |
| CTX-04 | User receives autocompact warning at 20K tokens remaining, regardless of model | `ShouldWarnAutocompact`: `totalTokens >= maxTokens - AutocompactWarningBuffer` (20_000) |
| CTX-05 | User sees correct progress bar percentage reflecting model-based effective max | `ContextWindowData.Utilization` divides by `GetEffectiveMaxTokens(MaxTokens)` = `MaxTokens - 33_000`; ViewModel binds `context.Utilization` |
| CTX-06 | User sees correct context limits on subagent progress bars (model-based detection) | `BuildSubagentContext()` already reads `lastEntry.Message?.Model`; passes to `GetMaxContextTokens`; `SubagentContextData.Utilization` uses same simplified `GetEffectiveMaxTokens` |
</phase_requirements>

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| C# 13 / .NET 9 | 9.0 | Language + runtime | Project stack (CLAUDE.md) |
| CommunityToolkit.Mvvm | 8.4 | Source generators for MVVM | Project stack (CLAUDE.md) |
| xUnit | 2.9.3 | Unit tests | Already in CCInfoWindows.Tests.csproj |
| Moq | 4.20.72 | Mocking | Already in CCInfoWindows.Tests.csproj |

No new libraries required. This phase is a refactor of a single static helper class.

**Installation:** None required.

---

## Architecture Patterns

### Existing Static Helper Pattern
`ModelContextLimits` is a `public static class` with pure, stateless methods. No DI, no interfaces, no instances. This pattern continues unchanged.

### ModelFamily Switch Pattern
Replace the `Dictionary<string, long> ContextLimits` with a switch expression on `ModelFamily`:

```csharp
// Source: 12-CONTEXT.md — locked decision
public static long GetMaxContextTokens(string? modelName, long sonnetContextSize = DefaultContextLimit)
{
    return GetModelFamily(modelName) switch
    {
        ModelFamily.Opus => ExtendedContextLimit,
        ModelFamily.Sonnet => sonnetContextSize,
        _ => DefaultContextLimit
    };
}
```

### Flat Buffer Pattern (replaces heuristic)
`GetEffectiveMaxTokens` removes the `currentTokens` parameter entirely:

```csharp
// Before: GetEffectiveMaxTokens(long currentTokens, long maxTokens)
// After:  GetEffectiveMaxTokens(long maxTokens)
public static long GetEffectiveMaxTokens(long maxTokens)
    => Math.Max(1, maxTokens - StandardAutocompactBuffer);
```

### Flat Warning Pattern
`ShouldWarnAutocompact` replaces ratio-based thresholds with absolute remaining tokens:

```csharp
// Before: utilization >= 0.90 (large) or 0.95 (small)
// After: totalTokens >= maxTokens - AutocompactWarningBuffer
public static bool ShouldWarnAutocompact(long totalTokens, long maxTokens)
{
    if (maxTokens <= 0)
        return false;
    return totalTokens >= maxTokens - AutocompactWarningBuffer;
}
```

### Callers: Signature Change Propagation

**`ContextWindowData.cs`** — Both `Utilization` computed properties call `GetEffectiveMaxTokens`. Signature shrinks from two args to one:

```csharp
// ContextWindowData.Utilization (before)
var effective = ModelContextLimits.GetEffectiveMaxTokens(TotalTokens, MaxTokens);
// After
var effective = ModelContextLimits.GetEffectiveMaxTokens(MaxTokens);
```

Same change applies identically to `SubagentContextData.Utilization`.

**`JsonlService.cs`** — Two call sites call `GetMaxContextTokens`. Both gain the `sonnetContextSize` parameter with the default value, so no calling code needs to change for Phase 12:

```csharp
// GetContextWindow() at line ~151 — no change needed (uses default)
var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName);
// BuildSubagentContext() at line ~693 — no change needed (uses default)
var maxTokens = ModelContextLimits.GetMaxContextTokens(modelName);
```

The `default` parameter on `GetMaxContextTokens` means both existing call sites compile without modification. Phase 13 will update these to pass a real setting value.

### Anti-Patterns to Avoid
- **Keeping the dictionary:** The dictionary has all values hardcoded to 200K (including Opus entries). The switch on `ModelFamily` is both simpler and correct. Delete the dictionary entirely.
- **Re-introducing currentTokens in GetEffectiveMaxTokens:** The buffer is now flat 33K regardless of context size. The `currentTokens` parameter exists only to support the now-deleted heuristic.
- **Percentage-based ShouldWarnAutocompact:** The two-tier (90%/95%) threshold must be replaced; the new spec requires absolute token count (`maxTokens - 20_000`).
- **Leaving LargeModelThresholdTokens:** This constant (`100_000`) was only used by the deleted threshold logic. Delete it along with `LargeModelAutocompactThreshold` and `SmallModelAutocompactThreshold`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Model family detection | Custom regex/parser | `string.Contains("opus")` case-insensitive | `GetBadgeColorHex` already uses this pattern with correct results; no new logic needed |
| Enum nested scope | Standalone file | Nested in `ModelContextLimits` | Locked decision; tightly coupled, no external consumers |

---

## Common Pitfalls

### Pitfall 1: Callers with Two-Arg GetEffectiveMaxTokens
**What goes wrong:** `ContextWindowData.cs` calls `GetEffectiveMaxTokens(TotalTokens, MaxTokens)` — if only the helper is updated but not the callers, code fails to compile.
**Why it happens:** Two call sites exist in `ContextWindowData.cs` (one in `ContextWindowData.Utilization`, one in `SubagentContextData.Utilization`). Both must be updated in the same commit.
**How to avoid:** Search for all call sites before committing: `GetEffectiveMaxTokens` appears only in `ContextWindowData.cs` (2 occurrences).
**Warning signs:** Compiler errors on `GetEffectiveMaxTokens` after the helper change.

### Pitfall 2: Stale Tests Assert Old Behavior
**What goes wrong:** `ModelContextLimitsTests.cs` and `ContextWindowTests.cs` assert the old percentage thresholds (90%, 95%) and old 200K values for Opus. These tests will fail after the refactor.
**Why it happens:** Tests encode the OLD behavior as expected — they are the specification to update.
**How to avoid:** Update tests in the same wave as the production code change.
**Warning signs:** `ShouldWarnAutocompact_LargeModel_UsesNinetyPercentThreshold` fails after the fix.

Specific tests that will break and must be updated:
- `ModelContextLimitsTests.GetMaxContextTokens_KnownModel_ReturnsCorrectLimit` — asserts 200K for `claude-opus-4-6`; must become 1_000_000.
- `ModelContextLimitsTests.ShouldWarnAutocompact_LargeModel_UsesNinetyPercentThreshold` — asserts 90% threshold; must use 20K buffer logic.
- `ContextWindowTests.ModelContextLimits_ShouldWarnAutocompact_LargeModel_NinetyPercentThreshold` — same threshold change.
- `ContextWindowTests.ModelContextLimits_ShouldWarnAutocompact_SmallModel_NinetyFivePercentThreshold` — deleted concept; test must be removed or replaced.
- `ContextWindowTests.ContextWindowData_Utilization_ComputesTotalOverEffectiveMax` — the `[InlineData(200_000, 200_000, 1.0)]` case used to hit `200K - 33K = 167K` as effective max; now `totalTokens/effectiveMax = 200_000/167_000` which is >1 and clamps to 1.0. Check whether the expected value is still 1.0 or needs adjustment.

### Pitfall 3: BuildSubagentContext model is null
**What goes wrong:** `lastEntry.Message?.Model` can be null for subagents using older or synthetic model strings. `GetModelFamily(null)` must return `Unknown`, triggering the `DefaultContextLimit = 200_000` fallback.
**Why it happens:** Locked decision states: "Null model name falls back to 200K (DefaultContextLimit) — existing behavior, safe default."
**How to avoid:** `GetModelFamily` must handle null/empty string and return `Unknown`. The `switch` default case returns `DefaultContextLimit`.
**Warning signs:** NullReferenceException in subagent path, or incorrect 0-context display for subagents.

### Pitfall 4: Pre-existing test failures unrelated to Phase 12
**What goes wrong:** 4 tests in `CountdownFormatterTests` are already failing before Phase 12 begins. Running the full suite appears to show Phase 12 regressions when they are pre-existing.
**Why it happens:** Pre-existing failures documented in STATE.md as pending todos.
**How to avoid:** Run only the targeted test classes: `--filter "FullyQualifiedName~ModelContextLimits|FullyQualifiedName~ContextWindow"`. The 44 relevant tests currently pass. Phase 12 verification gates on these 44 (updated) tests, not the full suite.
**Warning signs:** Seeing 4 failures and assuming Phase 12 caused them.

---

## Code Examples

### GetModelFamily (new method)
```csharp
// Pattern reused from existing GetBadgeColorHex
public static ModelFamily GetModelFamily(string? modelName)
{
    if (string.IsNullOrEmpty(modelName))
        return ModelFamily.Unknown;

    var lower = modelName.ToLowerInvariant();

    if (lower.Contains("opus"))
        return ModelFamily.Opus;
    if (lower.Contains("sonnet"))
        return ModelFamily.Sonnet;
    if (lower.Contains("haiku"))
        return ModelFamily.Haiku;

    return ModelFamily.Unknown;
}
```

### ModelFamily enum (nested)
```csharp
public enum ModelFamily
{
    Unknown,
    Opus,
    Sonnet,
    Haiku
}
```

### New constants block
```csharp
public const long DefaultContextLimit = 200_000;
public const long ExtendedContextLimit = 1_000_000;
public const long StandardAutocompactBuffer = 33_000;
public const long AutocompactWarningBuffer = 20_000;
// Deleted: ExtendedAutocompactBuffer, ExtendedContextDetectionThreshold
// Deleted: LargeModelAutocompactThreshold, SmallModelAutocompactThreshold, LargeModelThresholdTokens
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Token-count heuristic (`currentTokens > 180K`) for extended context detection | Model-family detection via name contains check | Phase 12 (macOS v1.8.1 origin) | Opus always gets 1M regardless of current token count |
| Two-tier autocompact threshold (90%/95% by model size) | Flat `maxTokens - 20K` remaining | Phase 12 (macOS v1.8.1 origin) | Warning fires at the same absolute margin for all models |
| Variable buffer: 33K (standard) / 165K (extended) | Flat 33K buffer always | Phase 12 (macOS v1.8.1 origin) | Progress bar accurately reflects remaining space |
| Dictionary with all-200K values | Switch on `ModelFamily` enum | Phase 12 | Extensible, correct, no stale dictionary entries |

**Deprecated/outdated after this phase:**
- `ExtendedAutocompactBuffer = 165_000` — was only needed for heuristic; delete
- `ExtendedContextDetectionThreshold = 180_000` — was the heuristic input; delete
- `LargeModelAutocompactThreshold`, `SmallModelAutocompactThreshold`, `LargeModelThresholdTokens` — percentage-based; delete all three
- `Dictionary<string, long> ContextLimits` — all 200K values are wrong for Opus; delete entire dictionary

---

## Open Questions

1. **CTX-03 is listed as a Phase 12 requirement but Sonnet configurability is Phase 13**
   - What we know: REQUIREMENTS.md maps CTX-03 to Phase 12. The `sonnetContextSize` parameter defaults to 200K in Phase 12.
   - What's unclear: Is CTX-03 considered "satisfied" by Phase 12 because the parameter exists (wired in), even though the UI to change it is Phase 13?
   - Recommendation: Mark CTX-03 as partially satisfied by Phase 12 (the parameter hook exists, default behavior is correct). Full satisfaction requires Phase 13 UI. The planner should note this.

---

## Environment Availability

Step 2.6: No external dependencies beyond the project's own C# code. All changes are pure logic refactors of static methods. No new tools, CLIs, services, or runtimes required.

**Skip condition applied:** Phase is purely code/config changes with no external dependencies.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-build --filter "FullyQualifiedName~ModelContextLimits\|FullyQualifiedName~ContextWindow"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-build` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CTX-01 | Opus model returns 1_000_000 from GetMaxContextTokens | unit | `dotnet test ... --filter "FullyQualifiedName~ModelContextLimits"` | ✅ (update existing) |
| CTX-01 | Opus effective max = 967_000 (1M - 33K) | unit | `dotnet test ... --filter "FullyQualifiedName~ContextWindow"` | ✅ (add new case) |
| CTX-02 | Haiku model returns 200_000 from GetMaxContextTokens | unit | `dotnet test ... --filter "FullyQualifiedName~ModelContextLimits"` | ✅ (update existing) |
| CTX-02 | Haiku effective max = 167_000 (200K - 33K) | unit | `dotnet test ... --filter "FullyQualifiedName~ContextWindow"` | ✅ (add new case) |
| CTX-03 | Sonnet with default 200K returns 200_000 | unit | `dotnet test ... --filter "FullyQualifiedName~ModelContextLimits"` | ✅ (update existing) |
| CTX-03 | Sonnet with explicit 1M param returns 1_000_000 | unit | `dotnet test ... --filter "FullyQualifiedName~ModelContextLimits"` | ❌ Wave 0 |
| CTX-04 | ShouldWarnAutocompact fires at maxTokens - 20K | unit | `dotnet test ... --filter "FullyQualifiedName~ModelContextLimits\|FullyQualifiedName~ContextWindow"` | ✅ (replace existing) |
| CTX-04 | ShouldWarnAutocompact does NOT fire at maxTokens - 20001 | unit | same | ❌ Wave 0 (boundary test) |
| CTX-05 | Utilization = totalTokens / (maxTokens - 33K) for 200K session | unit | `dotnet test ... --filter "FullyQualifiedName~ContextWindow"` | ✅ (update existing) |
| CTX-05 | Utilization = totalTokens / (maxTokens - 33K) for 1M session | unit | `dotnet test ... --filter "FullyQualifiedName~ContextWindow"` | ❌ Wave 0 |
| CTX-06 | SubagentContextData.Utilization uses flat 33K buffer | unit | `dotnet test ... --filter "FullyQualifiedName~ContextWindow"` | ❌ Wave 0 |
| CTX-06 | BuildSubagentContext returns correct MaxTokens for Opus subagent | unit | `dotnet test ... --filter "FullyQualifiedName~JsonlService"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-build --filter "FullyQualifiedName~ModelContextLimits|FullyQualifiedName~ContextWindow"`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-build`
- **Phase gate:** Targeted 44+ tests green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Helpers/ModelContextLimitsTests.cs` — update: Opus 1M assertion, ShouldWarnAutocompact flat-buffer cases, Sonnet with explicit 1M param test (CTX-01, CTX-02, CTX-03, CTX-04)
- [ ] `CCInfoWindows.Tests/Helpers/ContextWindowTests.cs` — update: threshold tests → flat buffer; add Opus 1M effective max case, Haiku 167K case, 1M session utilization case (CTX-01, CTX-02, CTX-04, CTX-05)
- [ ] `CCInfoWindows.Tests/Helpers/ContextWindowTests.cs` — add: `SubagentContextData.Utilization` flat buffer test (CTX-06)
- [ ] `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` — add: Opus subagent returns MaxTokens=1_000_000 (CTX-06)

*(Existing test infrastructure covers the framework — only test data and assertions need updating/adding.)*

---

## Sources

### Primary (HIGH confidence)
- Direct source code read: `CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs` — current implementation verified line-by-line
- Direct source code read: `CCInfoWindows/CCInfoWindows/Models/ContextWindowData.cs` — current callers of `GetEffectiveMaxTokens`
- Direct source code read: `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs` — lines 135–188, 666–716, 734–745 (all call sites)
- Direct source code read: `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — lines 747–768 (no changes needed here)
- Direct source code read: `spec-release-from-1.7.1-to-1.8.3.md` lines 46–151 — authoritative spec for Phase 1 (= Phase 12)
- `12-CONTEXT.md` — all locked decisions are the authoritative implementation specification
- `CCInfoWindows.Tests/Helpers/ModelContextLimitsTests.cs` — 9 existing tests, all passing; will need updates
- `CCInfoWindows.Tests/Helpers/ContextWindowTests.cs` — 35 existing tests, all passing; will need updates
- Test run confirmed: 44 relevant tests pass with current code; 4 CountdownFormatter failures are pre-existing and unrelated

### Secondary (MEDIUM confidence)
- None required — all facts derived from direct source code reads

### Tertiary (LOW confidence)
- None

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — verified from csproj files and source code
- Architecture patterns: HIGH — derived from existing code patterns (GetBadgeColorHex, locked decisions in CONTEXT.md)
- Pitfalls: HIGH — derived from reading actual test files and verifying which tests will break

**Research date:** 2026-04-12
**Valid until:** Indefinite — pure internal refactor, no external dependencies, no ecosystem churn risk

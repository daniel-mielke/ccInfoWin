# Phase 07: Security Fix & Dead Code Cleanup - Research

**Researched:** 2026-03-17
**Domain:** C# / WinUI 3 — event handler cleanup, interface extension, dead code removal
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None specified — all implementation choices are at Claude's discretion.

### Claude's Discretion
All implementation choices are at Claude's discretion — pure infrastructure phase.

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| AUTH-04 | User can log out, clearing all stored tokens | Logout flow in MainViewModel.Logout() already clears credentials and navigates to LoginView — missing WebViewBridge.Reset() call is the gap |
| SECU-03 | No telemetry, no tracking, no data collection | Dead code removal is hygienic housekeeping — no external data flows introduced |
</phase_requirements>

---

## Summary

This is a pure hygiene phase with four surgical changes. Three are dead code deletions (no logic written, only files and fields removed). One is a security-correctness fix: calling `WebViewBridge.Reset()` on logout to release the `CoreWebView2` reference and unhook the `WebMessageReceived` event handler.

The `WebViewBridge.Reset()` method already exists and is correctly implemented. The gap is that `MainViewModel.Logout()` does not call it. The fix requires injecting `IWebViewBridge` into `MainViewModel` and calling `Reset()` inside `Logout()`. The interface also lacks a `Reset()` declaration, so that must be added first.

The dead code items are unambiguously orphaned: `CostCalculator.cs` is never called (its logic lives in `JsonlService.AggregateEntryLog`), `JsonlDataUpdatedMessage` and `SessionSelectedMessage` appear in exactly one file each (their own definition file — zero senders/receivers), and `_inputTokensText`/`_outputTokensText` are declared in `MainViewModel` but never bound in XAML.

**Primary recommendation:** Add `Reset()` to `IWebViewBridge`, inject `IWebViewBridge` into `MainViewModel`, call `Reset()` in `Logout()`, then delete four dead code artifacts.

---

## Standard Stack

No new libraries needed. All work uses existing project stack.

| Component | Current State | Change |
|-----------|--------------|--------|
| `IWebViewBridge` | Missing `Reset()` declaration | Add `Reset()` method signature |
| `WebViewBridge` | `Reset()` already implemented correctly | No change to implementation |
| `MainViewModel` | Injects `ICredentialService`, `INavigationService`, `IClaudeApiService`, etc. | Add `IWebViewBridge` parameter |
| `App.xaml.cs` DI | Registers `WebViewBridge` as both concrete and `IWebViewBridge` singleton | No change |

---

## Architecture Patterns

### Existing Logout Flow (current)

```
MainViewModel.Logout()
  → _historyService.ClearHistory()
  → _credentialService.ClearCredentials()
  → WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false))
  → IsSessionExpired = false
  → _navigationService.NavigateTo<LoginView>()
  [MISSING: _bridge.Reset()]
```

### Fixed Logout Flow (target)

```
MainViewModel.Logout()
  → _historyService.ClearHistory()
  → _credentialService.ClearCredentials()
  → _bridge.Reset()                          ← new
  → WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false))
  → IsSessionExpired = false
  → _navigationService.NavigateTo<LoginView>()
```

### Reset() placement consideration

`Reset()` should be called **before** `AuthStateChangedMessage` is sent. The message triggers navigation away from MainView — if any in-flight fetch is racing on another thread, it could still call `FetchJsonAsync` after navigation starts. Calling `Reset()` first makes `FetchJsonAsync` return `null` immediately (guarded by `if (_coreWebView is null)`) rather than posting to a potentially-gone UI context.

### `_pending` TCS cleanup on Reset()

The existing `Reset()` implementation does NOT cancel pending `TaskCompletionSource` entries in `_pending`. On logout, any in-flight fetch will hang for 30 seconds (the `CancellationTokenSource` timeout). This is acceptable for a hygiene phase — no data loss, no crash. If desired, `Reset()` could iterate `_pending` and call `TrySetResult(null)` on each. This is a Claude's-discretion improvement — document the decision.

**Recommendation:** Extend `Reset()` to also drain `_pending` (cancel all in-flight requests immediately). This is a 4-line addition and prevents a 30-second ghost hang on logout. It stays within phase scope since it's part of the same `Reset()` correctness story.

### Pattern: Adding method to existing interface

```csharp
// IWebViewBridge.cs — add Reset() declaration
/// <summary>
/// Releases the WebView2 reference and unregisters the WebMessageReceived handler.
/// Call on logout to prevent stale event callbacks on a reused CoreWebView2.
/// </summary>
void Reset();
```

No other implementors of `IWebViewBridge` exist in the codebase — grep confirms only `WebViewBridge.cs` implements it. No mock or stub classes to update.

### Pattern: Injecting IWebViewBridge into MainViewModel

`LoginViewModel` injects `WebViewBridge` (the concrete type) directly, not via `IWebViewBridge`. This is a pre-existing inconsistency. For `MainViewModel`, use `IWebViewBridge` (the interface) — it only needs `Reset()`, which is now on the interface. This is the correct DI pattern per project conventions.

`LoginViewModel` needs `Initialize()` which is NOT on `IWebViewBridge`. That's why it takes the concrete type. No change needed there.

### DI registration — already correct

```csharp
// App.xaml.cs — existing registration (no change)
services.AddSingleton<WebViewBridge>();
services.AddSingleton<IWebViewBridge>(sp => sp.GetRequiredService<WebViewBridge>());
```

Both registrations resolve to the same singleton instance. `MainViewModel` receiving `IWebViewBridge` and `LoginViewModel` receiving `WebViewBridge` will share the same object — correct behavior.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Event handler memory leak | Custom weak reference wrapper | Standard `-=` unsubscribe in `Reset()` — already done |
| Pending TCS cleanup | Separate cleanup service | Iterate `_pending` ConcurrentDictionary directly in `Reset()` |

---

## Common Pitfalls

### Pitfall 1: Forgetting the `_pending` drain in Reset()
**What goes wrong:** After logout, any in-flight `FetchJsonAsync` call hangs for 30 seconds waiting for a `WebMessageReceived` event that will never fire (bridge is reset, CoreWebView2 unhooked).
**Why it happens:** `Reset()` was written before the pending-requests pattern was fully considered.
**How to avoid:** After unsubscribing the event handler, iterate `_pending` and `TrySetResult(null)` on each remaining TCS.
**Warning signs:** Logout feels sluggish or polling timer fires after logout.

### Pitfall 2: Removing `_inputTokensText` / `_outputTokensText` causes silent XAML compile crash
**What goes wrong:** WinUI 3 XAML compiler can crash silently or emit misleading errors if `x:Bind` references a property that no longer exists — even if the `[ObservableProperty]` generated property is the one bound.
**Why it happens:** The project STATE.md documents this explicitly: "Backward compat: `_inputTokensText` kept in MainViewModel — XAML compiler crashes silently if x:Bind references missing properties."
**How to avoid:** Before removing, search all XAML files for `InputTokensText` and `OutputTokensText` to confirm zero bindings exist.
**Verification:** `grep -r "InputTokensText\|OutputTokensText" CCInfoWindows/CCInfoWindows/Views/` must return zero results before deletion.

Confirmed: current grep on entire codebase finds `_inputTokensText` and `_outputTokensText` ONLY in MainViewModel.cs lines 199 and 202 — no XAML references. The STATE.md warning was for an intermediate state during Phase 05-02; it was resolved. Safe to remove.

### Pitfall 3: Removing Messages breaks unregistered but-still-compiled subscribers
**What goes wrong:** If any code registers to receive `JsonlDataUpdatedMessage` or `SessionSelectedMessage` via `WeakReferenceMessenger`, removing the message class causes a compile error that may not surface until the full build.
**Why it happens:** Messenger registrations use the message type as a generic parameter.
**How to avoid:** Confirm zero usages before deleting. Current grep confirms both message classes appear ONLY in their own definition files — zero senders, zero receivers.

### Pitfall 4: Removing CostCalculatorTests.cs also needed
**What goes wrong:** Leaving `CostCalculatorTests.cs` in the test project while `CostCalculator.cs` is deleted causes a build error (`CCInfoWindows.Helpers.CostCalculator` not found).
**Why it happens:** Tests reference the type directly.
**How to avoid:** Delete both `CostCalculator.cs` AND `CostCalculatorTests.cs` in the same commit.

---

## Code Examples

### Interface extension

```csharp
// Source: IWebViewBridge.cs — add alongside existing FetchJsonAsync signature
void Reset();
```

### Reset() with pending drain (enhancement)

```csharp
// Source: WebViewBridge.cs — replace existing Reset() body
public void Reset()
{
    if (_coreWebView is not null)
    {
        _coreWebView.WebMessageReceived -= OnWebMessageReceived;
    }
    _coreWebView = null;
    _dispatcherQueue = null;

    foreach (var key in _pending.Keys)
    {
        if (_pending.TryRemove(key, out var tcs))
        {
            tcs.TrySetResult(null);
        }
    }
}
```

### MainViewModel constructor injection

```csharp
// Add IWebViewBridge to constructor parameter list
private readonly IWebViewBridge _bridge;

public MainViewModel(
    ICredentialService credentialService,
    INavigationService navigationService,
    IClaudeApiService apiService,
    IUsageHistoryService historyService,
    IJsonlService jsonlService,
    IPricingService pricingService,
    IUpdateService updateService,
    IWebViewBridge bridge)           // ← add
{
    // ...existing assignments...
    _bridge = bridge;
}
```

### Logout method fix

```csharp
[RelayCommand]
private void Logout()
{
    _historyService.ClearHistory();
    _credentialService.ClearCredentials();
    _bridge.Reset();                  // ← add before auth message
    WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
    IsSessionExpired = false;
    _navigationService.NavigateTo<LoginView>();
}
```

---

## Dead Code Inventory

| Artifact | File | Safe to Delete? | Reason |
|----------|------|-----------------|--------|
| `CostCalculator.cs` | `Helpers/CostCalculator.cs` | YES | Zero call sites in production code; logic duplicated in `JsonlService.AggregateEntryLog` |
| `CostCalculatorTests.cs` | `CCInfoWindows.Tests/Helpers/CostCalculatorTests.cs` | YES | Tests for deleted class; keeping causes build error |
| `JsonlDataUpdatedMessage.cs` | `Messages/JsonlDataUpdatedMessage.cs` | YES | Zero senders and zero receivers confirmed |
| `SessionSelectedMessage.cs` | `Messages/SessionSelectedMessage.cs` | YES | Zero senders and zero receivers confirmed |
| `_inputTokensText` field + generated property | `ViewModels/MainViewModel.cs` lines 198-202 | YES | Zero XAML bindings confirmed |
| `_outputTokensText` field + generated property | `ViewModels/MainViewModel.cs` lines 201-202 | YES | Zero XAML bindings confirmed |

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | Notes |
|--------|----------|-----------|-------------------|-------|
| AUTH-04 | Logout clears credentials and calls bridge Reset | manual-only | N/A | `MainViewModel` requires WinUI runtime — not unit-testable in isolation |
| SECU-03 | No telemetry introduced by changes | manual-only | N/A | Code review: verify no new network calls added |

### Verification approach for dead code removal

The primary verification is **build success**:
```
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj
```

A clean build after deletions proves no remaining references to removed types. Test suite green after `CostCalculatorTests.cs` deletion confirms no test infrastructure regression.

### Wave 0 Gaps

None — existing test infrastructure covers build verification. No new test files required for this phase. The dead code items have no behavior to test beyond "they are gone." The `Reset()` call correctness is verified by code review (confirmed `_pending` drain logic) and by manual logout testing.

---

## Open Questions

1. **Should `Reset()` also cancel the 30-second CancellationTokenSource in FetchJsonAsync?**
   - What we know: Each `FetchJsonAsync` call creates its own `CancellationTokenSource` scoped to that call — it cannot be cancelled externally from `Reset()`.
   - What's unclear: Whether any caller awaits `FetchJsonAsync` during logout and would therefore block navigation.
   - Recommendation: The existing `CancellationTokenSource` timeout (30s) is self-contained per call. Draining `_pending` via `TrySetResult(null)` in `Reset()` is sufficient — it makes each awaiting task complete immediately regardless of the token.

2. **Should `LoginViewModel` be updated to inject `IWebViewBridge` instead of `WebViewBridge`?**
   - What we know: `LoginViewModel` calls `_bridge.Initialize(...)` which is NOT on `IWebViewBridge`. Changing it requires adding `Initialize()` to the interface or using the concrete type.
   - Recommendation: Out of scope for this phase. The inconsistency is pre-existing and not part of the phase requirements. Keep `LoginViewModel` using the concrete type.

---

## Sources

### Primary (HIGH confidence)
- Direct code inspection: `WebViewBridge.cs`, `IWebViewBridge.cs`, `LoginViewModel.cs`, `MainViewModel.cs`, `App.xaml.cs`
- Direct code inspection: `CostCalculator.cs`, `CostCalculatorTests.cs`, `JsonlDataUpdatedMessage.cs`, `SessionSelectedMessage.cs`
- Grep verification: Zero call sites for all dead code artifacts confirmed in production codebase
- STATE.md historical decision log: XAML backward-compat note from Phase 05-02 cross-checked against current XAML grep (no bindings found)

### Secondary (MEDIUM confidence)
- Project CLAUDE.md: MVVM conventions, DI patterns, async/UI thread rules followed throughout

---

## Metadata

**Confidence breakdown:**
- Security fix (WebViewBridge.Reset on logout): HIGH — code paths fully traced, fix is mechanical
- Dead code removal: HIGH — zero-reference status confirmed by grep on all artifacts
- Pitfalls: HIGH — grounded in actual code state, not speculation

**Research date:** 2026-03-17
**Valid until:** Stable — pure refactor/cleanup, no external dependencies

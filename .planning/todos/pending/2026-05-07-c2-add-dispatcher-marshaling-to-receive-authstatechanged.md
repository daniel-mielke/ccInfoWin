---
created: 2026-05-07
source: v1.4 code review
severity: critical
area: ViewModels/MainViewModel.cs
related_phase: 20-auth-flow-stability
---

# C-2: `Receive(AuthStateChangedMessage)` mutates UI state without thread marshaling

## Problem

`MainViewModel.cs:997-1026` mutates `[ObservableProperty]` fields (`IsSessionExpired`, `StatusMessage`) and calls `_navigationService.NavigateTo<LoginView>()` — all of which fire on the calling thread. `WeakReferenceMessenger.Send` invokes recipients synchronously on the sender's thread.

`ClaudeApiService.FetchUsageAsync` (line 88) and `TryMigrateOrgIdAsync` (line 184) both send `AuthStateChangedMessage(false)` from HTTP error handlers — these run on a `ThreadPool` thread, not the UI thread. Therefore `Receive(AuthStateChangedMessage)` runs off-thread, and the UI mutations + navigation calls happen off-thread.

Other receivers in the same class get this right:
- `OnUpdateAvailable` (line 981-986) wraps in `dispatcherQueue?.TryEnqueue(...)`
- `Receive(SessionTimeoutChangedMessage)` (line 1032) wraps in `_dispatcherQueue?.TryEnqueue(RefreshSessionList)`

`Receive(AuthStateChangedMessage)` does not.

## Why This Matters

WinUI 3 `[ObservableProperty]` setters fire `PropertyChanged`. If a binding evaluates that change off the UI thread, behavior ranges from silent corruption to `RPC_E_WRONG_THREAD` crash. `Frame.Navigate` is documented as UI-thread-only. The bug is intermittent: it depends on whether the `ThreadPool` thread happens to be the UI thread (rare) and whether any active binding requires UI-thread access (common).

UAT did not catch this because manual logout testing happens via the Settings UI button (UI-thread by definition). The 401-triggered path is the off-thread one, and AUTH-01/02 visual smoke was deferred — exactly the path with the bug.

## Fix

Wrap the entire body in `_dispatcherQueue?.TryEnqueue(() => { ... })`, identical to the pattern at line 1032:

```csharp
public void Receive(AuthStateChangedMessage message)
{
    _dispatcherQueue?.TryEnqueue(() =>
    {
        if (message.IsAuthenticated)
        {
            _autoReauthAttempted = false;
            IsSessionExpired = false;
            // ... existing logic ...
            _ = PollUsageAsync();  // also fixes C-1
        }
        else
        {
            // ... existing logic ...
            _navigationService.NavigateTo<LoginView>();
        }
    });
}
```

## Test To Add

Add a test that:
1. Constructs `MainViewModel` with a real `DispatcherQueue` from a TestApplication or `DispatcherQueueController`
2. Triggers `Send(AuthStateChangedMessage(false))` from `Task.Run` (off the UI thread)
3. Asserts no thread-affinity exception is thrown and `IsSessionExpired == true` propagates

If headless `DispatcherQueueController` is impractical, at minimum verify the `TryEnqueue` call exists via a mockable `IDispatcherQueue` adapter (similar to `IDispatcherTimer` from Phase 22).

## Effort

S — wrap body, write off-thread test (the test infrastructure may need an `IDispatcherQueue` adapter, mirror of `IDispatcherTimer`).

## v1.5 Priority

Critical. This is the same family of bug as the WeakReferenceMessenger+AddTransient pitfall: messenger pattern + naive thread assumption = production failure mode that survives UAT. The architectural memory `architecture_weakreferencemessenger_with_transient_vms.md` should be updated to also flag thread-marshaling requirements.

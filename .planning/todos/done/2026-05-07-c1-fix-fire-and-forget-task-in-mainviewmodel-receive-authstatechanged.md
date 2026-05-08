---
created: 2026-05-07
source: v1.4 code review
severity: critical
area: ViewModels/MainViewModel.cs
related_phase: 20-auth-flow-stability
resolves_phase: 24
---

# C-1: Fire-and-forget Task in `MainViewModel.Receive(AuthStateChangedMessage)`

## Problem

`MainViewModel.cs:1008` calls `RefreshCommand.ExecuteAsync(null)` and discards the returned `Task` neither via `await` nor via `_ =`. Because `Receive` is a plain `void` method (not `async void`), unhandled exceptions from `PollUsageCoreAsync` / `UpdateUsagePropertiesAsync` are silently swallowed — no logging, no error-state update, no UI feedback.

The existing comment says "Fire-and-forget is intentional" / "Pitfall 6 accepted: option (a)", which acknowledges the problem but does not justify swallowing exceptions.

## Why This Matters

`Receive(AuthStateChangedMessage)` runs after a successful login. If the post-login refresh throws (Cloudflare hiccup, transient 5xx, JSON deserialization error), the user sees a stale MainView with no indication anything went wrong. The bug only manifests under network edge cases — UAT did not catch it.

## Fix

Replace `RefreshCommand.ExecuteAsync(null)` with a direct `_ = PollUsageAsync();` call:

```csharp
// Direct PollUsageAsync invocation (not RefreshCommand) routes through
// the existing reentrancy guard and writes errors to HasApiError/ApiErrorMessage.
_ = PollUsageAsync();
```

The explicit `_ =` discard documents intent. `PollUsageAsync` already contains the `if (IsRefreshing) return` guard and the `try/catch` that updates the API-error state — using it instead of `RefreshCommand.ExecuteAsync` reactivates the error pipeline.

## Test To Add

Add a test in `MainViewModelAuthFlowTests` that:
1. Configures the mock API to throw on the first call after `AuthStateChangedMessage(true)`
2. Sends the message
3. Asserts `HasApiError == true` and `ApiErrorMessage` is non-empty within a reasonable timeout

## Effort

XS — one-line change + one test.

## v1.5 Priority

High. Auth flow stability was the v1.4 milestone goal — a swallowed exception in the post-login refresh path directly contradicts that goal.

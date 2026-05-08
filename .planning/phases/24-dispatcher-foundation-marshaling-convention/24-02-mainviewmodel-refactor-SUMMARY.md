---
phase: 24-dispatcher-foundation-marshaling-convention
plan: 02
subsystem: viewmodel
tags: [dispatcher, thread-safety, mvvm, messenger, winui3, testing]

requires:
  - 24-01 (IDispatcherQueue interface, WinuiDispatcherQueueAdapter, FakeDispatcherQueue, ThreadSafeReceiveAttribute)

provides:
  - Thread-safe MainViewModel.Receive(AuthStateChangedMessage) via always-TryEnqueue wrapper (L-04)
  - HandleAuthStateChangedCore extracted private method (C-1/C-2 fix)
  - Constructor-injected IDispatcherQueue in MainViewModel (CD-01)
  - [ThreadSafeReceive] attributes on MainWindow.Receive handlers (CD-05 #3)

affects:
  - 24-03-convention-enforcement (MessengerThreadingConventionTests scans IRecipient<> methods)
  - All future IRecipient<> handlers in Phases 25-28 (G-1 baseline established)

tech-stack:
  added: []
  patterns:
    - "Constructor injection for IDispatcherQueue: field assigned before UpdateAvailable hook, non-null from construction"
    - "always-TryEnqueue: Receive(AuthStateChangedMessage) body is a single TryEnqueue call wrapping HandleAuthStateChangedCore"
    - "UnregisterAll + re-register in InitializeAsync: prevents double-subscription on re-init (C2-P3)"
    - "WinRT CreateTimer() still uses local DispatcherQueue.GetForCurrentThread() — not part of IDispatcherQueue abstraction"
    - "CopyChartToClipboard obtains WinRT DispatcherQueue locally — ExportHelper type boundary preserved"

key-files:
  modified:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs
    - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
    - CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs

key-decisions:
  - "CD-01: constructor injection chosen over lazy-resolve helper — FakeDispatcherQueue test ergonomics; non-null contract eliminates 4 sites of ?. null-conditional clutter"
  - "CD-04: UnregisterAll(this) as first statement of InitializeAsync, followed by re-registration of AuthStateChangedMessage + SessionTimeoutChangedMessage"
  - "CD-05 #3 option b: G-1 scoped to ViewModels/Services; MainWindow Receive methods exempted via [ThreadSafeReceive(reason)] attribute"
  - "CD-05 #4: RefreshIntervalChangedMessage lambda wrapped in TryEnqueue (UpdateRefreshInterval mutates _pollTimer + _refreshIntervalSeconds — DispatcherQueueTimer requires UI thread)"
  - "WinRT boundary: CreateTimer() and ExportHelper.CopyChartToClipboardAsync still use local DispatcherQueue.GetForCurrentThread() — IDispatcherQueue does not expose WinRT-specific extension methods"

metrics:
  duration: ~35 min
  completed: "2026-05-08"
  tasks: 3 of 3
  files_modified: 5
---

# Phase 24 Plan 02: MainViewModel Dispatch Fix Summary

**Thread-safe MainViewModel Receive(AuthStateChangedMessage): constructor-injected IDispatcherQueue, always-TryEnqueue wrapper, HandleAuthStateChangedCore extracted, UnregisterAll guard, [ThreadSafeReceive] on MainWindow handlers, 5 test files updated**

## Performance

- **Duration:** ~35 min
- **Started:** 2026-05-08T14:25:00Z
- **Completed:** 2026-05-08T14:42:00Z (approx)
- **Tasks:** 3 of 3
- **Files modified:** 5

## Accomplishments

- C-1 fixed: `_ = RefreshCommand.ExecuteAsync(null);` with PITFALLS C1-P1 inline comment inside `HandleAuthStateChangedCore`
- C-2 fixed: `Receive(AuthStateChangedMessage)` body is now a single `_dispatcherQueue.TryEnqueue(() => HandleAuthStateChangedCore(message))` call — off-thread UI mutation eliminated (L-04/C2-P1)
- C2-P2 fixed: `_dispatcherQueue` is `readonly IDispatcherQueue`, assigned in constructor — non-null from construction
- C2-P3 fixed: `InitializeAsync` starts with `WeakReferenceMessenger.Default.UnregisterAll(this);` followed by re-registration
- CD-05 #2: `Receive(SessionTimeoutChangedMessage)` drops `?.` — `_dispatcherQueue.TryEnqueue(RefreshSessionList)`
- CD-05 #3: MainWindow.xaml.cs `Receive(ThemeChangedMessage)` and `Receive(ResetWindowSizeMessage)` decorated with `[ThreadSafeReceive("Window receivers run on the UI thread...")]`
- CD-05 #4: `RefreshIntervalChangedMessage` lambda wrapped in `_dispatcherQueue.TryEnqueue(() => vm.UpdateRefreshInterval(m.Value))` (defensive — UpdateRefreshInterval mutates `_pollTimer` state)
- All `_dispatcherQueue?.` null-conditional patterns removed from MainViewModel.cs (0 matches remaining)
- `AggregateStatisticsAsync` null-guard `if (_dispatcherQueue != null)` block removed — uses `_dispatcherQueue.TryEnqueue(...)` directly
- App.xaml.cs factory updated to pass `sp.GetRequiredService<IDispatcherQueue>()` as 11th argument
- 2 test files updated (MainViewModelAuthFlowTests.cs, MainViewModelRefreshTests.cs) — `new FakeDispatcherQueue()` as 11th arg
- 247 tests pass (excluding 13+2 pre-existing JsonlService/ClaudeApiService baselines)

## Task Commits

1. **Task 1: MainViewModel refactor** — `b359ffb`
2. **Task 2: App.xaml.cs factory + MainWindow [ThreadSafeReceive]** — `1f35edf`
3. **Task 3: Test files updated** — `25100a7`

## Lines Edited in MainViewModel.cs

| Edit | Before | After | Lines affected (approx) |
|------|--------|-------|------------------------|
| 1 — Field type | `DispatcherQueue? _dispatcherQueue` | `readonly IDispatcherQueue _dispatcherQueue` | 69 |
| 2 — Constructor | 10 params | 11 params + `_dispatcherQueue = dispatcherQueue;` | 277-303 |
| 3 — InitializeAsync | `GetForCurrentThread()` + assign | `UnregisterAll` + re-register; `winuiDispatcherQueue` for CreateTimer | 308-411 |
| 4 — Lambda line 318 | Synchronous `UpdateRefreshInterval` call | Wrapped in `_dispatcherQueue.TryEnqueue(...)` | 318-321 |
| 5 — Receive + HandleCore | Single method with raw UI mutations | `Receive` wraps in `TryEnqueue`; `HandleAuthStateChangedCore` extracted | 997-1044 |
| 6 — Line 1032 | `_dispatcherQueue?.TryEnqueue` | `_dispatcherQueue.TryEnqueue` | 1039 |
| 7 — OnUpdateAvailable | `_dispatcherQueue ?? GetForCurrentThread()` fallback | `_dispatcherQueue.TryEnqueue(...)` direct | 977-987 |
| 8 — AggregateStatisticsAsync | `?.TryEnqueue` + null-guard block | `.TryEnqueue(...)` direct (3 sites) | 803-828 |
| 9 — CopyChartToClipboard | `if (_dispatcherQueue == null) return;` | local `winuiDispatcherQueue` for ExportHelper boundary | 965-972 |

## Test Files Updated

| File | Change | Reason |
|------|--------|--------|
| `MainViewModelAuthFlowTests.cs` | `using CCInfoWindows.Tests.Helpers;` + `new FakeDispatcherQueue()` in `CreateViewModel()` and `CreateViewModelWithSuccessfulApi()` | 2 call sites with 11th arg |
| `MainViewModelRefreshTests.cs` | `using CCInfoWindows.Tests.Helpers;` + `new FakeDispatcherQueue()` in `CreateSut()` | 1 call site with 11th arg |
| `MainViewModelStatisticsTests.cs` | No change | Uses `MainViewModelTestHarness`, no direct `new MainViewModel(...)` |
| `SessionDisplayTooltipTests.cs` | No change | Reflection-only, no direct `new MainViewModel(...)` |
| `SettingsLogoutMessageRoundtripTests.cs` | No change | Tests `SettingsViewModel` only |

## Pre-flight Grep Results

- `_dispatcherQueue?.` in MainViewModel.cs: **0 matches** (all null-conditionals removed)
- `DispatcherQueue?` field declarations in MainViewModel.cs: **0 matches** (field is now `readonly IDispatcherQueue`)
- `HandleAuthStateChangedCore` in MainViewModel.cs: **1 match** (private method exists)
- `_ = RefreshCommand.ExecuteAsync(null);` in MainViewModel.cs: **1 match** (explicit discard)
- `[ThreadSafeReceive(` in MainWindow.xaml.cs: **2 matches** (both Receive methods decorated)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] WinRT DispatcherQueue boundary for AggregateStatisticsAsync and CopyChartToClipboard**
- **Found during:** Task 1
- **Issue:** After converting `_dispatcherQueue` to `readonly IDispatcherQueue`, `AggregateStatisticsAsync` had `if (_dispatcherQueue != null)` guards (dead code after constructor injection) and `CopyChartToClipboard` passed `_dispatcherQueue` to `ExportHelper.CopyChartToClipboardAsync(DispatcherQueue ...)` — type mismatch.
- **Fix:** `AggregateStatisticsAsync` null-guard block removed, all sites use `_dispatcherQueue.TryEnqueue(...)` directly. `CopyChartToClipboard` obtains `DispatcherQueue.GetForCurrentThread()` locally (command runs on UI thread) for the ExportHelper call — preserves ExportHelper's WinRT type signature without modifying it.
- **Files modified:** `MainViewModel.cs`
- **Commit:** `b359ffb`

## Carried Forward to Plan 24-03

- Convention test (`MessengerThreadingConventionTests`) must filter `Window` subclasses per CD-05 #3 option b
- `[ThreadSafeReceive(reason)]` on MainWindow handlers will be recognized as valid exemptions
- `HandleAuthStateChangedCore` is a private helper — convention test scans `IRecipient<T>.Receive` public interface methods, not private helpers (no action needed)

## Known Stubs

None — all observable properties wired to real service calls.

## Self-Check

- [x] `b359ffb` exists in git log
- [x] `1f35edf` exists in git log
- [x] `25100a7` exists in git log
- [x] MainViewModel.cs field: `private readonly IDispatcherQueue _dispatcherQueue;`
- [x] `HandleAuthStateChangedCore` private method exists
- [x] `_ = RefreshCommand.ExecuteAsync(null);` exists inside HandleAuthStateChangedCore
- [x] `WeakReferenceMessenger.Default.UnregisterAll(this);` first statement of InitializeAsync
- [x] 0 occurrences of `_dispatcherQueue?.` in MainViewModel.cs
- [x] `[ThreadSafeReceive(` appears exactly twice in MainWindow.xaml.cs
- [x] 247 tests pass (0 failures, excluding pre-existing baselines)
- [x] Pricing fire-and-forget at lines ~371-375 untouched

## Self-Check: PASSED

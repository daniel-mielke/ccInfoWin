---
phase: 24-dispatcher-foundation-marshaling-convention
plan: 03
subsystem: infra
tags: [convention-test, il-scan, dispatcher, mvvm, nuget, documentation]

requires:
  - 24-01 (ThreadSafeReceiveAttribute, IDispatcherQueue)
  - 24-02 (MainViewModel G-1-compliant Receive methods, MainWindow [ThreadSafeReceive] decorations)

provides:
  - MessengerThreadingConventionTests G-1 enforcement xUnit class (DISPATCH-06)
  - G-1 paragraph in CLAUDE.md MVVM Conventions section (DISPATCH-05)
  - CommunityToolkit.Mvvm 8.4.2 + Microsoft.WindowsAppSDK 1.8.260416003 (L-08)

affects:
  - 25-session-hydration (new IRecipient<> handler must pass MessengerThreadingConventionTests)
  - 26-session-renaming (ISessionNameStore Receive handlers must pass MessengerThreadingConventionTests)
  - 27-nextwin-orgid-pricing-l10n (any new IRecipient<> must pass MessengerThreadingConventionTests)
  - 28-cleanup (CLAUDE.md G-1 paragraph is now the authoritative reference)

tech-stack:
  added: []
  patterns:
    - "IL-bytecode scan (D-03 minimal-cost): MethodInfo.GetMethodBody().GetILAsByteArray() + Module.ResolveMethod — no Mono.Cecil or new NuGet packages"
    - "Convention-as-test (v1.4 L10N precedent): structural invariants enforced by xUnit, not reviewer discipline alone"
    - "G-1 Tier-1 (CLAUDE.md) + Tier-2 (xUnit) enforcement model — Roslyn analyzer deferred to v1.6+"

key-files:
  created:
    - CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs
  modified:
    - CLAUDE.md
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj

key-decisions:
  - "D-03 IL-scan mechanism: callvirt/call opcodes 0x6F/0x28, token resolved via Module.ResolveMethod — zero new NuGet packages (Mono.Cecil explicitly rejected per research/SUMMARY.md)"
  - "Sanity gate upgraded from Assert.NotEmpty to Assert.True(count >= 4) with named inventory message (plan-checker recommendation)"
  - "NuGet bump: CommunityToolkit.Mvvm 8.4.0 → 8.4.2; Microsoft.WindowsAppSDK 1.8.260209005 → 1.8.260416003 (same minor, servicing-patch-only, no breaking changes)"

metrics:
  duration: ~4 min
  started: "2026-05-08T14:35:16Z"
  completed: "2026-05-08T14:39:24Z"
  tasks: 3 of 3
  files_modified: 3 (1 created, 2 modified)

requirements-completed: [DISPATCH-05, DISPATCH-06]
---

# Phase 24 Plan 03: Convention Test & Docs Summary

**G-1 enforcement infrastructure: MessengerThreadingConventionTests IL-scan xUnit class + CLAUDE.md G-1 paragraph + NuGet patch bumps CommunityToolkit.Mvvm 8.4.2 / WindowsAppSDK 1.8.260416003**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-05-08T14:35:16Z
- **Completed:** 2026-05-08T14:39:24Z
- **Tasks:** 3 of 3
- **Files modified:** 3

## Accomplishments

- `MessengerThreadingConventionTests` xUnit class in `CCInfoWindows.Tests/Convention/` — 2 `[Fact]` methods enforce G-1 via IL-bytecode scan + attribute reflection
- `All_IRecipient_Receive_Methods_Either_Marshal_Or_Are_ThreadSafeAttributed` — passes on first run: all 4 Phase 24 inventory sites compliant
- `ThreadSafeReceiveAttribute_RejectsEmptyReason_AtConstruction` — D-02 spot check verifying attribute constructor enforces non-empty reason
- G-1 paragraph added to CLAUDE.md MVVM Conventions section after `partial class` bullet (L-06 anchor)
- `CommunityToolkit.Mvvm` bumped 8.4.0 → 8.4.2; `Microsoft.WindowsAppSDK` bumped 1.8.260209005 → 1.8.260416003
- All 249 tests pass after bumps (0 regressions)

## Task Commits

1. **Task 1: MessengerThreadingConventionTests.cs** — `cecda62` (test)
2. **Task 2: CLAUDE.md G-1 paragraph** — `3830953` (docs)
3. **Task 3: NuGet bump** — `d2e86f9` (chore)

## Convention Test Details (DISPATCH-06)

### D-03 Mechanism: IL-Bytecode Scan

- Opcode filter: `0x28` (call) and `0x6F` (callvirt)
- Token resolution: `Module.ResolveMethod(token, genericTypeArgs, genericMethodArgs)` — handles generic types correctly
- Match criterion: resolved `MethodInfo.Name == "TryEnqueue"` AND `DeclaringType == typeof(IDispatcherQueue)` OR implements `IDispatcherQueue`
- Zero new NuGet packages — Mono.Cecil explicitly avoided per research/SUMMARY.md decision

### Phase 24 Receiver Inventory (4 sites)

| Method | Type | Disposition | Evidence |
|--------|------|-------------|----------|
| `Receive(AuthStateChangedMessage)` | `MainViewModel` | IL-scan PASS | Body calls `_dispatcherQueue.TryEnqueue(() => HandleAuthStateChangedCore(message))` |
| `Receive(SessionTimeoutChangedMessage)` | `MainViewModel` | IL-scan PASS | Body calls `_dispatcherQueue.TryEnqueue(RefreshSessionList)` (CD-05 #2) |
| `Receive(ThemeChangedMessage)` | `MainWindow` | Attribute PASS | `[ThreadSafeReceive("Window receivers run on the UI thread...")]` |
| `Receive(ResetWindowSizeMessage)` | `MainWindow` | Attribute PASS | `[ThreadSafeReceive("Window receivers run on the UI thread...")]` |

### Sanity Assertion

Upgraded from `Assert.NotEmpty` to `Assert.True(receivers.Count >= 4, ...)` with a named inventory message — machine-checkable minimum count with clear failure output if the reflection filter breaks.

### Window Subclass Handling (CD-05 #3 option b)

Window subclasses are NOT excluded from the scan — they ARE scanned, but the body-check is replaced by a mandatory `[ThreadSafeReceive(reason)]` attribute requirement. If a Window receiver lacks the attribute, the test fails with a clear `"CD-05 #3"` message. This ensures exemptions are always documented.

## G-1 Paragraph Details (DISPATCH-05)

- **Placement:** MVVM Conventions section, after `Use partial class with source generators` bullet
- **Content:** Always-TryEnqueue rule, `if (!HasThreadAccess)` shortcut prohibition, `[ThreadSafeReceive(reason)]` escape hatch, `MessengerThreadingConventionTests` enforcement citation, Window subclass exemption, D-13 lesson cross-VM communication priority
- **Tier model:** Tier-1 (CLAUDE.md normative text) + Tier-2 (xUnit convention test) — Roslyn analyzer deferred to v1.6+

## NuGet Bump Diff (L-08)

| Package | Before | After | Type |
|---------|--------|-------|------|
| `CommunityToolkit.Mvvm` | 8.4.0 | 8.4.2 | Bug-fix patch (same minor) |
| `Microsoft.WindowsAppSDK` | 1.8.260209005 | 1.8.260416003 | Servicing patch (same minor) |

No `Microsoft.Windows.SDK.BuildTools` change (out of scope per plan).

## Test Results

- **Before bumps:** 247 tests (Plans 24-01 + 24-02 baseline)
- **After Task 1:** 249 tests (+ 2 convention tests)
- **After Task 3 (bumps):** 249 tests, 0 failures — no regressions from NuGet bumps
- **Convention test runtime:** 33ms (no I/O, no DI bootstrap, pure reflection + IL scan)
- **Pre-existing baselines excluded:** 13 `JsonlServiceTests` + 2 `ClaudeApiServiceTests` (parameter naming mismatch, production unaffected per STATE.md)

## Deviations from Plan

None — plan executed exactly as written. Sanity assertion upgrade from `Assert.NotEmpty` to `Assert.True(count >= 4)` is explicitly recommended in the execution_protocol and implemented accordingly.

## Known Stubs

None.

## Threat Flags

None — no new network endpoints, auth paths, or file access patterns introduced.

## Carried Forward to Phases 25-28

- Every new `IRecipient<>` handler added in Phases 25-27 MUST pass `MessengerThreadingConventionTests` — failure is loud and immediate
- CLAUDE.md G-1 paragraph is the authoritative reference — planners and executors will read it before authoring new handlers
- Phase 24 is COMPLETE — all 3 plans shipped (24-01: adapter, 24-02: MainViewModel fix, 24-03: enforcement)

## Self-Check

- [x] `cecda62` exists in git log
- [x] `3830953` exists in git log
- [x] `d2e86f9` exists in git log
- [x] `CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs` exists
- [x] `CLAUDE.md` contains `G-1 (Messenger receive thread-marshaling)`
- [x] `CLAUDE.md` contains `MessengerThreadingConventionTests`
- [x] `CLAUDE.md` contains `D-13`
- [x] `CCInfoWindows.csproj` contains `Version="8.4.2"` on CommunityToolkit.Mvvm line
- [x] `CCInfoWindows.csproj` contains `Version="1.8.260416003"` on Microsoft.WindowsAppSDK line
- [x] 249 tests pass (0 failures, excluding pre-existing baselines)
- [x] Zero new compiler warnings (pre-existing MVVMTK0045/MVVMTK0034/CS0618 unchanged)

## Self-Check: PASSED

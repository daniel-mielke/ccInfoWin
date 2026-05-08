# Phase 24: Dispatcher Foundation & Marshaling Convention - Context

**Gathered:** 2026-05-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 24 delivers a thread-safe Messenger receive infrastructure: an `IDispatcherQueue` adapter (mirror of v1.4 `IDispatcherTimer`), a refactored `MainViewModel.Receive(AuthStateChangedMessage)` that fixes both the C-1 fire-and-forget exception swallow and the C-2 off-thread UI-mutation bug in a single edit, a documented project-wide marshaling convention (G-1) in `CLAUDE.md`, and a reflection-based xUnit convention test that fails the build when a new `IRecipient<>` handler bypasses the rule. Two opportunistic NuGet patch bumps ship in this phase: `CommunityToolkit.Mvvm` 8.4.0→8.4.2 and `Microsoft.WindowsAppSDK` 1.8.260209005→1.8.260416003.

**Strict scope:** Foundation only. No new feature code. Phase 24 must ship before Phases 25–28 because each subsequent phase adds new `IRecipient<>` handlers that depend on `IDispatcherQueue` and the G-1 convention to be correct from day one.

</domain>

<decisions>
## Implementation Decisions

### G-1 Convention Enforcement Mechanism (DISPATCH-06)

- **D-01: Mechanism is the `[ThreadSafeReceive]` / implicit-default attribute pair.** Pure reflection-only method-body inspection is rejected (fragile against compiler inlining, source generators, helper-method wrappers; would require Mono.Cecil or similar new NuGet — violates "no new top-level packages" from research/SUMMARY.md). Manual code-review checklist is rejected because REQUIREMENTS.md DISPATCH-06 explicitly requires an xUnit `MessengerThreadingConventionTests` class. Resolves the "30-min Phase 24 spike" open detail flagged in research/SUMMARY.md Decision 3 and REQUIREMENTS.md DISPATCH-06.

- **D-02: `[ThreadSafeReceive(reason)]` requires a non-empty `reason` string in the constructor.** The convention test asserts `!string.IsNullOrWhiteSpace(attr.Reason)`. This forces the developer to articulate WHY a receive method is exempt from G-1; reviewer never has to second-guess intent. Mirrors the spirit of `[Obsolete("reason")]` in BCL.

- **D-03: `[RequiresMarshal]` is implicit (not a required marker).** The default rule is "every `IRecipient<T>.Receive` body that mutates `[ObservableProperty]`, calls `INavigationService`, or touches XAML controls MUST wrap in `IDispatcherQueue.TryEnqueue`." No marker is needed in the default case — convention test asserts EITHER `[ThreadSafeReceive(reason)]` is present OR the method body contains a call to `IDispatcherQueue.TryEnqueue` (verified by IL bytecode scan or equivalent — minimal-cost variant chosen during Plan Phase). Avoids doubled boilerplate as Phases 25–27 add ~5+ new `IRecipient<>` handlers.

- **D-04: G-1 violations surface as xUnit test failures, not MSBuild build errors.** `MessengerThreadingConventionTests` runs in the regular xUnit suite alongside `ResourceCoverageTests` (v1.4 L10N convention test) and `IDispatcherTimer` lifecycle tests. CI breaks the build via test failure; local dev sees a red test in VS Test Explorer. No `.csproj` MSBuild-target additions. Consistent DX with existing convention tests.

### Carrying Forward (locked upstream — not re-discussed)

These decisions are inherited and MUST NOT be re-litigated in Plan Phase:

- **L-01:** Interface shape is `IDispatcherQueue { bool TryEnqueue(Action); bool HasThreadAccess; }` — exact mirror of `Services/Interfaces/IDispatcherTimer.cs`. (REQ DISPATCH-01)
- **L-02:** `WinuiDispatcherQueueAdapter` registered as Singleton in `App.xaml.cs ConfigureServices`. (REQ DISPATCH-02)
- **L-03:** `FakeDispatcherQueue` in test project replaces every `DispatcherQueue.TryEnqueue` test seam in headless xUnit tests. (REQ DISPATCH-03)
- **L-04:** Always-TryEnqueue rule — NO `if (!HasThreadAccess) ... else ...` shortcut. The entire `Receive(AuthStateChangedMessage)` body wraps in `_dispatcherQueue.TryEnqueue(() => HandleCore(...))`. Reason: PITFALLS C2-P1 — recursive `Send → Receive` chains on UI thread execute synchronously inside the parent's stack frame, producing mid-update inconsistent state. (REQ DISPATCH-04, PITFALLS C2-P1)
- **L-05:** C-1 + C-2 fixed in a single edit on `Receive(AuthStateChangedMessage)`. Failures in the post-login refresh path surface via existing `HasApiError` / `ApiErrorMessage` (already wired in `PollUsageCoreAsync` lines 428–458) instead of being swallowed by a fire-and-forget Task. (REQ DISPATCH-04, PITFALLS C1-P1)
- **L-06:** G-1 convention text lands in `CLAUDE.md` MVVM Conventions section. Cross-VM communication priority documented: direct DI > singleton-service .NET event > `WeakReferenceMessenger`. (REQ DISPATCH-05, PITFALLS G-1)
- **L-07:** D-13 lesson honored project-wide — no `WeakReferenceMessenger` broadcasts for exactly-once flows (logout, save-on-close, future A2 rename → refresh). (PROJECT.md Key Decisions, memory architecture_weakreferencemessenger_with_transient_vms.md)
- **L-08:** NuGet patch bumps land in this phase: `CommunityToolkit.Mvvm` 8.4.0 → 8.4.2 (bug-fix-only, same minor) and `Microsoft.WindowsAppSDK` 1.8.260209005 → 1.8.260416003 (latest 1.8.x servicing patch). (ROADMAP Success Criterion #5, research/SUMMARY.md Stack Verdict)
- **L-09:** Adapter mirrors v1.4 `IDispatcherTimer` template exactly — interface ~5 LOC + production adapter ~15 LOC + fake ~5–20 LOC + DI singleton + convention test. (research/SUMMARY.md Decision 3)

### Out of Scope (explicit)

- **O-01:** Roslyn analyzer for G-1 enforcement — deferred to v1.6+. (REQUIREMENTS.md "Out of Scope")
- **O-02:** WinAppSDK 2.0 major-version bump — deferred to v1.6+ or alignment with future V2-05 (.NET 10 LTS migration). (research/SUMMARY.md, REQUIREMENTS.md "Out of Scope")
- **O-03:** Source-Generator-based compile-time G-1 check — rejected (new complexity class; out of Phase 24 time-box).

### Claude's Discretion (with anchored guidance)

These areas were not interactively discussed; the user delegated them to Plan Phase with PITFALLS.md anchors as the default playbook. Plan Phase MUST follow the anchored recommendations unless a concrete blocker is discovered during research:

- **CD-01: `_dispatcherQueue` lifecycle / cold-path null risk.** Anchor: PITFALLS.md C2-P2. Recommended approach is **lazy-resolve via a `ResolveDispatcher()` helper that throws `InvalidOperationException` when called off-UI-thread before `InitializeAsync`**. Constructor-set is rejected because DI may resolve `MainViewModel` off-UI-thread; full DI-injection of `IDispatcherQueue` is the cleanest test-seam but adds one constructor parameter — Plan Phase decides between lazy-resolve helper and constructor injection based on test-double ergonomics for `FakeDispatcherQueue`. Either way: the existing `_dispatcherQueue?.TryEnqueue(...)` null-conditional pattern at `MainViewModel.cs:1032` must be replaced with non-null `IDispatcherQueue` field after this phase.

- **CD-02: C-1 fire-and-forget surfacing pattern for `RefreshCommand.ExecuteAsync(null)` (line 1008).** Anchor: PITFALLS.md C1-P1. The try/catch at the call site is dead code — `[RelayCommand]` machinery catches exceptions inside `Refresh()` first. The real fix is already in place: `Refresh()` (line 906) sets `HasApiError` / `ApiErrorMessage` on failure via `PollUsageCoreAsync`. Recommended action in Phase 24: change line 1008 from `RefreshCommand.ExecuteAsync(null);` to `_ = RefreshCommand.ExecuteAsync(null);` — the explicit discard documents fire-and-forget intent. Inline comment cites PITFALLS C1-P1 for posterity. **Pricing fire-and-forget at lines 371–375 is OUT of Phase 24 scope** — its surfacing logic (`IsPricingError` InfoBar) belongs to Phase 27 PRICING-01. Phase 24 must not pre-empt Phase 27's work.

- **CD-03: NuGet patch bumps placement.** Anchor: ROADMAP Success Criterion #5. Recommended approach: bundle bumps into the same Plan as the C-1/C-2 edit (single `.csproj` touch). Rationale: small surface, NuGet bumps can break tests independently — best to verify both bumps and the dispatcher refactor in one CI run. Plan Phase decides whether to ship the bump in its own Wave 1 (clean test-baseline first, dispatcher refactor in Wave 2) or in the same Wave (one commit, smaller plan-table).

- **CD-04: C2-P3 double-registration in `InitializeAsync` (`WeakReferenceMessenger.Default.UnregisterAll(this)`).** Anchor: PITFALLS.md C2-P3. PITFALLS labels this "low severity but easy to forget when adding A2's `ISessionNameStore.NamesChanged` subscription." Recommended action: **Phase 24, not Phase 28.** Rationale: G-1 documentation moment; codifying the messenger-recipient lifecycle once, in the foundation phase, prevents Phases 25–27 from re-introducing the bug. Add `WeakReferenceMessenger.Default.UnregisterAll(this);` at the top of `InitializeAsync` (line 308) paired with re-registration. Document in `CLAUDE.md` alongside G-1.

- **CD-05: Migration scope of existing `IRecipient<>` implementations.** Inventory found 4 sites:
  1. `ViewModels/MainViewModel.cs:49` — `IRecipient<AuthStateChangedMessage>` — **must be migrated** (this is C-1/C-2 itself).
  2. `ViewModels/MainViewModel.cs:50` — `IRecipient<SessionTimeoutChangedMessage>` — **already G-1-compliant** at line 1032 (`_dispatcherQueue?.TryEnqueue(RefreshSessionList)`); needs only attribute decision: implicit-default OR explicit `[ThreadSafeReceive("UI-thread-only via SessionTimeoutMinutes setter")]` — Plan Phase chooses.
  3. `MainWindow.xaml.cs:18` — `IRecipient<ThemeChangedMessage>`, `IRecipient<ResetWindowSizeMessage>` — **G-1 scope decision required.** A WinUI 3 `Window` is by-construction a UI element (must be created and accessed on the UI thread that hosts it). Two valid framings: (a) extend G-1 to all `IRecipient<>` regardless of receiver type — apply attribute or marshaling to MainWindow handlers too; (b) scope G-1 to ViewModels and Services only — exclude `Window` subclasses by reflection filter. Plan Phase decides; recommendation is (b) with an explicit `[ThreadSafeReceive("Window receivers run on the UI thread that hosts the window")]` on these two handlers as a documented exception.
  4. **Lambda registrations are NOT covered by `IRecipient<>` reflection.** Two lambda registrations exist in `MainViewModel.InitializeAsync`: line 318 (`RefreshIntervalChangedMessage`) and line 324 (`SonnetContextChangedMessage`). Line 324 is already G-1-compliant (`vm._dispatcherQueue?.TryEnqueue(...)`); line 318 calls `UpdateRefreshInterval` synchronously — Plan Phase verifies whether `UpdateRefreshInterval` mutates `[ObservableProperty]` and decides whether to wrap in `TryEnqueue` for safety. Convention test does NOT need to inspect lambda registrations — they are discovered via code review at registration sites.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase 24 deliverable scope
- `.planning/REQUIREMENTS.md` §"Cluster C — DISPATCH" (lines 60–66) — DISPATCH-01..06 are the 6 locked requirements.
- `.planning/ROADMAP.md` §"Phase 24: Dispatcher Foundation & Marshaling Convention" (lines 260–270) — 5 success criteria; criterion #5 covers the NuGet patch bumps.

### Architectural research and decisions
- `.planning/research/SUMMARY.md` — Decision 3 (`IDispatcherQueue` full adapter scope), Top 3 Pitfalls (G-1, C2-P1), Stack Verdict (NuGet bumps).
- `.planning/research/PITFALLS.md` §"Cluster C" — C1-P1, C2-P1, C2-P2, C2-P3, M3-P1; §"Cross-Cluster" — G-1, G-2, G-3.
- `.planning/research/ARCHITECTURE.md` — five architectural decisions with cited file:line evidence.
- `.planning/PROJECT.md` Key Decisions table — D-13 (`WeakReferenceMessenger` + `AddTransient` recipient-GC pitfall), `IDispatcherTimer` adapter precedent.

### In-tree code anchors (must read before edits)
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherTimer.cs` — adapter pattern template; `IDispatcherQueue` mirrors this exactly.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:49–52` — existing `IRecipient<>` declarations.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:301–303` — constructor-time messenger registration (BEFORE `_dispatcherQueue` is set; relevant to PITFALLS C2-P2).
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:308–311` — `InitializeAsync` start; `_dispatcherQueue` assignment site.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:318–332` — lambda registrations for `RefreshIntervalChangedMessage` (line 318) and `SonnetContextChangedMessage` (line 324). Line 324 is G-1-compliant; line 318 needs verification.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:371–375` — pricing fire-and-forget. Surfacing OUT of Phase 24 scope (Phase 27 PRICING).
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:997–1026` — `Receive(AuthStateChangedMessage)` body — the C-1 + C-2 surface to refactor.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:1028–1033` — `Receive(SessionTimeoutChangedMessage)` body — G-1 reference implementation.
- `CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs:18` — `IRecipient<ThemeChangedMessage>`, `IRecipient<ResetWindowSizeMessage>` declarations (CD-05 #3 scope decision).
- `CCInfoWindows/CCInfoWindows/App.xaml.cs:137–178` — `ConfigureServices` for new `IDispatcherQueue` singleton registration.
- `CCInfoWindows/CCInfoWindows/Services/UsageHistoryService.cs:25–29,58–79,81–102` — G-2 pattern reference (informational only — Phase 24 does not consume G-2; Phase 26 first consumer).

### Convention documentation target
- `CLAUDE.md` §"MVVM Conventions" — G-1 paragraph lands here. Currently silent on threading rules — Phase 24 fills the gap.
- `CLAUDE.md` §"Bash Permission Rules" precedent — same prescriptive shape as G-1: STRICT, zero exceptions, with documented escape hatch.

### Project memory
- `architecture_weakreferencemessenger_with_transient_vms.md` — D-13 root cause; drives G-1 framing.
- `architecture_v1_5_dispatcher_marshaling_conventions.md` — pre-authored G-1/G-2/G-3 spec; Phase 24 is the first concrete consumer.

### Pending todos folded into this phase (DISPATCH cluster only)
- `.planning/todos/pending/2026-05-07-c1-fix-fire-and-forget-task-in-mainviewmodel-receive-authstatechanged.md` — folded as DISPATCH-04 / CD-02. Resolves on phase ship.
- `.planning/todos/pending/2026-05-07-c2-add-dispatcher-marshaling-to-receive-authstatechanged.md` — folded as DISPATCH-04 / L-04 / L-05. Resolves on phase ship.

### Pending todos NOT folded (explicitly deferred to later phases)
- `.planning/todos/pending/2026-05-07-m3-revert-contextmodelbadgecolor-default-to-gray.md` — Phase 28 (CLEANUP-02 / G-3 precedent).
- `.planning/todos/pending/2026-05-07-m1-delete-orphan-logoutrequestedmessage.md` — Phase 28 (CLEANUP-01).
- `.planning/todos/pending/2026-05-07-m2-localize-lastfetchrelativetime-strings.md` — Phase 27 (L10N-01, couples with PRICING).
- `.planning/todos/pending/2026-05-07-nits-v14-code-review-cleanups.md` — Phase 28 (CLEANUP-03).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- **`IDispatcherTimer` adapter (v1.4)** — `Services/Interfaces/IDispatcherTimer.cs`. Exact template for `IDispatcherQueue`. Production adapter (`WinuiDispatcherTimerAdapter`) and `FakeDispatcherTimer` already exist in tree; mirror their shape.
- **`HasApiError` / `ApiErrorMessage` plumbing** — `MainViewModel.cs` lines 428–458. Already wired into `PollUsageCoreAsync`; surfaces via existing InfoBar. C-1 fix needs no new properties — just stop swallowing the post-login refresh exception.
- **`ResourceCoverageTests` (v1.4)** — XDocument-based xUnit class that validates resw key presence structurally. Same shape as the new `MessengerThreadingConventionTests` — load assembly, iterate types, assert per-method invariants.
- **`SemaphoreSlim` write-guard pattern (`UsageHistoryService.cs:25–29`)** — informational; Phase 24 doesn't consume it but Plan Phase agents reading this CONTEXT.md should know it exists for Phase 26.

### Established Patterns

- **Adapter-as-test-seam** (v1.4 D-12): WinRT-typed APIs (`DispatcherTimer`, `DispatcherQueue`) cannot be faked headlessly without an interface wrapper. Phase 24 extends the precedent.
- **Convention-as-test** (v1.4 L10N): structural invariants (resw keys must exist in both locales, threading must respect G-1) are enforced by xUnit, not by reviewer discipline alone.
- **`[ObservableProperty]` source generators** (CommunityToolkit.Mvvm 8.4.x): `_camelCase` private field → `PascalCase` public property. Convention test must distinguish source-generated `Receive` overloads from user-authored ones — `IRecipient<T>` is the user-authored marker; `MessageHandlersGenerator` artifacts are NOT recipients.

### Integration Points

- **`App.xaml.cs:137 ConfigureServices`** — add `services.AddSingleton<IDispatcherQueue, WinuiDispatcherQueueAdapter>();`. Position: after `services.AddSingleton<HttpClient>();` (infrastructure), before service singletons. `WinuiDispatcherQueueAdapter` constructor calls `DispatcherQueue.GetForCurrentThread()` — App.OnLaunched runs on UI thread, so this is safe.
- **`MainViewModel` constructor** — replace `_dispatcherQueue` field nullable assignment with `IDispatcherQueue _dispatcherQueue` constructor parameter (CD-01 decision deferred to Plan Phase). Update factory at `App.xaml.cs:164–174` to pass new dependency.
- **`MainViewModel.Receive(AuthStateChangedMessage)` line 997** — entire body wraps in `_dispatcherQueue.TryEnqueue(() => HandleAuthStateChangedCore(message))`. Extract `HandleAuthStateChangedCore` private method containing the existing line 1000–1025 logic. Preserve all D-01/D-02/D-03 behaviors verbatim.
- **`MainViewModel.InitializeAsync` line 308** — add `WeakReferenceMessenger.Default.UnregisterAll(this);` as first statement (CD-04). Then proceed with existing logic.
- **`CLAUDE.md` MVVM Conventions** — append G-1 paragraph immediately after the existing "Use `DispatcherQueue.TryEnqueue()` for UI thread marshaling" sentence (currently informal; G-1 makes it normative for `IRecipient<>`).
- **Test project** — `MessengerThreadingConventionTests.cs` new class; `FakeDispatcherQueue.cs` new class. Convention test references `Microsoft.UI.Dispatching` indirectly via `IDispatcherQueue`, so no WinRT runtime dependency in test assemblies (same pattern as `IDispatcherTimer`).

</code_context>

<specifics>
## Specific Ideas

- **Convention test pseudo-code (Plan Phase will refine):**
  ```csharp
  // MessengerThreadingConventionTests.cs (sketch — Plan Phase chooses IL-scan vs. source-read)
  [Fact]
  public void All_IRecipient_Receive_Methods_Either_Marshal_Or_Are_ThreadSafeAttributed()
  {
      var asm = typeof(MainViewModel).Assembly;
      var receivers = asm.GetTypes()
          .Where(t => !t.IsSubclassOf(typeof(Microsoft.UI.Xaml.Window))) // CD-05 #3 scope
          .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
          .Where(m => m.Name == "Receive"
                   && m.GetParameters().Length == 1
                   && m.DeclaringType.GetInterfaces()
                        .Any(i => i.IsGenericType
                              && i.GetGenericTypeDefinition() == typeof(IRecipient<>)
                              && i.GetGenericArguments()[0] == m.GetParameters()[0].ParameterType));

      foreach (var m in receivers)
      {
          var attr = m.GetCustomAttribute<ThreadSafeReceiveAttribute>();
          if (attr != null)
          {
              Assert.False(string.IsNullOrWhiteSpace(attr.Reason),
                  $"{m.DeclaringType.Name}.{m.Name}({m.GetParameters()[0].ParameterType.Name}) has [ThreadSafeReceive] without reason.");
              continue;
          }
          // Default rule: body must call IDispatcherQueue.TryEnqueue.
          // Plan Phase chooses: IL-bytecode scan via MethodInfo.GetMethodBody().GetILAsByteArray()
          //   resolving callvirt tokens, OR a controlled source-file read via reflection metadata.
          Assert.True(BodyCallsTryEnqueue(m),
              $"{m.DeclaringType.Name}.{m.Name} mutates UI state without IDispatcherQueue.TryEnqueue and lacks [ThreadSafeReceive(reason)].");
      }
  }
  ```
- **Attribute shape sketch:**
  ```csharp
  [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
  public sealed class ThreadSafeReceiveAttribute : Attribute
  {
      public string Reason { get; }
      public ThreadSafeReceiveAttribute(string reason)
      {
          if (string.IsNullOrWhiteSpace(reason))
              throw new ArgumentException("Reason must be non-empty.", nameof(reason));
          Reason = reason;
      }
  }
  ```
  Lives in `Services/Interfaces/` next to `IDispatcherQueue.cs` (or `Helpers/Threading/` — Plan Phase decides namespace).
- **G-1 paragraph for `CLAUDE.md` (sketch — Plan Phase finalizes):**
  > **G-1 — `IRecipient<T>.Receive` thread-marshaling rule (STRICT):** Every `IRecipient<T>.Receive(T)` method body that mutates `[ObservableProperty]` fields, calls `INavigationService`, or touches XAML controls MUST wrap the body in `IDispatcherQueue.TryEnqueue(() => HandleCore(...))`. NEVER use the `if (!HasThreadAccess) ... else ...` shortcut — recursive `Send → Receive` chains on the UI thread execute synchronously inside the parent's stack frame, producing mid-update inconsistent state. Always-`TryEnqueue` is the rule. Exception: methods provably called only on the UI thread may be marked `[ThreadSafeReceive("specific reason proving UI-thread-only")]` — `MessengerThreadingConventionTests` enforces both.

</specifics>

<deferred>
## Deferred Ideas

- **Roslyn analyzer for G-1** — deferred to v1.6+ per REQUIREMENTS.md "Out of Scope". Tier-1 (CLAUDE.md) + Tier-2 (xUnit reflection test) is sufficient for v1.5.
- **WinAppSDK 2.0 major-version bump** — deferred per REQUIREMENTS.md.
- **Source-Generator-based compile-time G-1 check** — rejected; new complexity class out of Phase 24 time-box.
- **G-2 (`SemaphoreSlim` JSON store pattern)** — Phase 26 first consumer (`ISessionNameStore`); not in Phase 24 scope.
- **G-3 (`[ObservableProperty]` no-`null!` defaults)** — Phase 28 first concrete fix (`_contextModelBadgeColor`); not in Phase 24 scope.
- **Pricing fire-and-forget surfacing (`MainViewModel.cs:371–375`)** — Phase 27 PRICING-01. Phase 24 must not pre-empt.
- **Auto-prune of orphan custom session names** — Phase 26 RENAME-06 explicitly defers; not relevant to Phase 24.
- **Removing the existing `_dispatcherQueue?.TryEnqueue(RefreshSessionList)` null-conditional pattern at line 1032** — implicit follow-up after CD-01 lifecycle decision. Plan Phase mechanism: once `_dispatcherQueue` is non-null after refactor, drop `?.` to assert post-construction non-null.

### Reviewed Todos (not folded — explicitly deferred)

- `2026-05-07-m3-revert-contextmodelbadgecolor-default-to-gray.md` — Phase 28 (CLEANUP-02 / G-3 precedent).
- `2026-05-07-m1-delete-orphan-logoutrequestedmessage.md` — Phase 28 (CLEANUP-01).
- `2026-05-07-m2-localize-lastfetchrelativetime-strings.md` — Phase 27 (L10N-01).
- `2026-05-07-nits-v14-code-review-cleanups.md` — Phase 28 (CLEANUP-03).

</deferred>

---

*Phase: 24-Dispatcher-Foundation-Marshaling-Convention*
*Context gathered: 2026-05-08*

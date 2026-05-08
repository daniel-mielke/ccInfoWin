---
phase: 24
plan: 02
type: execute
wave: 2
depends_on:
  - "24-01"
files_modified:
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs
  - CCInfoWindows/CCInfoWindows/App.xaml.cs
  - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
  - CCInfoWindows.Tests/ViewModels/MainViewModelStatisticsTests.cs
  - CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
  - CCInfoWindows.Tests/ViewModels/SessionDisplayTooltipTests.cs
  - CCInfoWindows.Tests/ViewModels/SettingsLogoutMessageRoundtripTests.cs
autonomous: true
requirements:
  - DISPATCH-04

must_haves:
  truths:
    - "MainViewModel constructor takes IDispatcherQueue as constructor parameter (CD-01: constructor injection chosen for FakeDispatcherQueue ergonomics)"
    - "Receive(AuthStateChangedMessage) entire body runs inside _dispatcherQueue.TryEnqueue(...) — no HasThreadAccess shortcut (L-04)"
    - "Receive(SessionTimeoutChangedMessage) drops null-conditional `?.` from _dispatcherQueue (now non-null after constructor injection)"
    - "Line 1008 RefreshCommand.ExecuteAsync(null) replaced with explicit-discard `_ = RefreshCommand.ExecuteAsync(null);` plus PITFALLS C1-P1 inline comment"
    - "Line 318 RefreshIntervalChangedMessage lambda audited and wrapped in TryEnqueue if it mutates [ObservableProperty]"
    - "InitializeAsync line 308 first statement: `WeakReferenceMessenger.Default.UnregisterAll(this);` (CD-04 / C2-P3)"
    - "MainWindow.xaml.cs Receive(ThemeChangedMessage) and Receive(ResetWindowSizeMessage) marked [ThreadSafeReceive(reason)] per CD-05 #3"
    - "All existing MainViewModel tests pass after factory wiring update"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      provides: "Refactored MainViewModel with thread-safe Receive(AuthStateChangedMessage)"
      contains: "IDispatcherQueue _dispatcherQueue"
    - path: "CCInfoWindows/CCInfoWindows/App.xaml.cs"
      provides: "Updated MainViewModel factory passing IDispatcherQueue"
      contains: "GetRequiredService<IDispatcherQueue>"
  key_links:
    - from: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs Receive(AuthStateChangedMessage)"
      to: "_dispatcherQueue.TryEnqueue"
      via: "always-TryEnqueue wrapper (L-04 / C2-P1)"
      pattern: "_dispatcherQueue\\.TryEnqueue"
    - from: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs HandleAuthStateChangedCore"
      to: "Receive(AuthStateChangedMessage) body"
      via: "extracted private method called from inside TryEnqueue"
      pattern: "HandleAuthStateChangedCore"
---

<objective>
Phase 24 Wave 2: Fix C-1 (fire-and-forget swallow) and C-2 (off-thread UI mutation) in `MainViewModel.Receive(AuthStateChangedMessage)` in a single edit, plus all related cleanup mandated by CD-01..CD-05.

Purpose: Eliminate the documented v1.4 production regression risk where a stacked `Send → Receive` chain on a non-UI thread mutates `[ObservableProperty]` fields mid-update, plus the parallel risk where a post-login refresh exception is silently swallowed by a fire-and-forget Task. Single edit covers both per L-05.

Output:
- MainViewModel constructor accepts `IDispatcherQueue` (CD-01: constructor injection)
- MainViewModel `_dispatcherQueue` field changes type from `DispatcherQueue?` (line 69) to non-null `IDispatcherQueue _dispatcherQueue`
- `HandleAuthStateChangedCore(AuthStateChangedMessage)` extracted private method
- `Receive(AuthStateChangedMessage)` body wraps in `_dispatcherQueue.TryEnqueue(() => HandleAuthStateChangedCore(message))` (L-04)
- Line 1008 fire-and-forget gets explicit discard: `_ = RefreshCommand.ExecuteAsync(null);` + C1-P1 comment
- `InitializeAsync` line 308 first statement: `WeakReferenceMessenger.Default.UnregisterAll(this);` (CD-04)
- Line 1032 `_dispatcherQueue?.TryEnqueue(...)` → `_dispatcherQueue.TryEnqueue(...)` (drop `?.`)
- Line 318 `RefreshIntervalChangedMessage` lambda audited
- `MainWindow.xaml.cs` Receive methods marked `[ThreadSafeReceive(reason)]` (CD-05 #3 option b)
- App.xaml.cs MainViewModel factory updated
- Existing MainViewModel tests updated to construct VM with FakeDispatcherQueue
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/24-dispatcher-foundation-marshaling-convention/24-CONTEXT.md
@.planning/phases/24-dispatcher-foundation-marshaling-convention/24-01-dispatcher-adapter-PLAN.md
@CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
@CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs

<interfaces>
<!-- IDispatcherQueue (created in Plan 24-01): -->
```csharp
namespace CCInfoWindows.Services.Interfaces;
public interface IDispatcherQueue
{
    bool TryEnqueue(Action action);
    bool HasThreadAccess { get; }
}
```

<!-- ThreadSafeReceiveAttribute (created in Plan 24-01): -->
```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ThreadSafeReceiveAttribute : Attribute
{
    public string Reason { get; }
    public ThreadSafeReceiveAttribute(string reason) { /* throws if whitespace */ }
}
```

<!-- FakeDispatcherQueue (created in Plan 24-01): -->
```csharp
namespace CCInfoWindows.Tests.Helpers;
public sealed class FakeDispatcherQueue : IDispatcherQueue
{
    public bool ExecuteInline { get; set; } = true;
    public bool HasThreadAccess { get; set; } = true;
    public int InvocationCount { get; private set; }
    public bool TryEnqueue(Action action);
    public int Pump();
}
```

<!-- Current MainViewModel constructor signature (10 deps, factory at App.xaml.cs:164-174): -->
```csharp
public MainViewModel(
    ICredentialService credentialService,
    INavigationService navigationService,
    IClaudeApiService apiService,
    ISettingsService settingsService,
    IUsageHistoryService historyService,
    IJsonlService jsonlService,
    IPricingService pricingService,
    IUpdateService updateService,
    IWebViewBridge bridge,
    IBurnRateNotificationService burnRateNotificationService)
```

<!-- Current Receive(AuthStateChangedMessage) at MainViewModel.cs:997-1026 (the C-1/C-2 surface): -->
<!-- Mutates IsSessionExpired, HasApiError, _autoReauthAttempted, StatusMessage off-thread when called from non-UI Send sites in ClaudeApiService.cs:88, 184. -->

<!-- Existing test pattern from MainViewModelAuthFlowTests.cs CreateViewModel — used by 5+ test files. ALL must be updated to pass FakeDispatcherQueue as the new 11th parameter. -->
</interfaces>

<test_files_requiring_constructor_update>
<!-- All test files calling `new MainViewModel(...)` need a new FakeDispatcherQueue argument: -->
- CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs (CreateViewModel and CreateViewModelWithSuccessfulApi helpers)
- CCInfoWindows.Tests/ViewModels/MainViewModelStatisticsTests.cs
- CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
- CCInfoWindows.Tests/ViewModels/SessionDisplayTooltipTests.cs
- CCInfoWindows.Tests/ViewModels/SettingsLogoutMessageRoundtripTests.cs (if it constructs MainViewModel)
</test_files_requiring_constructor_update>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Refactor MainViewModel — constructor injection, always-TryEnqueue, UnregisterAll, line 1032 cleanup, line 1008 explicit discard, line 318 lambda audit</name>
  <files>
    CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs (full file — must understand existing structure)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs (Plan 24-01 output)
    - .planning/phases/24-dispatcher-foundation-marshaling-convention/24-CONTEXT.md (lines 49-65 for CD-01..CD-05)
    - .planning/research/PITFALLS.md sections C1-P1, C2-P1, C2-P2, C2-P3 (anchors)
  </read_first>
  <action>
    Apply seven discrete edits to `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` in a single pass. Preserve every existing comment, every `[ObservableProperty]`, every `[RelayCommand]`, every D-XX behavior comment verbatim.

    **Edit 1 — Field type change (around line 69):**
    Replace:
    ```csharp
    private DispatcherQueue? _dispatcherQueue;
    ```
    With:
    ```csharp
    private readonly IDispatcherQueue _dispatcherQueue;
    ```
    Remove the `using Microsoft.UI.Dispatching;` import ONLY if no other usage remains in the file (search before deleting — `DispatcherQueueTimer` at lines 66-67 still requires it, so keep the import).

    **Edit 2 — Constructor parameter add (around line 280-303):**
    Add `IDispatcherQueue dispatcherQueue` as the LAST parameter (after `IBurnRateNotificationService burnRateNotificationService`). Inside constructor body, add `_dispatcherQueue = dispatcherQueue;` BEFORE the `_updateService.UpdateAvailable += OnUpdateAvailable;` line. Reason: messenger registration at lines 301-302 happens before InitializeAsync; with constructor-injection the field is non-null from construction, eliminating the cold-path null risk (PITFALLS C2-P2).

    **Edit 3 — InitializeAsync first statement (around line 308):**
    Insert as FIRST line inside `InitializeAsync()` body, BEFORE `var dispatcherQueue = DispatcherQueue.GetForCurrentThread();`:
    ```csharp
            // CD-04 / PITFALLS C2-P3: prevent double-subscription if InitializeAsync is called twice.
            // Pairs with constructor-time Register calls at lines 301-302; we re-register below via lambda
            // overloads. Cheap insurance as Phases 25-27 add new IRecipient<> handlers.
            WeakReferenceMessenger.Default.UnregisterAll(this);
    ```
    Then immediately re-register the two interface-based recipients (since UnregisterAll wiped them):
    ```csharp
            WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);
            WeakReferenceMessenger.Default.Register<SessionTimeoutChangedMessage>(this);
    ```
    Remove the duplicate Register calls from the constructor (lines 301-302) so registration happens exactly once per InitializeAsync cycle. Add a brief constructor comment: `// Messenger registration happens in InitializeAsync (paired with UnregisterAll for re-init safety — PITFALLS C2-P3).`

    Then DELETE the now-redundant `var dispatcherQueue = DispatcherQueue.GetForCurrentThread(); _dispatcherQueue = dispatcherQueue;` lines (310-311). The local `dispatcherQueue` variable is reused at line 355 (`_dataUpdatedHandler = (s, e) => dispatcherQueue.TryEnqueue(RefreshSessionList);`) — replace that line 355 reference with `_dispatcherQueue.TryEnqueue(RefreshSessionList)` so the field handles it (interface call, same semantics).

    **Edit 4 — Line 318 RefreshIntervalChangedMessage lambda audit (CD-05 #4):**
    Locate the existing lambda registration:
    ```csharp
    WeakReferenceMessenger.Default.Register<RefreshIntervalChangedMessage>(this, (r, m) =>
    {
        ((MainViewModel)r).UpdateRefreshInterval(m.Value);
    });
    ```
    Read `UpdateRefreshInterval` method body. If it mutates any `[ObservableProperty]` field, restarts a `DispatcherQueueTimer`, or touches XAML state, wrap the body in `_dispatcherQueue.TryEnqueue`:
    ```csharp
    WeakReferenceMessenger.Default.Register<RefreshIntervalChangedMessage>(this, (r, m) =>
    {
        var vm = (MainViewModel)r;
        vm._dispatcherQueue.TryEnqueue(() => vm.UpdateRefreshInterval(m.Value));
    });
    ```
    If `UpdateRefreshInterval` is provably UI-thread-only (only sets `_refreshIntervalSeconds` and calls `_pollTimer?.Stop()/Start()`), add an inline comment: `// CD-05 #4 audit: UpdateRefreshInterval mutates _pollTimer + _refreshIntervalSeconds; DispatcherQueueTimer requires UI thread → wrap.` and STILL wrap (defensive). Reason: PITFALLS C2-P1 always-TryEnqueue rule.

    **Edit 5 — Receive(AuthStateChangedMessage) refactor (lines 997-1026, the C-1/C-2 surface):**
    Replace the entire method with:
    ```csharp
    public void Receive(AuthStateChangedMessage message)
    {
        // L-04 / PITFALLS C2-P1: always-TryEnqueue. ClaudeApiService Send sites at FetchUsageAsync:88
        // and TryMigrateOrgIdAsync:184 may run on the HttpClient continuation thread; off-thread
        // mutation of [ObservableProperty] fields below produces inconsistent mid-update state.
        _dispatcherQueue.TryEnqueue(() => HandleAuthStateChangedCore(message));
    }

    private void HandleAuthStateChangedCore(AuthStateChangedMessage message)
    {
        // D-03: post-login refresh — clear error flags, reset auto-reauth budget, refresh immediately.
        if (message.Value)
        {
            IsSessionExpired = false;
            HasApiError = false;
            _autoReauthAttempted = false;
            // CD-02 / PITFALLS C1-P1: explicit discard documents intentional fire-and-forget.
            // [RelayCommand] machinery already catches exceptions inside Refresh() and surfaces
            // them via HasApiError / ApiErrorMessage in PollUsageCoreAsync (lines 428-458).
            // Adding a try/catch at THIS call site would be dead code.
            _ = RefreshCommand.ExecuteAsync(null);
            return;
        }

        // D-01: first 401 in a session → auto-navigate to LoginView, do NOT open InfoBar.
        // NOTE: ClaudeApiService has two send sites for AuthStateChangedMessage(false)
        // (FetchUsageAsync:88 and TryMigrateOrgIdAsync:184). Stacked-401 edge case is accepted —
        // Receive(true) post-login clears IsSessionExpired so a stale flag resolves at next login.
        if (!_autoReauthAttempted)
        {
            _autoReauthAttempted = true;
            _navigationService.NavigateTo<LoginView>();
            return;
        }

        // Second 401 (and beyond): existing InfoBar fallback path (AUTH-02).
        IsSessionExpired = true;
        StatusMessage = "Session expired. Please re-login to continue.";
    }
    ```
    Preserve every D-01/D-02/D-03 comment verbatim. The only behavioral change is the wrapper + explicit discard at the RefreshCommand call.

    **Edit 6 — Receive(SessionTimeoutChangedMessage) null-conditional cleanup (line 1032):**
    Replace:
    ```csharp
    _dispatcherQueue?.TryEnqueue(RefreshSessionList);
    ```
    With:
    ```csharp
    _dispatcherQueue.TryEnqueue(RefreshSessionList);
    ```
    Add comment above: `// G-1 compliant: constructor-injected _dispatcherQueue is non-null. CD-05 #2 — implicit-default exemption (no [ThreadSafeReceive] needed).`

    **Edit 7 — OnUpdateAvailable (line 980-987) line 981 cleanup:**
    Replace:
    ```csharp
    var dispatcherQueue = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
    dispatcherQueue?.TryEnqueue(() =>
    {
    ```
    With:
    ```csharp
    _dispatcherQueue.TryEnqueue(() =>
    {
    ```
    (Field is now non-null and is the IDispatcherQueue interface, so the WinRT-typed local fallback is no longer needed.)

    **Note:** Do NOT touch `_pollTimer` / `_countdownTimer` (DispatcherQueueTimer types at lines 66-67) in this phase. They are out of scope; only the dispatcher abstraction surface for Send/Receive is in scope.

    **Note:** Do NOT touch the pricing fire-and-forget at lines 371-375 (CD-02 explicit deferral to Phase 27 PRICING-01).
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
  </verify>
  <acceptance_criteria>
    - `MainViewModel.cs` field declaration line contains exactly `private readonly IDispatcherQueue _dispatcherQueue;` (Grep -F).
    - Grep `MainViewModel.cs` for `DispatcherQueue?` returns 0 matches in the field-declaration region (lines 60-72) — only `DispatcherQueueTimer?` remains.
    - Constructor parameter list contains `IDispatcherQueue dispatcherQueue` as last parameter (Grep with multi-line context).
    - `InitializeAsync` first non-comment statement is `WeakReferenceMessenger.Default.UnregisterAll(this);` (Read lines 305-315).
    - Constructor at lines 280-303 NO LONGER contains `WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);` (moved to InitializeAsync; Grep with line-range).
    - `Receive(AuthStateChangedMessage)` body has exactly one statement: a TryEnqueue call (Grep multiline).
    - Method `HandleAuthStateChangedCore` exists as private method (Grep `private void HandleAuthStateChangedCore`).
    - `_ = RefreshCommand.ExecuteAsync(null);` appears literally inside `HandleAuthStateChangedCore` (Grep -F).
    - Grep for `_dispatcherQueue?.TryEnqueue` returns 0 matches across MainViewModel.cs (all `?.` removed — non-null after constructor injection).
    - Solution builds clean: `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0 with zero new warnings.
  </acceptance_criteria>
  <done>MainViewModel is now G-1 compliant: Receive(AuthStateChangedMessage) wraps body in TryEnqueue, RefreshCommand fire-and-forget is explicit, _dispatcherQueue is non-null and constructor-injected, line 318 lambda audited, InitializeAsync starts with UnregisterAll, line 1032 dropped its null-conditional. The build does NOT yet succeed end-to-end because App.xaml.cs factory and test files still pass the old 10-arg constructor — Tasks 2 and 3 fix that.</done>
</task>

<task type="auto">
  <name>Task 2: Update App.xaml.cs MainViewModel factory + apply [ThreadSafeReceive] to MainWindow handlers</name>
  <files>
    CCInfoWindows/CCInfoWindows/App.xaml.cs
    CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/App.xaml.cs (lines 137-178)
    - CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs (full file)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs (Plan 24-01 output)
  </read_first>
  <action>
    Two files.

    **File 1: `CCInfoWindows/CCInfoWindows/App.xaml.cs` MainViewModel factory (lines 164-174)**

    Add `IDispatcherQueue` as last constructor argument:
    ```csharp
            services.AddTransient<MainViewModel>(sp => new MainViewModel(
                sp.GetRequiredService<ICredentialService>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IClaudeApiService>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IUsageHistoryService>(),
                sp.GetRequiredService<IJsonlService>(),
                sp.GetRequiredService<IPricingService>(),
                sp.GetRequiredService<IUpdateService>(),
                sp.GetRequiredService<IWebViewBridge>(),
                sp.GetRequiredService<IBurnRateNotificationService>(),
                sp.GetRequiredService<IDispatcherQueue>()));
    ```

    **File 2: `CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs` (CD-05 #3 option b — scope G-1 to ViewModels/Services, exempt Window subclasses)**

    Add using directive at top:
    ```csharp
    using CCInfoWindows.Services.Interfaces;
    ```
    (Already present for INavigationService etc. — confirm.)

    Add `[ThreadSafeReceive(...)]` attributes to BOTH Receive methods (lines 53 and 66). Exact attribute (per CD-05 #3 recommendation):

    ```csharp
    [ThreadSafeReceive("Window receivers run on the UI thread that hosts the window — WinUI 3 Window construction and access is by-design UI-thread-only.")]
    public void Receive(ThemeChangedMessage message)
    {
        ...existing body unchanged...
    }

    [ThreadSafeReceive("Window receivers run on the UI thread that hosts the window — WinUI 3 Window construction and access is by-design UI-thread-only.")]
    public void Receive(ResetWindowSizeMessage message)
    {
        ...existing body unchanged...
    }
    ```

    Do NOT modify method bodies. Do NOT register additional handlers. Do NOT add an IDispatcherQueue dependency to MainWindow (this would inflate the Window-vs-ViewModel boundary unnecessarily; CD-05 #3 chose option b explicitly).
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
  </verify>
  <acceptance_criteria>
    - `App.xaml.cs` MainViewModel factory contains exactly 11 constructor arguments (Grep `sp.GetRequiredService<` count inside the factory block returns 11).
    - `App.xaml.cs` factory contains `sp.GetRequiredService<IDispatcherQueue>()` as the LAST argument before `));` (Read lines 164-176).
    - `MainWindow.xaml.cs` contains `[ThreadSafeReceive(` exactly twice (Grep -c with anchored pattern).
    - Each `[ThreadSafeReceive(...)]` reason is non-empty and explicitly mentions "Window" or "UI thread" (Grep with extended pattern).
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0 with zero new warnings.
    - The production project builds end-to-end (test project still has stale 10-arg constructor calls — Task 3 fixes those).
  </acceptance_criteria>
  <done>Production project compiles cleanly. DI factory wires IDispatcherQueue into MainViewModel. MainWindow handlers are explicitly attributed for the convention test (Plan 24-03) to recognize them as legitimate exemptions per CD-05 #3.</done>
</task>

<task type="auto">
  <name>Task 3: Update existing MainViewModel test files to construct with FakeDispatcherQueue</name>
  <files>
    CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
    CCInfoWindows.Tests/ViewModels/MainViewModelStatisticsTests.cs
    CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
    CCInfoWindows.Tests/ViewModels/SessionDisplayTooltipTests.cs
    CCInfoWindows.Tests/ViewModels/SettingsLogoutMessageRoundtripTests.cs
  </files>
  <read_first>
    - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs (full file — pattern source for `CreateViewModel` helper)
    - CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs (Plan 24-01 output)
    - Each of the 4 other test files in files_modified — count `new MainViewModel(...)` call sites
  </read_first>
  <action>
    For every test file that constructs `new MainViewModel(...)`:

    1. Add `using CCInfoWindows.Tests.Helpers;` at top of file if not present.
    2. Add `using CCInfoWindows.Services.Interfaces;` if not present.
    3. Locate every `new MainViewModel(...)` invocation. Add `new FakeDispatcherQueue()` as the 11th (last) constructor argument.

    **Do NOT alter any existing test assertions. Do NOT change mock setups. The added FakeDispatcherQueue defaults (ExecuteInline=true, HasThreadAccess=true) preserve the existing inline-execution semantics — every existing test passes without behavioral change.**

    **Special case for `SettingsLogoutMessageRoundtripTests.cs`:** if it doesn't construct MainViewModel directly, skip it. Verify by Grepping for `new MainViewModel(` first. If 0 matches, remove from edit list.

    **Special case for `MainViewModelAuthFlowTests.cs`:** there are TWO factory helpers (`CreateViewModel` lines 19-48 and `CreateViewModelWithSuccessfulApi` lines 94-126). Both need the new last argument. Pattern after edit:
    ```csharp
    var vm = new MainViewModel(
        credentialService.Object,
        navigationService.Object,
        apiService.Object,
        settingsService.Object,
        historyService.Object,
        jsonlService.Object,
        pricingService.Object,
        updateService.Object,
        bridge.Object,
        burnRate.Object,
        new FakeDispatcherQueue());
    ```

    For tests that need to assert dispatcher invocation (none currently — but possible in future), keep `FakeDispatcherQueue` as a captured local first, then pass it. For Phase 24 tests, `new FakeDispatcherQueue()` inline is sufficient.
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MainViewModel|FullyQualifiedName~SessionDisplayTooltip|FullyQualifiedName~SettingsLogoutMessageRoundtrip"</automated>
  </verify>
  <acceptance_criteria>
    - Solution builds end-to-end: `dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` exits 0.
    - Grep across test project: `new MainViewModel(` followed within next 12 lines by `new FakeDispatcherQueue()` — every match is paired (manual verification by reading each call site).
    - All MainViewModel-constructing tests pass — focused dotnet test command exits 0 (excludes the 13+2 documented pre-existing baselines per STATE.md, but those are in JsonlService and ClaudeApiService — not in MainViewModel-related test files).
    - No mock setup changes (existing Mock&lt;IClaudeApiService&gt;, Mock&lt;INavigationService&gt; etc. behave identically because FakeDispatcherQueue.ExecuteInline=true preserves synchronous test semantics).
    - Zero new compiler warnings.
  </acceptance_criteria>
  <done>All existing MainViewModel tests pass with the constructor-injected IDispatcherQueue. Phase 24 Wave 2 complete. The C-1/C-2 fix is in production code AND covered by the existing AuthFlow test suite (Receive_True_ClearsFlagsAndResetsAutoReauth at line 73 still asserts the post-login refresh path; Receive_FirstFalse_NavigatesToLoginView at line 50 still asserts the auto-nav path — both now run through TryEnqueue inline).</done>
</task>

</tasks>

<verification>
After all three tasks complete, run in three separate Bash calls (per CLAUDE.md strict no-chaining rule):

```bash
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
```

```bash
dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj
```

```bash
dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-build --filter "FullyQualifiedName!~JsonlServiceTests&FullyQualifiedName!~ClaudeApiServiceTests"
```

Expected: zero failures (excludes 13+2 documented pre-existing baselines).

Spot-check via Grep that the C-2 regression-prevention property holds:

```bash
# Receive(AuthStateChangedMessage) body must contain exactly one TryEnqueue call wrapping HandleAuthStateChangedCore
grep -n -A 5 "public void Receive(AuthStateChangedMessage" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
```

Expected: TryEnqueue call appears within first 5 lines of the method body.
</verification>

<success_criteria>
- DISPATCH-04 satisfied: `MainViewModel.Receive(AuthStateChangedMessage)` body wraps in `_dispatcherQueue.TryEnqueue(() => HandleAuthStateChangedCore(message))` (always-TryEnqueue per L-04).
- C-1 fixed: Line 1008 fire-and-forget is explicit (`_ = RefreshCommand.ExecuteAsync(null);`) with PITFALLS C1-P1 inline comment.
- C-2 fixed: Receive body never mutates UI state on the calling thread.
- C2-P2 fixed: `_dispatcherQueue` is non-null after construction (constructor injection).
- C2-P3 fixed: `InitializeAsync` starts with `UnregisterAll(this)`.
- CD-05 #3 fixed: MainWindow.xaml.cs Receive methods marked `[ThreadSafeReceive(reason)]`.
- CD-05 #4 fixed: Line 318 RefreshIntervalChangedMessage lambda audited and wrapped.
- All existing tests pass (modulo pre-existing baselines).
- Zero new compiler warnings.
- Pricing fire-and-forget at lines 371-375 UNTOUCHED (Phase 27 scope per CD-02).
</success_criteria>

<output>
After completion, create `.planning/phases/24-dispatcher-foundation-marshaling-convention/24-02-SUMMARY.md` listing:
- CD-01 decision: constructor injection chosen (over lazy-resolve helper) — rationale: FakeDispatcherQueue test ergonomics; non-null contract eliminates 4 sites of `?.` null-conditional clutter
- Lines edited in MainViewModel.cs (with before/after line numbers)
- Test files updated (count + list)
- Pre-flight grep results: 0 occurrences of `_dispatcherQueue?.` anywhere in MainViewModel.cs
- Carried forward to Plan 24-03: convention test must filter Window subclasses (CD-05 #3 option b) and accept `[ThreadSafeReceive(reason)]` as exemption
- Decision artifact: line 318 lambda WAS wrapped (defensive — UpdateRefreshInterval mutates _pollTimer state)
</output>
</content>
</invoke>
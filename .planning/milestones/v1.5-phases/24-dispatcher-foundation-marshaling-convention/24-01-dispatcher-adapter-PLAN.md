---
phase: 24
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs
  - CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs
  - CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs
  - CCInfoWindows/CCInfoWindows/App.xaml.cs
  - CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs
autonomous: true
requirements:
  - DISPATCH-01
  - DISPATCH-02
  - DISPATCH-03

must_haves:
  truths:
    - "IDispatcherQueue interface exists with TryEnqueue(Action) and HasThreadAccess matching IDispatcherTimer template shape"
    - "ThreadSafeReceiveAttribute exists, requires non-empty reason, throws ArgumentException when reason is whitespace"
    - "WinuiDispatcherQueueAdapter wraps Microsoft.UI.Dispatching.DispatcherQueue and is registered as singleton in DI"
    - "FakeDispatcherQueue executes actions inline by default with optional queued/manual-pump mode for tests"
    - "Solution builds with zero new compiler warnings"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs"
      provides: "IDispatcherQueue contract"
      contains: "bool TryEnqueue(Action"
    - path: "CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs"
      provides: "G-1 explicit-exemption marker"
      contains: "ThreadSafeReceiveAttribute"
    - path: "CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs"
      provides: "Production adapter wrapping DispatcherQueue.GetForCurrentThread()"
      contains: "WinuiDispatcherQueueAdapter"
    - path: "CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs"
      provides: "Headless test double with inline + queued execution modes"
      contains: "FakeDispatcherQueue"
  key_links:
    - from: "CCInfoWindows/CCInfoWindows/App.xaml.cs"
      to: "WinuiDispatcherQueueAdapter"
      via: "services.AddSingleton<IDispatcherQueue, WinuiDispatcherQueueAdapter>()"
      pattern: "AddSingleton<IDispatcherQueue"
    - from: "CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs"
      to: "IDispatcherQueue"
      via: "implements interface"
      pattern: "FakeDispatcherQueue\\s*:\\s*IDispatcherQueue"
---

<objective>
Phase 24 Wave 1: Establish the dispatcher abstraction foundation that Plans 24-02 and 24-03 depend on.

Purpose: Mirror the v1.4 IDispatcherTimer adapter precedent for DispatcherQueue. Produces a tested-against interface plus production adapter plus headless test double plus the [ThreadSafeReceive] attribute that Plan 24-03's convention test will key off.

Output:
- IDispatcherQueue.cs (interface, ~5 LOC)
- ThreadSafeReceiveAttribute.cs (attribute, ~15 LOC, requires non-empty reason per D-02)
- WinuiDispatcherQueueAdapter.cs (production wrapper, ~25 LOC)
- FakeDispatcherQueue.cs (test double in CCInfoWindows.Tests/Helpers/, supports inline + queued modes)
- App.xaml.cs DI registration
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
@CLAUDE.md

<interfaces>
<!-- Exact template to mirror for IDispatcherQueue. From CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherTimer.cs -->

```csharp
namespace CCInfoWindows.Services.Interfaces;

public interface IDispatcherTimer
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    event EventHandler<object> Tick;
    void Start();
    void Stop();
}
```

<!-- IDispatcherQueue target shape (locked L-01): -->
<!--
public interface IDispatcherQueue
{
    bool TryEnqueue(Action action);
    bool HasThreadAccess { get; }
}
-->

<!-- DI registration target site. From CCInfoWindows/CCInfoWindows/App.xaml.cs:137-178 -->

```csharp
private static IServiceProvider ConfigureServices()
{
    var services = new ServiceCollection();
    // Infrastructure
    services.AddSingleton<HttpClient>();

    // Singleton services
    services.AddSingleton<ISettingsService, SettingsService>();
    // ... etc
}
```

<!-- Microsoft.UI.Dispatching.DispatcherQueue surface used by adapter:
     - DispatcherQueue.GetForCurrentThread() : DispatcherQueue
     - DispatcherQueue.TryEnqueue(DispatcherQueueHandler) : bool
     - DispatcherQueue.HasThreadAccess : bool
-->
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create IDispatcherQueue interface and ThreadSafeReceiveAttribute</name>
  <files>
    CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs
    CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherTimer.cs (template — mirror its docblock + namespace shape exactly per L-09)
    - .planning/phases/24-dispatcher-foundation-marshaling-convention/24-CONTEXT.md (lines 180-194 for attribute sketch)
  </read_first>
  <action>
    Create exactly two files:

    **File 1: `CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs`** (per L-01)
    ```csharp
    namespace CCInfoWindows.Services.Interfaces;

    /// <summary>
    /// Abstraction over Microsoft.UI.Dispatching.DispatcherQueue that allows headless unit testing
    /// without a Windows App SDK UI context. Production code uses WinuiDispatcherQueueAdapter;
    /// tests supply FakeDispatcherQueue.
    ///
    /// G-1 convention (CLAUDE.md, MVVM Conventions): every IRecipient&lt;T&gt;.Receive(T) body that
    /// mutates [ObservableProperty] fields, calls INavigationService, or touches XAML controls
    /// MUST wrap the body in IDispatcherQueue.TryEnqueue. Always-TryEnqueue, no HasThreadAccess
    /// shortcut (PITFALLS C2-P1).
    /// </summary>
    public interface IDispatcherQueue
    {
        /// <summary>Enqueues the action to run on the dispatcher's thread. Returns false if the queue is shut down.</summary>
        bool TryEnqueue(Action action);

        /// <summary>True if the calling thread is the dispatcher's owning thread.</summary>
        bool HasThreadAccess { get; }
    }
    ```

    **File 2: `CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs`** (per D-02)
    ```csharp
    namespace CCInfoWindows.Services.Interfaces;

    /// <summary>
    /// Marks an IRecipient&lt;T&gt;.Receive(T) method as exempt from G-1 thread-marshaling rule.
    /// MessengerThreadingConventionTests asserts EITHER this attribute is present OR the method body
    /// calls IDispatcherQueue.TryEnqueue. Reason MUST be non-empty (mirrors [Obsolete("reason")] spirit).
    /// </summary>
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

    Do NOT add any other members. Do NOT add `[RequiresMarshal]` (D-03: implicit default). Namespace is `CCInfoWindows.Services.Interfaces` to match `IDispatcherTimer.cs`.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
  </verify>
  <acceptance_criteria>
    - File `CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs` exists.
    - File contains exactly the literal `bool TryEnqueue(Action action);` (verify via Grep).
    - File contains exactly the literal `bool HasThreadAccess { get; }` (verify via Grep).
    - File `CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs` exists.
    - File contains `public sealed class ThreadSafeReceiveAttribute : Attribute` (verify via Grep).
    - File contains `if (string.IsNullOrWhiteSpace(reason))` and `throw new ArgumentException` (verify via Grep).
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0 with zero new warnings (compare to baseline warning count before edits).
  </acceptance_criteria>
  <done>Interface and attribute compile clean. No DI wiring yet (Task 3). No production refactor yet (Plan 24-02).</done>
</task>

<task type="auto">
  <name>Task 2: Create WinuiDispatcherQueueAdapter and FakeDispatcherQueue test double</name>
  <files>
    CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs
    CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherTimerAdapter.cs (template — mirror class shape, sealed/internal modifier, namespace)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs (just created in Task 1)
    - CCInfoWindows.Tests/CCInfoWindows.Tests.csproj (confirm test project has access to CCInfoWindows project reference + InternalsVisibleTo CCInfoWindows.Tests is set in main csproj line 46-49)
  </read_first>
  <action>
    Create exactly two files.

    **File 1: `CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs`** (per L-02 + L-09)
    ```csharp
    using CCInfoWindows.Services.Interfaces;
    using Microsoft.UI.Dispatching;

    namespace CCInfoWindows.Services;

    /// <summary>
    /// Production adapter wrapping Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().
    /// Resolved once at construction in App.xaml.cs ConfigureServices (UI thread context guaranteed
    /// because OnLaunched runs on the UI thread). Singleton lifetime (L-02).
    /// </summary>
    internal sealed class WinuiDispatcherQueueAdapter : IDispatcherQueue
    {
        private readonly DispatcherQueue _inner;

        public WinuiDispatcherQueueAdapter()
        {
            // Must be constructed on the UI thread. App.OnLaunched satisfies this contract.
            _inner = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException(
                    "WinuiDispatcherQueueAdapter must be constructed on a thread that owns a DispatcherQueue. "
                    + "Ensure ConfigureServices runs from App.OnLaunched (UI thread).");
        }

        public bool TryEnqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return _inner.TryEnqueue(() => action());
        }

        public bool HasThreadAccess => _inner.HasThreadAccess;
    }
    ```

    **File 2: `CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs`** (per L-03)
    ```csharp
    using CCInfoWindows.Services.Interfaces;

    namespace CCInfoWindows.Tests.Helpers;

    /// <summary>
    /// Headless test double for IDispatcherQueue. Two modes:
    ///   - Inline (default): TryEnqueue executes the action immediately on the calling thread.
    ///     Mirrors single-threaded xUnit test execution and exposes off-thread bugs synchronously.
    ///   - Queued: actions are stored and run only when Pump() is called. Use for tests that
    ///     need to assert ordering or verify fire-and-forget timing.
    /// HasThreadAccess defaults to true (test thread "owns" the fake dispatcher); can be overridden
    /// via property setter to simulate off-thread Send/Receive paths.
    /// </summary>
    public sealed class FakeDispatcherQueue : IDispatcherQueue
    {
        private readonly Queue<Action> _queued = new();

        public bool ExecuteInline { get; set; } = true;
        public bool HasThreadAccess { get; set; } = true;
        public int InvocationCount { get; private set; }
        public IReadOnlyCollection<Action> PendingActions => _queued;

        public bool TryEnqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvocationCount++;
            if (ExecuteInline)
            {
                action();
                return true;
            }
            _queued.Enqueue(action);
            return true;
        }

        /// <summary>Drains queued actions in FIFO order. Only meaningful when ExecuteInline is false.</summary>
        public int Pump()
        {
            int count = 0;
            while (_queued.Count > 0)
            {
                _queued.Dequeue()();
                count++;
            }
            return count;
        }
    }
    ```

    Place file under `CCInfoWindows.Tests/Helpers/` (create directory if missing). Namespace `CCInfoWindows.Tests.Helpers`.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj</automated>
  </verify>
  <acceptance_criteria>
    - File `CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs` exists, contains `internal sealed class WinuiDispatcherQueueAdapter : IDispatcherQueue` (Grep).
    - File `CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs` contains `DispatcherQueue.GetForCurrentThread()` (Grep).
    - File `CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs` exists, contains `public sealed class FakeDispatcherQueue : IDispatcherQueue` (Grep).
    - File `CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs` contains both `ExecuteInline` and `Pump()` (Grep).
    - `dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` exits 0.
    - No new compiler warnings introduced (compare to baseline).
  </acceptance_criteria>
  <done>Production adapter and test fake compile. Test project references resolve via existing `<ProjectReference>` and `InternalsVisibleTo CCInfoWindows.Tests` (csproj line 46-49). Adapter is `internal sealed` to match `WinuiDispatcherTimerAdapter` precedent.</done>
</task>

<task type="auto">
  <name>Task 3: Register WinuiDispatcherQueueAdapter as singleton in App.xaml.cs DI</name>
  <files>
    CCInfoWindows/CCInfoWindows/App.xaml.cs
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/App.xaml.cs (lines 137-178 — ConfigureServices and MainViewModel factory)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs (Task 1 output)
    - CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs (Task 2 output)
  </read_first>
  <action>
    Edit `CCInfoWindows/CCInfoWindows/App.xaml.cs` ConfigureServices (around line 142). Insert ONE line registering the adapter as singleton, immediately after `services.AddSingleton<HttpClient>();`. Position justification (per CONTEXT.md Integration Points): after infrastructure (HttpClient), before service singletons. The constructor calls `DispatcherQueue.GetForCurrentThread()` — App.OnLaunched runs on UI thread, so eager construction is safe.

    Exact insertion (per L-02):
    ```csharp
            // Infrastructure
            services.AddSingleton<HttpClient>();
            services.AddSingleton<IDispatcherQueue, WinuiDispatcherQueueAdapter>();   // DISPATCH-02 (Phase 24, L-02)
    ```

    Add `using` directives at top of file if not already present:
    - `using CCInfoWindows.Services.Interfaces;` (likely already there for INavigationService etc.)
    - `using CCInfoWindows.Services;` (likely already there for service implementations)

    Do NOT modify the MainViewModel factory in this task. The constructor parameter wiring belongs to Plan 24-02 (CD-01 decision: constructor injection vs lazy-resolve).
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
  </verify>
  <acceptance_criteria>
    - `App.xaml.cs` contains the literal string `services.AddSingleton<IDispatcherQueue, WinuiDispatcherQueueAdapter>()` (Grep with -F).
    - The registration line appears AFTER `services.AddSingleton<HttpClient>();` and BEFORE `services.AddSingleton<ISettingsService` (Grep -n + line-number ordering check).
    - Solution builds clean: `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - Existing test suite still compiles: `dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` exits 0.
    - No MainViewModel factory changes in this task (the factory at lines 164-174 is identical to pre-edit).
  </acceptance_criteria>
  <done>IDispatcherQueue resolvable via DI as singleton. Plan 24-02 can now inject it into MainViewModel constructor.</done>
</task>

</tasks>

<verification>
After all three tasks complete:

```bash
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
```
Then in a separate call:
```bash
dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj
```
Then in a separate call:
```bash
dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-build --filter "FullyQualifiedName!~JsonlServiceTests&FullyQualifiedName!~ClaudeApiServiceTests"
```
(Excludes the 13 pre-existing JsonlServiceTests + 2 pre-existing ClaudeApiServiceTests failures documented as out-of-scope baselines in STATE.md.)

Existing test suite must remain green (no regression from new files / DI registration).
</verification>

<success_criteria>
- DISPATCH-01 satisfied: `IDispatcherQueue` interface exists with locked shape `{ bool TryEnqueue(Action); bool HasThreadAccess; }`.
- DISPATCH-02 satisfied: `WinuiDispatcherQueueAdapter` is singleton-registered in `App.xaml.cs` ConfigureServices.
- DISPATCH-03 satisfied: `FakeDispatcherQueue` exists in `CCInfoWindows.Tests/Helpers/` with inline + queued execution modes.
- `ThreadSafeReceiveAttribute` exists and enforces non-empty reason at construction (D-02).
- All existing tests pass except documented pre-existing baselines.
- Zero new compiler warnings.
- No MainViewModel changes (Plan 24-02 owns that surface).
</success_criteria>

<output>
After completion, create `.planning/phases/24-dispatcher-foundation-marshaling-convention/24-01-SUMMARY.md` listing:
- Files created (5 total)
- Files modified (1: App.xaml.cs)
- DI lifetime (singleton, eager construction in App.OnLaunched UI-thread context)
- Adapter LOC count vs IDispatcherTimer template (target: similar shape)
- Decision artifact: `internal sealed` modifier on WinuiDispatcherQueueAdapter (mirrors WinuiDispatcherTimerAdapter)
- Carried forward to Plan 24-02: constructor-injection vs lazy-resolve choice for MainViewModel._dispatcherQueue (CD-01)
</output>
</content>
</invoke>
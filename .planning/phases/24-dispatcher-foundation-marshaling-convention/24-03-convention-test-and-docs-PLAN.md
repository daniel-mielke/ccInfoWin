---
phase: 24
plan: 03
type: execute
wave: 3
depends_on:
  - "24-01"
  - "24-02"
files_modified:
  - CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs
  - CLAUDE.md
  - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
autonomous: true
requirements:
  - DISPATCH-05
  - DISPATCH-06

must_haves:
  truths:
    - "MessengerThreadingConventionTests xUnit class exists in CCInfoWindows.Tests/Convention/ and passes"
    - "Convention test enumerates every IRecipient<T>.Receive(T) method in production assembly via reflection, excluding Window subclasses (CD-05 #3 option b)"
    - "Convention test asserts EITHER [ThreadSafeReceive(reason)] with non-empty reason OR method body IL contains a callvirt to IDispatcherQueue.TryEnqueue (D-03)"
    - "Convention test fails with a clear message naming the offending method when a violator is introduced"
    - "CLAUDE.md MVVM Conventions section contains a G-1 paragraph documenting the always-TryEnqueue rule + ThreadSafeReceive escape hatch"
    - "CommunityToolkit.Mvvm package upgraded from 8.4.0 to 8.4.2"
    - "Microsoft.WindowsAppSDK package upgraded from 1.8.260209005 to 1.8.260416003"
    - "All existing tests still green after both NuGet bumps (no regression)"
  artifacts:
    - path: "CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs"
      provides: "G-1 enforcement xUnit test (DISPATCH-06)"
      contains: "MessengerThreadingConventionTests"
    - path: "CLAUDE.md"
      provides: "G-1 convention paragraph (DISPATCH-05)"
      contains: "G-1"
    - path: "CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj"
      provides: "Updated PackageReferences for L-08 patch bumps"
      contains: "8.4.2"
  key_links:
    - from: "CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs"
      to: "ThreadSafeReceiveAttribute (Plan 24-01)"
      via: "GetCustomAttribute<ThreadSafeReceiveAttribute>()"
      pattern: "GetCustomAttribute<ThreadSafeReceiveAttribute"
    - from: "CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs"
      to: "IDispatcherQueue.TryEnqueue (production callsite)"
      via: "MethodInfo.GetMethodBody().GetILAsByteArray() callvirt-token scan"
      pattern: "GetMethodBody"
---

<objective>
Phase 24 Wave 3: Land the G-1 enforcement infrastructure that makes the rule self-policing across Phases 25-27, plus the two locked NuGet patch bumps.

Purpose: Tier-1 (CLAUDE.md) + Tier-2 (xUnit reflection convention test) is the locked enforcement model per REQUIREMENTS.md "Out of Scope" (Roslyn analyzer deferred to v1.6+). Convention test must run alongside ResourceCoverageTests (v1.4 L10N precedent — same XDocument-based shape, swapped for IL-bytecode + attribute scan).

Output:
- `MessengerThreadingConventionTests.cs` xUnit class in `CCInfoWindows.Tests/Convention/` enforcing G-1 via reflection + IL scan
- `CLAUDE.md` MVVM Conventions section gains a G-1 paragraph (per L-06 placement: immediately after the existing `DispatcherQueue.TryEnqueue()` line)
- `CCInfoWindows.csproj` PackageReferences updated for `CommunityToolkit.Mvvm` 8.4.0→8.4.2 and `Microsoft.WindowsAppSDK` 1.8.260209005→1.8.260416003 (L-08)

Depends on Plans 24-01 (provides `ThreadSafeReceiveAttribute` + `IDispatcherQueue` types referenced by the test) and 24-02 (provides the actual G-1-compliant `MainViewModel.Receive(AuthStateChangedMessage)` that must pass the test on first run, plus the `[ThreadSafeReceive]`-decorated MainWindow handlers).
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
@CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj

<interfaces>
<!-- Convention test pseudo-code (CONTEXT.md lines 146-179, refined here): -->

<!-- Targets to enumerate (after Plan 24-02): -->
<!-- 1. MainViewModel.Receive(AuthStateChangedMessage) — wraps body in _dispatcherQueue.TryEnqueue → IL scan finds it -->
<!-- 2. MainViewModel.Receive(SessionTimeoutChangedMessage) — body is _dispatcherQueue.TryEnqueue(RefreshSessionList) → IL scan finds it -->
<!-- 3. MainWindow.Receive(ThemeChangedMessage) — has [ThreadSafeReceive(reason)] → attribute exempts -->
<!-- 4. MainWindow.Receive(ResetWindowSizeMessage) — has [ThreadSafeReceive(reason)] → attribute exempts -->

<!-- Exemption mechanism (D-03 — minimal-cost variant): -->
<!-- IL bytecode scan via MethodInfo.GetMethodBody().GetILAsByteArray() + Module.ResolveMember(token) -->
<!-- Look for callvirt or call opcodes (0x6F, 0x28) followed by 4-byte token resolving to a -->
<!-- MethodInfo whose DeclaringType implements IDispatcherQueue and whose Name is "TryEnqueue". -->

<!-- ResourceCoverageTests precedent shape (CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs): -->
<!-- - Class is parameterless, [Fact] methods on it -->
<!-- - Uses XDocument-based structural validation -->
<!-- - One test per assertion family — clear failure messages -->

<!-- L-06 CLAUDE.md target — current "MVVM Conventions" section content: -->
<!--
## MVVM Conventions

- Use `[ObservableProperty]` for bindable properties (generates PascalCase property from `_camelCase` field)
- Use `[RelayCommand]` for commands (generates `XxxCommand` from `Xxx` method)
- No code-behind logic in Views -- all logic in ViewModels
- Use `partial class` with source generators

## Async Patterns

- Always `async/await` -- never fire-and-forget
- Use `DispatcherQueue.TryEnqueue()` for UI thread marshaling
- `HttpClient` as singleton (registered in DI)
-->

<!-- G-1 paragraph lands as a NEW SECTION OR appended to MVVM Conventions per L-06. -->
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Author MessengerThreadingConventionTests.cs (DISPATCH-06)</name>
  <files>
    CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs
  </files>
  <read_first>
    - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs (precedent — convention-as-test shape, [Fact] structure, namespace, class style)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs (Plan 24-01 — interface used in IL scan target)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs (Plan 24-01 — attribute used in exemption check)
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs Receive methods (Plan 24-02 — must pass test on first run)
    - CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs (Plan 24-02 — has [ThreadSafeReceive(reason)] on both Receive overloads)
  </read_first>
  <action>
    Create `CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs` (create the `Convention/` directory if missing). Class must be public, parameterless, named exactly `MessengerThreadingConventionTests` (per D-04 locked class name).

    Full file content (D-03 minimal-cost variant: IL-bytecode scan):

    ```csharp
    using System.Reflection;
    using CCInfoWindows.Services.Interfaces;
    using CCInfoWindows.ViewModels;
    using CommunityToolkit.Mvvm.Messaging;
    using Microsoft.UI.Xaml;

    namespace CCInfoWindows.Tests.Convention;

    /// <summary>
    /// Phase 24 DISPATCH-06: enforces convention G-1 (CLAUDE.md MVVM Conventions).
    ///
    /// G-1: every IRecipient&lt;T&gt;.Receive(T) body that mutates [ObservableProperty] fields,
    /// calls INavigationService, or touches XAML controls MUST wrap the body in
    /// IDispatcherQueue.TryEnqueue. Always-TryEnqueue, no HasThreadAccess shortcut
    /// (PITFALLS C2-P1).
    ///
    /// Exemption: a method may be marked [ThreadSafeReceive("reason")] with a non-empty
    /// reason. Per D-02, the attribute constructor itself enforces non-empty reason —
    /// this test additionally asserts the attribute is reachable via reflection.
    ///
    /// Scope (CD-05 #3 option b): Window subclasses are excluded from the body-scan
    /// rule because they are by-construction UI-thread-bound. Window receivers must
    /// still carry [ThreadSafeReceive(reason)] to document the exemption explicitly.
    ///
    /// Mechanism (D-03 minimal-cost variant): IL-bytecode scan of method body looking
    /// for a call/callvirt opcode whose resolved member is IDispatcherQueue.TryEnqueue.
    /// Source-generator artifacts (e.g. CommunityToolkit messaging glue) are NOT
    /// IRecipient&lt;T&gt; implementations — they are filtered out by the IRecipient
    /// interface check.
    /// </summary>
    public class MessengerThreadingConventionTests
    {
        [Fact]
        public void All_IRecipient_Receive_Methods_Either_Marshal_Or_Are_ThreadSafeAttributed()
        {
            var assembly = typeof(MainViewModel).Assembly;

            var receivers = EnumerateReceiverMethods(assembly).ToList();

            Assert.NotEmpty(receivers);   // sanity: phase 24 inventory has 4 sites; future phases add more

            var violations = new List<string>();
            foreach (var (method, declaringType) in receivers)
            {
                var attr = method.GetCustomAttribute<ThreadSafeReceiveAttribute>();
                if (attr != null)
                {
                    if (string.IsNullOrWhiteSpace(attr.Reason))
                    {
                        violations.Add(
                            $"{declaringType.FullName}.{FormatSignature(method)} carries [ThreadSafeReceive] without a non-empty reason. " +
                            "Provide a justification string per D-02.");
                    }
                    continue;
                }

                if (declaringType.IsSubclassOf(typeof(Window)))
                {
                    violations.Add(
                        $"{declaringType.FullName}.{FormatSignature(method)} is a Window receiver but lacks [ThreadSafeReceive(reason)]. " +
                        "CD-05 #3: Window subclasses MUST carry an explicit exemption attribute.");
                    continue;
                }

                if (!BodyCallsTryEnqueue(method))
                {
                    violations.Add(
                        $"{declaringType.FullName}.{FormatSignature(method)} mutates UI state without IDispatcherQueue.TryEnqueue " +
                        "and lacks [ThreadSafeReceive(reason)]. See CLAUDE.md G-1.");
                }
            }

            Assert.True(
                violations.Count == 0,
                "G-1 convention violations found:\n  - " + string.Join("\n  - ", violations));
        }

        [Fact]
        public void ThreadSafeReceiveAttribute_RejectsEmptyReason_AtConstruction()
        {
            // D-02 spot check: attribute itself enforces non-empty reason.
            // (Belt-and-suspenders — the All_... test above also catches whitespace reasons via reflection,
            // but if a developer somehow tries to bypass the constructor at runtime this test catches it.)
            Assert.Throws<ArgumentException>(() => new ThreadSafeReceiveAttribute(""));
            Assert.Throws<ArgumentException>(() => new ThreadSafeReceiveAttribute("   "));
            var ok = new ThreadSafeReceiveAttribute("documented reason");
            Assert.Equal("documented reason", ok.Reason);
        }

        // -- helpers --

        private static IEnumerable<(MethodInfo Method, Type DeclaringType)> EnumerateReceiverMethods(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;

                var recipientInterfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRecipient<>));

                foreach (var iface in recipientInterfaces)
                {
                    var messageType = iface.GetGenericArguments()[0];
                    var method = type.GetMethod(
                        nameof(IRecipient<object>.Receive),
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                        binder: null,
                        types: [messageType],
                        modifiers: null);

                    if (method != null)
                    {
                        yield return (method, type);
                    }
                }
            }
        }

        private static string FormatSignature(MethodInfo method)
        {
            var paramType = method.GetParameters()[0].ParameterType.Name;
            return $"Receive({paramType})";
        }

        /// <summary>
        /// IL-bytecode scan: walks the method body looking for a call/callvirt
        /// opcode whose 4-byte metadata token resolves to a MethodInfo declared on
        /// IDispatcherQueue (or a type implementing it) with name "TryEnqueue".
        ///
        /// Note: a positive match here only proves the call exists somewhere in the
        /// method body, not that the entire body is wrapped. G-1 is intent-driven —
        /// reviewer + author are responsible for full-body wrapping; this test catches
        /// the common case where TryEnqueue is missing entirely.
        /// </summary>
        private static bool BodyCallsTryEnqueue(MethodInfo method)
        {
            var body = method.GetMethodBody();
            if (body == null) return false;
            var il = body.GetILAsByteArray();
            if (il == null || il.Length == 0) return false;

            var module = method.Module;
            var genericMethodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var genericTypeArgs = method.DeclaringType?.IsGenericType == true
                ? method.DeclaringType.GetGenericArguments()
                : null;

            for (int i = 0; i + 4 < il.Length; i++)
            {
                var opcode = il[i];
                // 0x28 = call, 0x6F = callvirt, 0x73 = newobj
                if (opcode != 0x28 && opcode != 0x6F) continue;

                int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                MethodBase? resolved;
                try
                {
                    resolved = module.ResolveMethod(token, genericTypeArgs, genericMethodArgs);
                }
                catch
                {
                    continue;   // tokens for varargs / non-method members; ignore
                }

                if (resolved is MethodInfo mi
                    && mi.Name == nameof(IDispatcherQueue.TryEnqueue)
                    && (mi.DeclaringType == typeof(IDispatcherQueue)
                        || (mi.DeclaringType?.GetInterfaces().Contains(typeof(IDispatcherQueue)) ?? false)))
                {
                    return true;
                }
            }
            return false;
        }
    }
    ```

    Place under `CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs`. The `Convention/` directory is new — create it (xUnit auto-discovers tests regardless of folder).
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MessengerThreadingConventionTests"</automated>
  </verify>
  <acceptance_criteria>
    - File `CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs` exists.
    - File contains `public class MessengerThreadingConventionTests` (Grep -F).
    - File contains `BodyCallsTryEnqueue` private method (Grep -F).
    - File contains `EnumerateReceiverMethods` private method (Grep -F).
    - File contains exactly two `[Fact]` attributes (Grep -c).
    - `dotnet test ... --filter "FullyQualifiedName~MessengerThreadingConventionTests"` exits 0 — both tests PASS on first run because:
      * MainViewModel.Receive(AuthStateChangedMessage) body calls TryEnqueue (Plan 24-02 Edit 5).
      * MainViewModel.Receive(SessionTimeoutChangedMessage) body calls TryEnqueue (Plan 24-02 Edit 6).
      * MainWindow.Receive(ThemeChangedMessage) has `[ThreadSafeReceive(...)]` (Plan 24-02 Task 2).
      * MainWindow.Receive(ResetWindowSizeMessage) has `[ThreadSafeReceive(...)]` (Plan 24-02 Task 2).
    - Test failure message format includes the offending type's `FullName` and the formatted signature (manual check by reading test source).
    - Sanity assertion holds: at least 4 receivers found in the assembly (`Assert.NotEmpty(receivers)`).
  </acceptance_criteria>
  <done>G-1 is now self-enforcing. Adding a new `IRecipient<>` handler in Phases 25-27 that forgets `_dispatcherQueue.TryEnqueue` will fail this test before merge. The test runs in seconds (reflection + IL scan, no DI bootstrap, no UI thread).</done>
</task>

<task type="auto">
  <name>Task 2: Document G-1 convention in CLAUDE.md (DISPATCH-05)</name>
  <files>
    CLAUDE.md
  </files>
  <read_first>
    - CLAUDE.md (full file — must understand existing MVVM Conventions and Async Patterns sections; G-1 paragraph lands per L-06 anchor)
    - .planning/phases/24-dispatcher-foundation-marshaling-convention/24-CONTEXT.md lines 195-197 (G-1 paragraph sketch)
  </read_first>
  <action>
    Edit `CLAUDE.md`. Locate the existing `## MVVM Conventions` section (it currently contains 4 bullets). Append a new bullet immediately after the last one (`Use partial class with source generators`):

    ```markdown
    - **G-1 (Messenger receive thread-marshaling)** — Every `IRecipient<T>.Receive(T)` method body that mutates `[ObservableProperty]` fields, calls `INavigationService`, or touches XAML controls MUST wrap the body in `IDispatcherQueue.TryEnqueue(() => HandleCore(...))`. Always-TryEnqueue is the rule — NEVER use the `if (!HasThreadAccess) ... else ...` shortcut, because recursive `Send → Receive` chains on the UI thread execute synchronously inside the parent's stack frame and produce mid-update inconsistent state. **Exception:** mark a method `[ThreadSafeReceive("specific reason proving UI-thread-only")]` and supply a non-empty reason — `MessengerThreadingConventionTests` enforces both branches. Window subclasses are exempt from the body-scan rule (they are by-construction UI-thread-bound) but MUST still carry `[ThreadSafeReceive(reason)]` to document the exemption. **Cross-VM communication priority:** direct DI > singleton-service .NET event > `WeakReferenceMessenger`. Reason: D-13 hotfix lesson — `WeakReferenceMessenger` + `AddTransient` recipients silently GC-drop, breaking exactly-once flows like logout / save-on-close.
    ```

    Do NOT edit any other section. Do NOT remove the existing `Use DispatcherQueue.TryEnqueue() for UI thread marshaling` bullet under `## Async Patterns` (it remains the informal general guidance; G-1 is the formal normative rule for IRecipient).

    Cite the convention test by name and the D-13 lesson by name so future readers can grep for both.
  </action>
  <verify>
    <automated>grep -n "G-1" CLAUDE.md</automated>
  </verify>
  <acceptance_criteria>
    - `CLAUDE.md` contains the literal string `G-1 (Messenger receive thread-marshaling)` (Grep -F).
    - `CLAUDE.md` contains the literal string `[ThreadSafeReceive(` somewhere in the G-1 paragraph (Grep -F).
    - `CLAUDE.md` contains the literal string `MessengerThreadingConventionTests` (Grep -F).
    - `CLAUDE.md` contains the literal string `D-13` in the G-1 paragraph (Grep -F).
    - The new bullet appears AFTER `Use partial class with source generators` and BEFORE the next `## ` markdown header (Read with line range).
    - The existing `## Async Patterns` section is UNCHANGED (Grep returns same line count for that section as before edit).
  </acceptance_criteria>
  <done>G-1 has Tier-1 documentation (CLAUDE.md) and Tier-2 enforcement (Plan 24-03 Task 1). Phase 25-27 planners and executors will see G-1 every time they read CLAUDE.md before authoring new IRecipient handlers.</done>
</task>

<task type="auto">
  <name>Task 3: Bump CommunityToolkit.Mvvm and Microsoft.WindowsAppSDK PackageReferences (L-08)</name>
  <files>
    CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj (lines 30-39 — current PackageReferences)
    - .planning/phases/24-dispatcher-foundation-marshaling-convention/24-CONTEXT.md L-08 (locked target versions)
  </read_first>
  <action>
    Edit `CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`. Update exactly two `<PackageReference>` lines:

    Before:
    ```xml
        <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260209005" />
        ...
        <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    ```

    After (per L-08):
    ```xml
        <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260416003" />
        ...
        <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    ```

    Do NOT change any other PackageReference. Do NOT update `Microsoft.Windows.SDK.BuildTools` (line 34 — out of scope).
    Do NOT change PackageReferences in `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` (the test project doesn't reference these two packages directly; they flow transitively).

    After edit, run `dotnet restore` once before build (NuGet patch resolution).
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
  </verify>
  <acceptance_criteria>
    - `CCInfoWindows.csproj` contains exactly the literal `Version="8.4.2"` on the `CommunityToolkit.Mvvm` PackageReference line (Grep -F + line context).
    - `CCInfoWindows.csproj` contains exactly the literal `Version="1.8.260416003"` on the `Microsoft.WindowsAppSDK` PackageReference line (Grep -F + line context).
    - Grep -F `8.4.0` returns 0 matches in `CCInfoWindows.csproj` (old version fully replaced).
    - Grep -F `1.8.260209005` returns 0 matches in `CCInfoWindows.csproj` (old version fully replaced).
    - Production project builds clean: `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0 with zero new compiler warnings.
    - Test project builds clean: `dotnet build CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` exits 0 (no transitive incompatibility).
    - Full test suite (excluding documented pre-existing baselines) passes: `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-build --filter "FullyQualifiedName!~JsonlServiceTests&FullyQualifiedName!~ClaudeApiServiceTests"` exits 0.
    - `MessengerThreadingConventionTests` (Task 1) still passes after the bumps.
  </acceptance_criteria>
  <done>NuGet patch bumps shipped. Phase 24 ROADMAP success criterion #5 satisfied. Both packages on latest 1.8.x / 8.4.x patch as locked in L-08.</done>
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

Expected:
- All builds exit 0 with zero new compiler warnings (compare to pre-Phase-24 baseline).
- All tests pass except the 13 pre-existing `JsonlServiceTests` + 2 pre-existing `ClaudeApiServiceTests` documented in STATE.md.
- `MessengerThreadingConventionTests.All_IRecipient_Receive_Methods_Either_Marshal_Or_Are_ThreadSafeAttributed` PASSES (this is the true G-1 enforcement verification — if this fails, Plans 24-01 and 24-02 had a regression).
- `MessengerThreadingConventionTests.ThreadSafeReceiveAttribute_RejectsEmptyReason_AtConstruction` PASSES.

Targeted spot check that G-1 is documented:
```bash
grep -n -A 0 "G-1" CLAUDE.md
```
Expected: at least one match in the MVVM Conventions section.

Targeted spot check that NuGet bumps landed:
```bash
grep -n "Version=" CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
```
Expected: `8.4.2` and `1.8.260416003` both visible.
</verification>

<success_criteria>
- DISPATCH-05 satisfied: G-1 paragraph documented in CLAUDE.md MVVM Conventions section, citing `[ThreadSafeReceive]`, `MessengerThreadingConventionTests`, and D-13 lesson.
- DISPATCH-06 satisfied: `MessengerThreadingConventionTests` xUnit class exists and PASSES on first run (covers all 4 inventoried receivers from Plan 24-02; future Phases 25-27 receivers will be auto-checked).
- L-08 NuGet patch bumps landed: `CommunityToolkit.Mvvm` → 8.4.2, `Microsoft.WindowsAppSDK` → 1.8.260416003.
- Full test suite green (modulo documented pre-existing baselines).
- Zero new compiler warnings.
- Phase 24 ROADMAP success criteria #1-#5 ALL met across the three plans.
</success_criteria>

<output>
After completion, create `.planning/phases/24-dispatcher-foundation-marshaling-convention/24-03-SUMMARY.md` listing:
- D-03 mechanism chosen: IL-bytecode scan via `MethodInfo.GetMethodBody().GetILAsByteArray()` + `Module.ResolveMethod` (rationale: zero new NuGet packages — Mono.Cecil avoided per research/SUMMARY.md; native System.Reflection.Metadata token resolution sufficient for callvirt/call opcodes 0x28 / 0x6F)
- 4 receiver sites enumerated and their disposition (TryEnqueue body / `[ThreadSafeReceive(reason)]` attribute)
- G-1 paragraph word count and placement (CLAUDE.md MVVM Conventions, after `partial class` bullet)
- NuGet bump diff (before/after for both packages)
- Test runtime: convention test should complete in <500ms (no I/O, no DI bootstrap)
- Carried forward: future phases 25-28 must respect G-1 — `MessengerThreadingConventionTests` will fail loudly otherwise
- Phase 24 declared complete; next: `/gsd-execute-phase 24` then `/gsd-plan-phase 25`
</output>
</content>
</invoke>
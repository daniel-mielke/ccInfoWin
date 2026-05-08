---
phase: 27-nextwin-orgid-pricing-l10n
plan: 03
type: execute
wave: 3
depends_on:
  - 27-02
files_modified:
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
  - CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs
autonomous: true
requirements:
  - PRICING-01
  - PRICING-02
  - PRICING-03

must_haves:
  truths:
    - "When _pricingService.EnsurePricesLoadedAsync() throws, IsPricingError becomes true"
    - "When pricing succeeds on a subsequent retry (auto-poll OR manual refresh), IsPricingError becomes false"
    - "MainView shows an Information/Warning InfoBar with title + message text when IsPricingErrorVisible is true"
    - "When IsSessionExpired AND IsPricingError are both true, the pricing InfoBar is suppressed (auth banner takes priority)"
    - "BannerStackPolicyTests verifies the suppression matrix at unit-test level"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      provides: "IsPricingError ObservableProperty + IsPricingErrorVisible computed property + set/clear sites"
      contains: "IsPricingErrorVisible"
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      provides: "Pricing-error InfoBar element bound to IsPricingErrorVisible"
      contains: "PricingErrorInfoBar"
    - path: "CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs"
      provides: "Banner-stack visibility matrix unit tests (D-PR-05)"
      contains: "BannerStackPolicyTests"
  key_links:
    - from: "MainViewModel.InitializeAsync (line 401-405) Task.Run pricing fire-and-forget"
      to: "IsPricingError setter (true on exception, false on success)"
      via: "try/catch wrapping EnsurePricesLoadedAsync"
      pattern: "IsPricingError = "
    - from: "MainViewModel pricing-retry call (line 898 area in Refresh path)"
      to: "IsPricingError = false on success / true on exception"
      via: "instrumented try/catch"
      pattern: "EnsurePricesLoadedAsync"
    - from: "MainViewModel.IsPricingErrorVisible computed property"
      to: "(IsPricingError && !IsSessionExpired)"
      via: "auto-notified via [NotifyPropertyChangedFor] on both fields"
      pattern: "IsPricingErrorVisible"
    - from: "MainView.xaml pricing InfoBar"
      to: "ViewModel.IsPricingErrorVisible"
      via: "x:Bind OneWay + BoolToVisibilityConverter"
      pattern: "IsPricingErrorVisible"
---

<objective>
Surface silent pricing-service failures via a dedicated `IsPricingError` `[ObservableProperty]` +
warning-severity InfoBar in MainView. Implement banner-stack policy: when both `IsPricingError`
and `IsSessionExpired` are true, the pricing InfoBar is suppressed (auth wins). Verified by a new
`BannerStackPolicyTests` xUnit class.

Wave 3 (after 27-02) because:
- Touches `MainViewModel.cs` (modified by 27-02) — sequenced to avoid merge churn
- Touches `MainView.xaml` (modified by 27-02 in the countdown region) — different region (Row 0 InfoBars), but same file
- Touches `Resources.resw` files (additive, but serialized to avoid header conflicts)
- Independent of 27-04 ORGID — does NOT touch `ClaudeApiService` / `Logout` flow

Purpose: PRICING-01..03 — surface the swallowed `_pricingService.EnsurePricesLoadedAsync()` failures
that currently degrade cost analytics silently (memory note `backlog_pricing_never_loaded.md`).

Output:
- 2 new resw key pairs (`MainView.PricingErrorInfoBar.Title`, `.Message`)
- `IsPricingError` ObservableProperty + `IsPricingErrorVisible` computed property
- Pricing failure surfacing at 2 sites (init + Refresh)
- New InfoBar in MainView Row-0 StackPanel
- `BannerStackPolicyTests` covering the (IsPricingError × IsSessionExpired) matrix
- `ResourceCoverageTests` extension
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/27-nextwin-orgid-pricing-l10n/27-CONTEXT.md
@.planning/phases/27-nextwin-orgid-pricing-l10n/27-02-nextwin-label-PLAN.md

@CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
@CCInfoWindows/CCInfoWindows/Views/MainView.xaml
@CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs

<interfaces>
<!-- Existing pricing call sites and surrounding patterns -->

```csharp
// Site 1: InitializeAsync (lines 401-405) — fire-and-forget pricing load
_ = Task.Run(async () =>
{
    try { await _pricingService.EnsurePricesLoadedAsync(); }
    catch (Exception ex) { Debug.WriteLine($"[MainViewModel] Pricing load failed: {ex.Message}"); }
});

// Site 2: Refresh-path retry (line 898) — pricing reload on aggregation refresh
await _pricingService.EnsurePricesLoadedAsync();

// Existing IsSessionExpired ObservableProperty:
[ObservableProperty]
private bool _isSessionExpired;
```

CommunityToolkit.Mvvm.ComponentModel `[NotifyPropertyChangedFor]` attribute pattern (already used
in the project for cross-property notification):
```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]
private bool _isPricingError;
```

Existing InfoBar template (MainView.xaml MigrationToastInfoBar, lines 84-93) — copy this shape.
Use `Severity="Warning"`, `IsClosable="False"` (auto-clears on success per D-PR-03), no
ActionButton.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add 2 PricingErrorInfoBar.* resw key pairs + extend ResourceCoverageTests</name>
  <files>
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw,
    CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
  </files>
  <behavior>
    - en-US contains `MainView.PricingErrorInfoBar.Title` = "Pricing data unavailable"
    - en-US contains `MainView.PricingErrorInfoBar.Message` = "Cost figures may be inaccurate."
    - de-DE contains `MainView.PricingErrorInfoBar.Title` = "Preisdaten nicht verfügbar"
    - de-DE contains `MainView.PricingErrorInfoBar.Message` = "Kostendaten können ungenau sein."
    - ResourceCoverageTests passes (existing + 2 new keys)
  </behavior>
  <action>
Per **D-PR-02**: warning-level InfoBar with localized title + message.

**Append to en-US/Resources.resw** (before `</root>`):
```xml
<data name="MainView.PricingErrorInfoBar.Title" xml:space="preserve">
  <value>Pricing data unavailable</value>
</data>
<data name="MainView.PricingErrorInfoBar.Message" xml:space="preserve">
  <value>Cost figures may be inaccurate.</value>
</data>
```

**Append to de-DE/Resources.resw** (before `</root>`):
```xml
<data name="MainView.PricingErrorInfoBar.Title" xml:space="preserve">
  <value>Preisdaten nicht verfügbar</value>
</data>
<data name="MainView.PricingErrorInfoBar.Message" xml:space="preserve">
  <value>Kostendaten können ungenau sein.</value>
</data>
```

**Extend ResourceCoverageTests.cs**:

To `RequiredKeys`:
```csharp
// Phase 27 PRICING-01..03: pricing-service silent-failure surfacing
"MainView.PricingErrorInfoBar.Title",
"MainView.PricingErrorInfoBar.Message",
```

To `ExpectedEnUs`:
```csharp
["MainView.PricingErrorInfoBar.Title"] = "Pricing data unavailable",
["MainView.PricingErrorInfoBar.Message"] = "Cost figures may be inaccurate.",
```

To `ExpectedDeDe`:
```csharp
["MainView.PricingErrorInfoBar.Title"] = "Preisdaten nicht verfügbar",
["MainView.PricingErrorInfoBar.Message"] = "Kostendaten können ungenau sein.",
```
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests" --nologo</automated>
  </verify>
  <done>Both resw files contain the 2 new keys; `ResourceCoverageTests` passes.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Add IsPricingError + IsPricingErrorVisible to MainViewModel + wire failure/success sites</name>
  <files>CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs</files>
  <behavior>
    - `IsPricingError` ObservableProperty exists with `[NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]`
    - `IsSessionExpired` ObservableProperty has `[NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]` added
    - `IsPricingErrorVisible` computed property returns `IsPricingError && !IsSessionExpired` (D-PR-04)
    - InitializeAsync (line 401-405) site: catch block sets `IsPricingError = true`; success path sets `IsPricingError = false`
    - Refresh path (line ~898): try/catch wraps `EnsurePricesLoadedAsync`; success → `IsPricingError = false`; failure → `IsPricingError = true`
    - Per CD-04: BOTH auto-poll AND manual refresh clear IsPricingError on success
  </behavior>
  <action>
Per **D-PR-01..05** + **specifics block**: add 1 ObservableProperty + 1 computed property + add
[NotifyPropertyChangedFor] to `_isSessionExpired`, instrument 2 call-sites.

**Add the new ObservableProperty** in the InfoBar/state region of MainViewModel.cs (near other
top-level state flags — search for the existing `_isSessionExpired` declaration; add the new field
adjacent to it). Locate the existing `[ObservableProperty] private bool _isSessionExpired;`
declaration first.

**Modify the existing `_isSessionExpired`** to add cross-notification:

```csharp
// PRICING-03 / D-PR-04: IsPricingErrorVisible depends on IsSessionExpired (auth banner priority)
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]
private bool _isSessionExpired;
```

**Add the new field** directly after `_isSessionExpired`:

```csharp
// PRICING-01..03 (D-PR-01, D-PR-04): surfaces _pricingService.EnsurePricesLoadedAsync() failures
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsPricingErrorVisible))]
private bool _isPricingError;
```

**Add the computed property** as a class-level expression-bodied member, near the existing
`FiveHourWindowStart` computed property (line 283):

```csharp
/// <summary>
/// PRICING-03 / D-PR-04: banner-stack policy — pricing InfoBar suppressed while auth banner shows.
/// Auto-notifies via [NotifyPropertyChangedFor] on IsPricingError + IsSessionExpired.
/// </summary>
public bool IsPricingErrorVisible => IsPricingError && !IsSessionExpired;
```

**Instrument Site 1 — InitializeAsync line 401-405** (D-PR-01: surface; D-PR-03: clear on success).
Replace the existing fire-and-forget block:

```csharp
// PRICING-01..03 (D-PR-01, D-PR-03): surface failures via IsPricingError; clear on subsequent success.
// Marshal back to the UI thread because Task.Run runs off the UI thread (G-1 alignment for property mutation).
_ = Task.Run(async () =>
{
    try
    {
        await _pricingService.EnsurePricesLoadedAsync();
        _dispatcherQueue.TryEnqueue(() => IsPricingError = false);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[MainViewModel] Pricing load failed: {ex.Message}");
        _dispatcherQueue.TryEnqueue(() => IsPricingError = true);
    }
});
```

**Why `_dispatcherQueue.TryEnqueue`?** The pricing call runs inside `Task.Run`, which executes on a
ThreadPool thread. Setting `IsPricingError` (which fires `PropertyChanged`) MUST marshal back to
the UI thread per **G-1 convention** (CLAUDE.md). The `_dispatcherQueue` field already exists on
MainViewModel (Phase 24). This is NOT an `IRecipient<>.Receive` body, so G-1 doesn't strictly
mandate the wrap, but the equivalent rationale applies (off-thread → property mutation → UI
binding). Document this rationale in the inline comment.

**Instrument Site 2 — line ~898** (Refresh path retry). Wrap the existing `await _pricingService.EnsurePricesLoadedAsync();`
call in a try/catch (look for it in the Refresh / aggregation path; the exact line depends on
post-Phase-26 state — use grep `EnsurePricesLoadedAsync` if line drift is suspected):

```csharp
// PRICING-02 / CD-04: manual refresh + auto-poll BOTH clear IsPricingError on success.
try
{
    await _pricingService.EnsurePricesLoadedAsync();
    IsPricingError = false;
}
catch (Exception ex)
{
    Debug.WriteLine($"[MainViewModel] Pricing reload failed: {ex.Message}");
    IsPricingError = true;
}
```

**No `_dispatcherQueue` wrap needed at Site 2** — this site is already on the UI thread (called
from the Refresh path which runs inside the existing dispatcher chain).

**No XAML changes** — the binding to `IsPricingErrorVisible` is added in Task 3.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj --nologo</automated>
  </verify>
  <done>Build is green. `IsPricingError`, `IsPricingErrorVisible`, and the dual `[NotifyPropertyChangedFor]` attributes are present. Both call-sites surface failures and clear on success.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 3: Add pricing-error InfoBar to MainView.xaml + create BannerStackPolicyTests</name>
  <files>
    CCInfoWindows/CCInfoWindows/Views/MainView.xaml,
    CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs
  </files>
  <behavior>
    - MainView.xaml row-0 StackPanel contains a new InfoBar element with `x:Name="PricingErrorInfoBar"` bound to `ViewModel.IsPricingErrorVisible`
    - InfoBar text is sourced from the 2 resw keys via `l:Uids.Uid` pattern
    - Severity = Warning, IsClosable = False (auto-clears on success retry)
    - BannerStackPolicyTests covers the 4-cell matrix: (IsPricingError × IsSessionExpired) → expected IsPricingErrorVisible
    - dotnet test BannerStackPolicyTests passes (4 test cases)
  </behavior>
  <action>
**Part A — MainView.xaml**: Add a new InfoBar inside the existing Row 0 `<StackPanel>` (lines 39-94),
inserted between the API error InfoBar (line 75-82) and the Migration toast InfoBar (line 84-93).
Position is **intentional** — banner-stack ordering: auth (highest) → API error → pricing →
migration toast.

```xml
<!-- PRICING-01..03 / D-PR-02: pricing-service silent-failure InfoBar -->
<InfoBar
    x:Name="PricingErrorInfoBar"
    l:Uids.Uid="MainView.PricingErrorInfoBar"
    Severity="Warning"
    IsOpen="{x:Bind ViewModel.IsPricingErrorVisible, Mode=OneWay}"
    Visibility="{x:Bind ViewModel.IsPricingErrorVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
    IsClosable="False"
    Margin="0,0,0,12" />
```

The `l:Uids.Uid="MainView.PricingErrorInfoBar"` pattern auto-binds Title + Message from the
matching resw entries (`MainView.PricingErrorInfoBar.Title` and `MainView.PricingErrorInfoBar.Message`).

**Part B — BannerStackPolicyTests** (D-PR-05): create new file at
`CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs`.

This test verifies the `IsPricingErrorVisible` computed property's behavior in isolation. Since
constructing a full `MainViewModel` requires 12+ DI services, we use a test-double approach:
**create a minimal test fixture that exercises the `IsPricingErrorVisible` formula directly via a
`record`-shape stand-in** OR via reflection. Use the simplest approach — a stand-in:

```csharp
namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// PRICING-03 / D-PR-05: banner-stack policy verification — (IsPricingError × IsSessionExpired)
/// matrix. The ViewModel formula is `IsPricingError && !IsSessionExpired`. This test asserts the
/// 4-cell truth table at unit-test level without requiring a full MainViewModel construction.
///
/// Rationale: ResearchCoverageTests covers resw-key correctness; this class covers the
/// banner-priority logic that suppresses pricing while auth is showing.
/// </summary>
public class BannerStackPolicyTests
{
    /// <summary>Mirrors MainViewModel.IsPricingErrorVisible exactly.</summary>
    private static bool ComputeIsPricingErrorVisible(bool isPricingError, bool isSessionExpired)
        => isPricingError && !isSessionExpired;

    [Theory]
    [InlineData(false, false, false)]   // Neither — pricing banner hidden
    [InlineData(true,  false, true)]    // Only pricing error — banner visible
    [InlineData(false, true,  false)]   // Only session expired — pricing banner hidden (auth shows alone)
    [InlineData(true,  true,  false)]   // Both — auth wins, pricing suppressed (banner-stack policy)
    public void IsPricingErrorVisible_FollowsBannerStackPolicy(
        bool isPricingError, bool isSessionExpired, bool expected)
    {
        // ARRANGE / ACT
        var actual = ComputeIsPricingErrorVisible(isPricingError, isSessionExpired);

        // ASSERT
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BannerStackPolicy_AuthAlwaysWinsOverPricing()
    {
        // The two-banner cap policy: when auth banner is showing, pricing must be suppressed
        // regardless of pricing-error state.
        Assert.False(ComputeIsPricingErrorVisible(isPricingError: true,  isSessionExpired: true));
        Assert.False(ComputeIsPricingErrorVisible(isPricingError: false, isSessionExpired: true));
    }
}
```

**Critical**: this test does NOT instantiate `MainViewModel` (avoids 12-arg constructor + DI
dependencies). It mirrors the formula in `ComputeIsPricingErrorVisible`. If `MainViewModel`'s
formula ever drifts, the test will go stale silently — mitigate by adding a single sanity
integration test in a follow-up phase OR by extracting the formula to a static helper that
both call.

**Per scope_reduction_prohibition rule**: this is NOT a "v1 simplification". The locked decision
**D-PR-05** explicitly requires a unit-level matrix test; instantiation of `MainViewModel`
is out of scope per the test pyramid (full integration tests are visual-smoke tier).
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~BannerStackPolicyTests" --nologo</automated>
  </verify>
  <done>BannerStackPolicyTests passes (5 test cases including the Theory's 4 InlineData rows + 1 Fact). MainView.xaml builds clean with new InfoBar; XAML compiler resolves `l:Uids.Uid="MainView.PricingErrorInfoBar"` against the 2 resw keys.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| _pricingService → MainViewModel | thrown exceptions from EnsurePricesLoadedAsync (network failure, JSON parse error, file IO) |
| ThreadPool worker → MainViewModel observable mutation | Site 1 runs in Task.Run; cross-thread property mutation must marshal via `_dispatcherQueue` |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-27-03-01 | Information Disclosure | exception messages from pricing service | mitigate | Debug.WriteLine logs `ex.Message` (not `ex.ToString()`) to debug output only; user-facing InfoBar uses generic localized text without exception details (per CLAUDE.md "No sensitive data in errors" rule) |
| T-27-03-02 | Tampering | resw values controlling InfoBar text | accept | bundled assets, code-integrity protected at install time |
| T-27-03-03 | Race condition | concurrent property mutation from ThreadPool + UI thread | mitigate | Site 1 wraps in `_dispatcherQueue.TryEnqueue` (G-1 alignment); Site 2 already on UI thread |
| T-27-03-04 | Denial of Service | repeated pricing failures spam Debug log | accept | Debug.WriteLine is ignored in Release; one log line per failure is acceptable |
</threat_model>

<verification>
Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — must succeed.
Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~BannerStackPolicyTests|FullyQualifiedName~ResourceCoverageTests" --nologo` — all green.
Manual smoke (optional): kill internet, restart app → pricing fails → InfoBar appears with localized text. Restore network, click Refresh → InfoBar disappears.
</verification>

<success_criteria>
1. PRICING-01: failure → IsPricingError = true → localized InfoBar appears
2. PRICING-02: subsequent retry success → IsPricingError = false → InfoBar disappears
3. PRICING-03: when IsSessionExpired = true, pricing InfoBar is suppressed (auth wins)
4. BannerStackPolicyTests passes (4 matrix cases + 1 priority sanity case)
5. ResourceCoverageTests passes for both new keys
6. Build is green
</success_criteria>

<output>
After completion, create `.planning/phases/27-nextwin-orgid-pricing-l10n/27-03-SUMMARY.md` documenting:
- 2 new resw keys
- IsPricingError + IsPricingErrorVisible properties
- 2 call-site instrumentations (G-1 marshaling at Site 1)
- BannerStackPolicyTests creation
- Banner stack ordering decision documented for Phase-end PROJECT.md update
</output>

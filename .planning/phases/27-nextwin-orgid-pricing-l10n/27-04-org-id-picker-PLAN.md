---
phase: 27-nextwin-orgid-pricing-l10n
plan: 04
type: execute
wave: 4
depends_on:
  - 27-03
files_modified:
  - CCInfoWindows/CCInfoWindows/Models/OrganizationInfo.cs
  - CCInfoWindows/CCInfoWindows/Messages/OpenOrgPickerRequestedMessage.cs
  - CCInfoWindows/CCInfoWindows/Services/Interfaces/IClaudeApiService.cs
  - CCInfoWindows/CCInfoWindows/Services/ClaudeApiService.cs
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
  - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
  - CCInfoWindows.Tests/ViewModels/OrgMismatchSoftPromptTests.cs
autonomous: false
requirements:
  - ORGID-01
  - ORGID-02
  - ORGID-03
  - ORGID-04
  - ORGID-05

user_setup: []

must_haves:
  truths:
    - "User clicks Settings → Account → 'Re-detect organization' button → ContentDialog opens listing orgs (name + uuid) from /api/organizations"
    - "Selecting a different org persists new uuid to Credential Manager 'CCInfoWindows/claude-org' and triggers MainViewModel.Logout (lands on LoginView)"
    - "After 5 consecutive polls with utilization=0 AND HasActiveSession=true, MainView shows a dismissable InfoBar prompting org re-resolve"
    - "Checking 'Don't show again this session' + closing the InfoBar suppresses it for the rest of the app session (in-memory only — re-appears next launch)"
    - "All ORGID UI strings (button label, dialog title/buttons, InfoBar text, suppress-checkbox) are localized in DE + EN"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Models/OrganizationInfo.cs"
      provides: "Public DTO for /api/organizations response items (Uuid + Name)"
      contains: "OrganizationInfo"
    - path: "CCInfoWindows/CCInfoWindows/Services/Interfaces/IClaudeApiService.cs"
      provides: "ListAvailableOrganizationsAsync method signature"
      contains: "ListAvailableOrganizationsAsync"
    - path: "CCInfoWindows/CCInfoWindows/Services/ClaudeApiService.cs"
      provides: "Public ListAvailableOrganizationsAsync extracted from private TryMigrateOrgIdAsync"
      contains: "ListAvailableOrganizationsAsync"
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      provides: "_zeroUtilizationPollCount + IsOrgMismatchPromptVisible + ResolveOrgMismatchCommand + SuppressOrgMismatchPromptCommand"
      contains: "OrgMismatchPollThreshold"
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs"
      provides: "OpenOrgPickerCommand triggers Account-tab dialog flow"
      contains: "OpenOrgPicker"
    - path: "CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml"
      provides: "Re-detect button + ContentDialog markup on Account tab"
      contains: "OrgPickerDialog"
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      provides: "Org-mismatch InfoBar in Row 0 banner stack"
      contains: "OrgMismatchInfoBar"
    - path: "CCInfoWindows.Tests/ViewModels/OrgMismatchSoftPromptTests.cs"
      provides: "Poll-counter increment + threshold trigger + suppression flag tests"
      contains: "OrgMismatchSoftPromptTests"
  key_links:
    - from: "ClaudeApiService.ListAvailableOrganizationsAsync"
      to: "/api/organizations endpoint via _bridge.FetchJsonAsync"
      via: "extracted from private TryMigrateOrgIdAsync (line 163)"
      pattern: "ListAvailableOrganizationsAsync"
    - from: "MainViewModel.PollUsageCoreAsync (success branch)"
      to: "_zeroUtilizationPollCount increment + threshold check"
      via: "post-FetchUsageAsync, after UpdateUsagePropertiesAsync"
      pattern: "_zeroUtilizationPollCount"
    - from: "SettingsView.OpenOrgPickerButton click"
      to: "ContentDialog with ListView of OrganizationInfo + persist + Logout"
      via: "ICommand → ShowAsync() → CredentialService.SaveOrganizationId + MainViewModel.LogoutCommand.Execute"
      pattern: "OpenOrgPickerCommand"
    - from: "MainView OrgMismatchInfoBar ResolveButton click"
      to: "Settings → Account → opens OrgPicker dialog"
      via: "ResolveOrgMismatchCommand: navigates to Settings + sets SelectedTabIndex=AccountTabIndex + opens dialog"
      pattern: "ResolveOrgMismatchCommand"
---

<objective>
Ship the multi-account org-id picker (ORGID-01..05) — the largest feature in Phase 27. Multi-account
users (personal + team Anthropic accounts under the same email) currently get a wrong cached org-id
in `CCInfoWindows/claude-org`, which surfaces as 0% utilization with no error. This plan adds:

1. **Public API extraction** — `IClaudeApiService.ListAvailableOrganizationsAsync` extracted from the
   existing private `TryMigrateOrgIdAsync` (line 163).
2. **Settings Account tab Re-detect button** + ContentDialog with ListView of orgs; selection
   persists new org-id and triggers `MainViewModel.Logout` (cookie-jar partitioning per **PITFALLS
   B2** mandates re-auth).
3. **MainView soft-prompt InfoBar** triggered after 5 consecutive zero-utilization polls
   (`OrgMismatchPollThreshold = 5`); dismissable in-memory only (NOT persisted per **D-OG-05**).
4. **Localization** — 8 resw key pairs across button label / dialog / InfoBar / suppress-checkbox.

Wave 4 (after 27-03) because:
- Largest file surface (13 files modified vs ≤5 in earlier plans)
- Touches `MainViewModel.cs` (after 27-02 + 27-03 modifications) — sequenced last
- Touches `MainView.xaml` Row-0 banner stack (after 27-03 added pricing InfoBar)
- Touches `SettingsView.xaml` Account tab (only this plan)
- `autonomous: false` — visual smoke required: org-switch lands on LoginView (Logout flow + cookie
  jar reset is hard to test headless)

Purpose: ORGID-01..05 — multi-account org-id picker (memory note `backlog_org_id_picker.md`).

Output: 13 file modifications, 1 new model, 1 new message, 1 new test class, 8 resw key pairs,
4 ObservableProperty/field additions to MainViewModel, 2 RelayCommands on MainViewModel,
1 RelayCommand on SettingsViewModel, ContentDialog markup, soft-prompt InfoBar, full localization
coverage.
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
@.planning/phases/27-nextwin-orgid-pricing-l10n/27-03-pricing-error-PLAN.md
@.planning/research/PITFALLS.md

@CCInfoWindows/CCInfoWindows/Services/ClaudeApiService.cs
@CCInfoWindows/CCInfoWindows/Services/Interfaces/IClaudeApiService.cs
@CCInfoWindows/CCInfoWindows/Services/CredentialService.cs
@CCInfoWindows/CCInfoWindows/Services/Interfaces/ICredentialService.cs
@CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
@CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
@CCInfoWindows/CCInfoWindows/Views/MainView.xaml
@CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
@CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs

<interfaces>
<!-- Existing private TryMigrateOrgIdAsync to extract from (ClaudeApiService.cs:155-192) -->
```csharp
private async Task<string?> TryMigrateOrgIdAsync(CancellationToken ct)
{
    try
    {
        ct.ThrowIfCancellationRequested();
        var responseBody = await _bridge.FetchJsonAsync($"{BaseUrl}/api/organizations");
        if (responseBody is null) return null;
        using var doc = JsonDocument.Parse(responseBody);
        var orgs = doc.RootElement;
        if (orgs.GetArrayLength() > 0)
        {
            var uuid = orgs[0].GetProperty("uuid").GetString();
            if (!string.IsNullOrEmpty(uuid))
            {
                _credentialService.SaveOrganizationId(uuid);
                return uuid;
            }
        }
    }
    catch (UnauthorizedAccessException)
    {
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
    }
    catch (Exception) { /* swallowed */ }
    return null;
}
```

<!-- Existing ICredentialService methods -->
```csharp
void SaveOrganizationId(string orgId);
string? GetOrganizationId();
void ClearCredentials();
```

<!-- Existing MainViewModel.Logout (line 1027) — call this after org switch -->
```csharp
[RelayCommand]
private void Logout()
{
    _historyService.ClearHistory();
    _credentialService.ClearCredentials();
    _bridge.Reset();
    WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
    IsSessionExpired = false;
    _autoReauthAttempted = false;
    _navigationService.NavigateTo<LoginView>();
}
```

<!-- Existing _hasActiveSession ObservableProperty at MainViewModel line 227 — drives whether the soft-prompt is even relevant -->

<!-- Existing PollUsageCoreAsync (line 462-493) — soft-prompt counter wires here, AFTER successful FetchUsageAsync -->
```csharp
try
{
    var result = await _apiService.FetchUsageAsync();
    if (result != null)
    {
        await UpdateUsagePropertiesAsync(result);
        _autoReauthAttempted = false;
        // ORGID-03 wiring goes HERE
    }
    // ...
}
```

<!-- Existing SettingsViewModel constructor and SegmentedControl: AccountTabIndex = 2 (post-Phase-26)
     SessionsTabIndex = 3, AboutTabIndex = 4 -->

<!-- Existing MainView.xaml Row-0 InfoBar stack (after 27-03):
     UpdateInfoBar → SessionExpiredInfoBar → ApiErrorInfoBar → PricingErrorInfoBar → MigrationToastInfoBar
     OrgMismatchInfoBar inserts AFTER PricingErrorInfoBar, BEFORE MigrationToastInfoBar (priority: auth > api > pricing > org > migration). -->
```
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add OrganizationInfo model + extract ListAvailableOrganizationsAsync to IClaudeApiService + ClaudeApiService</name>
  <files>
    CCInfoWindows/CCInfoWindows/Models/OrganizationInfo.cs,
    CCInfoWindows/CCInfoWindows/Services/Interfaces/IClaudeApiService.cs,
    CCInfoWindows/CCInfoWindows/Services/ClaudeApiService.cs
  </files>
  <behavior>
    - `OrganizationInfo` record exists with two string properties: `Uuid` and `Name`
    - `IClaudeApiService.ListAvailableOrganizationsAsync(CancellationToken ct = default)` returns `Task<IReadOnlyList<OrganizationInfo>>`
    - `ClaudeApiService.ListAvailableOrganizationsAsync` queries `/api/organizations` via `_bridge.FetchJsonAsync` and parses each org into an `OrganizationInfo`
    - On `UnauthorizedAccessException` it broadcasts `AuthStateChangedMessage(false)` and returns empty list (preserves existing TryMigrateOrgIdAsync behavior)
    - Other exceptions return empty list (defensive — UI shows "no orgs found")
    - The existing private `TryMigrateOrgIdAsync` REMAINS (still used by the cookie-fallback flow at line 51) but is REWRITTEN to delegate to the new public method (DRY)
    - Build is green, no breaking changes to existing FetchUsageAsync
  </behavior>
  <action>
**Per D-OG-01**: extract the org-list parsing logic; delegate the existing private migrator to it.

**A. Create `CCInfoWindows/CCInfoWindows/Models/OrganizationInfo.cs`:**

```csharp
namespace CCInfoWindows.Models;

/// <summary>
/// ORGID-01: DTO for entries returned by /api/organizations. Used by the Settings → Account →
/// Re-detect organization flow. The API returns more fields (capabilities[], settings, member_role)
/// but only Uuid + Name are relevant for the picker UI.
/// </summary>
public sealed record OrganizationInfo(string Uuid, string Name);
```

**B. Add method signature to `Services/Interfaces/IClaudeApiService.cs`:**

```csharp
using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

public interface IClaudeApiService
{
    Task<UsageResponse?> FetchUsageAsync(CancellationToken ct = default);
    UsageResponse? GetCachedUsage();
    Task SaveCacheAsync(UsageResponse data);
    Task<UsageResponse?> LoadCacheAsync();

    /// <summary>
    /// ORGID-01 (D-OG-01): Returns the list of organizations the current session has access to,
    /// fetched from /api/organizations. Empty list when unauthenticated, network failure, or
    /// JSON parse error. UnauthorizedAccessException broadcasts AuthStateChangedMessage(false)
    /// to trigger the existing auto-reauth flow before returning empty.
    /// </summary>
    Task<IReadOnlyList<OrganizationInfo>> ListAvailableOrganizationsAsync(CancellationToken ct = default);
}
```

**C. Add public method implementation to `Services/ClaudeApiService.cs`** (place after the existing
public methods, before the private `TryMigrateOrgIdAsync`):

```csharp
/// <summary>
/// ORGID-01 (D-OG-01): public org-list endpoint. Extracted from the private TryMigrateOrgIdAsync
/// at line 163 — the same /api/organizations endpoint. Returns parsed entries; never throws to the
/// caller (returns empty list on any failure).
/// </summary>
public async Task<IReadOnlyList<OrganizationInfo>> ListAvailableOrganizationsAsync(CancellationToken ct = default)
{
    try
    {
        ct.ThrowIfCancellationRequested();

        var responseBody = await _bridge.FetchJsonAsync($"{BaseUrl}/api/organizations");
        if (responseBody is null)
        {
            return Array.Empty<OrganizationInfo>();
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OrganizationInfo>();
        }

        var list = new List<OrganizationInfo>(root.GetArrayLength());
        foreach (var element in root.EnumerateArray())
        {
            if (!element.TryGetProperty("uuid", out var uuidProp)) continue;
            var uuid = uuidProp.GetString();
            if (string.IsNullOrEmpty(uuid)) continue;

            // Name fallback chain: name → uuid (defensive — API typically returns name)
            var name = element.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? uuid
                : uuid;

            list.Add(new OrganizationInfo(uuid, name));
        }

        return list;
    }
    catch (UnauthorizedAccessException)
    {
        WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
        return Array.Empty<OrganizationInfo>();
    }
    catch (Exception)
    {
        // Defensive — caller renders "no orgs available" in the dialog
        return Array.Empty<OrganizationInfo>();
    }
}
```

**D. Refactor `TryMigrateOrgIdAsync` (line 157-192) to delegate**:

```csharp
private async Task<string?> TryMigrateOrgIdAsync(CancellationToken ct)
{
    var orgs = await ListAvailableOrganizationsAsync(ct);
    if (orgs.Count == 0)
    {
        return null;
    }

    // Preserve original first-org auto-pick behavior (cookie-fallback case)
    var first = orgs[0];
    _credentialService.SaveOrganizationId(first.Uuid);
    return first.Uuid;
}
```

**Critical invariant**: the cookie-fallback at line 51 (`var orgId = _credentialService.GetOrganizationId();`)
behavior must be preserved — first-org auto-pick remains the default for a freshly-authenticated
single-org user. This refactor is pure DRY; no behavior change for the migration path.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj --nologo</automated>
  </verify>
  <done>Build is green. `IClaudeApiService.ListAvailableOrganizationsAsync` is public; `OrganizationInfo` model exists; `TryMigrateOrgIdAsync` delegates to the new method without behavior change.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Add MainViewModel ORGID state — IsOrgMismatchPromptVisible + 2 RelayCommands + poll-counter wiring + 8 resw keys</name>
  <files>
    CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs,
    CCInfoWindows/CCInfoWindows/Messages/OpenOrgPickerRequestedMessage.cs,
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw,
    CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs,
    CCInfoWindows.Tests/ViewModels/OrgMismatchSoftPromptTests.cs
  </files>
  <behavior>
    - `MainViewModel` exposes `[ObservableProperty] IsOrgMismatchPromptVisible` (bool)
    - `MainViewModel` has private `_zeroUtilizationPollCount` (int) and `_orgMismatchSuppressed` (bool, in-memory) fields
    - `MainViewModel` has private const `OrgMismatchPollThreshold = 5` (per D-OG-04)
    - `PollUsageCoreAsync` success path: when `data.FiveHour?.Utilization == 0 && HasActiveSession`, increments `_zeroUtilizationPollCount`; when count reaches threshold AND not suppressed → sets `IsOrgMismatchPromptVisible = true`
    - `PollUsageCoreAsync` success path: when utilization > 0 (or no active session), resets count to 0 AND sets `IsOrgMismatchPromptVisible = false`
    - `[RelayCommand] ResolveOrgMismatch` navigates to Settings, broadcasts `OpenOrgPickerRequestedMessage` so SettingsViewModel opens the dialog
    - `[RelayCommand] SuppressOrgMismatchPrompt` sets `_orgMismatchSuppressed = true` AND `IsOrgMismatchPromptVisible = false` (in-memory only — does NOT call SaveSettings)
    - `OpenOrgPickerRequestedMessage` record exists in `Messages/`
    - 8 resw key pairs added in DE + EN
    - `OrgMismatchSoftPromptTests` covers: counter increment, threshold trigger, suppression flag, reset on utilization > 0, no increment when no active session
    - All tests pass
  </behavior>
  <action>
**Per D-OG-04, D-OG-05, D-OG-06 + specifics block "ORGID poll-counter wiring".**

**A. Add 8 resw key pairs** to BOTH locale files (before `</root>`):

en-US/Resources.resw:
```xml
<data name="Settings.Account.RedetectButton" xml:space="preserve">
  <value>Re-detect organization</value>
</data>
<data name="Dialog.OrgPicker.Title" xml:space="preserve">
  <value>Select organization</value>
</data>
<data name="Dialog.OrgPicker.SwitchButton" xml:space="preserve">
  <value>Switch</value>
</data>
<data name="Dialog.OrgPicker.CancelButton" xml:space="preserve">
  <value>Cancel</value>
</data>
<data name="MainView.OrgMismatchInfoBar.Title" xml:space="preserve">
  <value>Possible organization mismatch</value>
</data>
<data name="MainView.OrgMismatchInfoBar.Message" xml:space="preserve">
  <value>5 polls returned 0% utilization while a session is active. Re-resolve organization?</value>
</data>
<data name="MainView.OrgMismatchInfoBar.ResolveButton" xml:space="preserve">
  <value>Re-resolve</value>
</data>
<data name="MainView.OrgMismatchInfoBar.SuppressCheckbox" xml:space="preserve">
  <value>Don't show again this session</value>
</data>
```

de-DE/Resources.resw:
```xml
<data name="Settings.Account.RedetectButton" xml:space="preserve">
  <value>Organisation neu erkennen</value>
</data>
<data name="Dialog.OrgPicker.Title" xml:space="preserve">
  <value>Organisation auswählen</value>
</data>
<data name="Dialog.OrgPicker.SwitchButton" xml:space="preserve">
  <value>Wechseln</value>
</data>
<data name="Dialog.OrgPicker.CancelButton" xml:space="preserve">
  <value>Abbrechen</value>
</data>
<data name="MainView.OrgMismatchInfoBar.Title" xml:space="preserve">
  <value>Möglicher Organisations-Mismatch</value>
</data>
<data name="MainView.OrgMismatchInfoBar.Message" xml:space="preserve">
  <value>5 Abfragen ergaben 0% Auslastung bei aktiver Sitzung. Organisation neu erkennen?</value>
</data>
<data name="MainView.OrgMismatchInfoBar.ResolveButton" xml:space="preserve">
  <value>Neu erkennen</value>
</data>
<data name="MainView.OrgMismatchInfoBar.SuppressCheckbox" xml:space="preserve">
  <value>Diese Sitzung nicht mehr anzeigen</value>
</data>
```

**B. Extend ResourceCoverageTests** — append all 8 keys to `RequiredKeys`, `ExpectedEnUs`,
`ExpectedDeDe` per the same pattern as 27-01/27-02/27-03.

**C. Create `CCInfoWindows/CCInfoWindows/Messages/OpenOrgPickerRequestedMessage.cs`:**

```csharp
namespace CCInfoWindows.Messages;

/// <summary>
/// ORGID-03: sent by MainViewModel.ResolveOrgMismatchCommand to instruct SettingsViewModel
/// to open the OrgPicker ContentDialog after the user navigates to Settings → Account.
/// SettingsViewModel.IRecipient&lt;OpenOrgPickerRequestedMessage&gt;.Receive wraps in
/// IDispatcherQueue.TryEnqueue per G-1 convention.
/// </summary>
public sealed record OpenOrgPickerRequestedMessage();
```

**D. Add fields, property, command, and wiring to `MainViewModel.cs`**:

Near the top-of-file (constants region):
```csharp
// ORGID-03 (D-OG-04): threshold for soft-prompt trigger after consecutive zero-utilization polls
private const int OrgMismatchPollThreshold = 5;
```

Near the existing `_autoReauthAttempted` field (around line 272):
```csharp
// ORGID-03 / D-OG-04: counter for consecutive utilization=0 polls while HasActiveSession
private int _zeroUtilizationPollCount;

// ORGID-04 / D-OG-05: in-memory dismissal — NOT persisted (resets on app restart)
private bool _orgMismatchSuppressed;
```

In the ObservableProperty block (near other UI-state flags):
```csharp
// ORGID-03 / D-OG-04: soft-prompt InfoBar visibility
[ObservableProperty]
private bool _isOrgMismatchPromptVisible;
```

**E. Wire poll-counter** in `PollUsageCoreAsync` (line 462-493). Insert AFTER the existing
`_autoReauthAttempted = false;` line (currently line 473):

```csharp
if (result != null)
{
    await UpdateUsagePropertiesAsync(result);
    _autoReauthAttempted = false;  // D-02: HTTP 200 resets the auto-reauth budget

    // ORGID-03 / D-OG-04: org-mismatch soft-prompt counter
    var utilization = result.FiveHour?.Utilization ?? 0;
    if (utilization == 0 && HasActiveSession)
    {
        _zeroUtilizationPollCount++;
        if (_zeroUtilizationPollCount >= OrgMismatchPollThreshold && !_orgMismatchSuppressed)
        {
            IsOrgMismatchPromptVisible = true;
        }
    }
    else
    {
        _zeroUtilizationPollCount = 0;
        IsOrgMismatchPromptVisible = false;
    }
}
```

**Note** `result.FiveHour?.Utilization` uses the integer `Utilization` (0-100 range) NOT the
`NormalizedUtilization` (0-1) per the Phase-2 fix documented in MEMORY.md. Stay consistent with
the Burn-rate site at line 511 which uses `Utilization`.

**F. Add 2 RelayCommands** near other commands at the bottom of MainViewModel.cs:

```csharp
/// <summary>
/// ORGID-03: navigates user to Settings → Account and signals SettingsViewModel
/// to open the OrgPicker ContentDialog. Fires from the MainView soft-prompt InfoBar
/// "Re-resolve" button.
/// </summary>
[RelayCommand]
private void ResolveOrgMismatch()
{
    IsOrgMismatchPromptVisible = false;
    _navigationService.NavigateTo<SettingsView>();
    WeakReferenceMessenger.Default.Send(new OpenOrgPickerRequestedMessage());
}

/// <summary>
/// ORGID-04 (D-OG-05): suppresses the soft-prompt for the rest of the in-process session.
/// NOT persisted — flag resets to false on next app start.
/// </summary>
[RelayCommand]
private void SuppressOrgMismatchPrompt()
{
    _orgMismatchSuppressed = true;
    IsOrgMismatchPromptVisible = false;
}
```

**G. Create `OrgMismatchSoftPromptTests`** at `CCInfoWindows.Tests/ViewModels/OrgMismatchSoftPromptTests.cs`:

Same rationale as BannerStackPolicyTests in 27-03 — avoid full MainViewModel construction; mirror
the counter-state machine directly.

```csharp
namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// ORGID-03 / ORGID-04 (D-OG-04, D-OG-05): verifies the consecutive-zero-utilization counter
/// state machine. Mirrors MainViewModel.PollUsageCoreAsync logic — DOES NOT instantiate
/// MainViewModel (12-arg ctor + WinRT services).
/// </summary>
public class OrgMismatchSoftPromptTests
{
    private const int Threshold = 5;

    /// <summary>State machine reproducing MainViewModel.PollUsageCoreAsync ORGID block.</summary>
    private sealed class CounterState
    {
        public int Count;
        public bool Suppressed;
        public bool PromptVisible;

        public void OnPoll(double utilization, bool hasActiveSession)
        {
            if (utilization == 0 && hasActiveSession)
            {
                Count++;
                if (Count >= Threshold && !Suppressed)
                    PromptVisible = true;
            }
            else
            {
                Count = 0;
                PromptVisible = false;
            }
        }
    }

    [Fact]
    public void Counter_TriggersAtThreshold_WhenAllPollsZero()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold; i++) s.OnPoll(utilization: 0, hasActiveSession: true);
        Assert.Equal(Threshold, s.Count);
        Assert.True(s.PromptVisible);
    }

    [Fact]
    public void Counter_DoesNotTrigger_BelowThreshold()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold - 1; i++) s.OnPoll(0, true);
        Assert.False(s.PromptVisible);
    }

    [Fact]
    public void Counter_ResetsAndHidesPrompt_OnNonZeroUtilization()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold; i++) s.OnPoll(0, true);
        Assert.True(s.PromptVisible);

        s.OnPoll(utilization: 5, hasActiveSession: true);
        Assert.Equal(0, s.Count);
        Assert.False(s.PromptVisible);
    }

    [Fact]
    public void Counter_DoesNotIncrement_WhenNoActiveSession()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold + 2; i++) s.OnPoll(utilization: 0, hasActiveSession: false);
        Assert.Equal(0, s.Count);
        Assert.False(s.PromptVisible);
    }

    [Fact]
    public void SuppressionFlag_PreventsPromptAtThreshold()
    {
        var s = new CounterState { Suppressed = true };
        for (int i = 0; i < Threshold + 1; i++) s.OnPoll(0, true);
        Assert.True(s.Count >= Threshold);  // counter still increments
        Assert.False(s.PromptVisible);       // prompt stays hidden
    }
}
```
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~OrgMismatchSoftPromptTests|FullyQualifiedName~ResourceCoverageTests" --nologo</automated>
  </verify>
  <done>Build green. OrgMismatchSoftPromptTests passes (5 cases). ResourceCoverageTests passes with 8 new ORGID keys in both locales.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 3: Wire SettingsViewModel OpenOrgPickerCommand + SettingsView Re-detect button + ContentDialog + MainView OrgMismatch InfoBar</name>
  <files>
    CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs,
    CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml,
    CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs,
    CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  </files>
  <behavior>
    - `SettingsViewModel` registers as `IRecipient<OpenOrgPickerRequestedMessage>`; `Receive` body wraps the dialog-trigger in `_dispatcherQueue.TryEnqueue` per G-1
    - `SettingsViewModel` exposes `[RelayCommand] OpenOrgPickerCommand` (parameterless async; produces `ObservableCollection<OrganizationInfo>` for ListView binding + a `RequestOpenOrgPickerDialog` event the View subscribes to)
    - `SettingsViewModel` constructor adds `IClaudeApiService` parameter (DI registration in `App.xaml.cs` already provides `IClaudeApiService` as singleton); also resolves `MainViewModel` for Logout dispatch via `App.Services.GetRequiredService<MainViewModel>()` invoked inside `OpenOrgPickerCommand` (avoids circular-DI by deferring resolution to call-time, NOT constructor-time)
    - `SettingsView.xaml.cs` subscribes to the `RequestOpenOrgPickerDialog` event and calls `OrgPickerDialog.ShowAsync()`; on `ContentDialogResult.Primary`, persists the selected org via `ICredentialService.SaveOrganizationId` and invokes `MainViewModel.LogoutCommand.Execute(null)`
    - `SettingsView.xaml` Account tab contains a "Re-detect organization" Button bound to `OpenOrgPickerCommand` with `l:Uids.Uid="Settings.Account.RedetectButton"`; below the existing Logout row
    - `SettingsView.xaml` contains a `<ContentDialog x:Name="OrgPickerDialog">` with `l:Uids.Uid="Dialog.OrgPicker.Title"` for the Title plus PrimaryButton/CloseButton via `l:Uids.Uid="Dialog.OrgPicker.SwitchButton"` / `.CancelButton`; ListView with single-select bound to `ViewModel.AvailableOrganizations`
    - `MainView.xaml` Row-0 banner stack contains `<InfoBar x:Name="OrgMismatchInfoBar" l:Uids.Uid="MainView.OrgMismatchInfoBar" Severity="Warning" IsClosable="True" IsOpen="{x:Bind ViewModel.IsOrgMismatchPromptVisible, Mode=TwoWay}">` — inserted AFTER `PricingErrorInfoBar` and BEFORE `MigrationToastInfoBar`, with ActionButton bound to `ResolveOrgMismatchCommand` and a CheckBox in `InfoBar.Content` bound to `SuppressOrgMismatchPromptCommand`
    - Build is green
    - `MessengerThreadingConventionTests` (Phase 24 G-1) still passes — confirms the new `IRecipient<OpenOrgPickerRequestedMessage>` complies with G-1
  </behavior>
  <action>
**Per D-OG-02, D-OG-03 + L-02 (G-1) + PITFALLS B2.**

**Important DI pattern decision (resolves the D-13 lesson)**:

`MainViewModel` is registered as `AddTransient` (Phase 21 D-13 hotfix discovery). A circular DI
between `SettingsViewModel` ↔ `MainViewModel` would either:
- Break DI graph (constructor-time circular), OR
- Recreate a fresh `MainViewModel` instance (TransientAttribute → wrong instance, ClearHistory
  fires on a phantom).

**Solution**: resolve `MainViewModel` lazily at command-execution time via
`((App)Application.Current).Services.GetRequiredService<MainViewModel>()` inside `OpenOrgPickerCommand`.
Since `MainViewModel` is `AddTransient`, this returns a NEW instance — NOT the one bound to MainView.
Therefore we **broadcast `AuthStateChangedMessage(false)` via WeakReferenceMessenger** instead of
calling `LogoutCommand` directly — the live MainViewModel handler (Phase 24 DISPATCH-04) catches it
and runs the full logout flow. This honors the D-13 lesson: NO direct MainViewModel injection;
NO Logout-via-message (which Phase 21-03 reverted) — we use the EXISTING `AuthStateChangedMessage(false)`
broadcast which is the verified Phase 24 path.

**Concrete implementation**:

**A. Update `SettingsViewModel.cs`**:

1. Add `IClaudeApiService` to the field list and constructor parameters:
   ```csharp
   private readonly IClaudeApiService _apiService;

   // Constructor — append IClaudeApiService parameter
   public SettingsViewModel(
       ISettingsService settingsService,
       ICredentialService credentialService,
       INavigationService navigationService,
       IPricingService pricingService,
       IUsageHistoryService historyService,
       ISessionNameStore sessionNameStore,
       IJsonlService jsonlService,
       IDispatcherQueue dispatcherQueue,
       IClaudeApiService apiService)   // NEW — ORGID-01
   {
       // ... existing assignments ...
       _apiService = apiService;

       // ORGID-03 / G-1: receive open-picker requests from MainViewModel
       WeakReferenceMessenger.Default.Register<SettingsViewModel, OpenOrgPickerRequestedMessage>(this, (r, m) =>
       {
           r._dispatcherQueue.TryEnqueue(() => _ = r.OpenOrgPickerCommand.ExecuteAsync(null));
       });
   }
   ```

2. Add the `AvailableOrganizations` collection + dialog-result event + command:
   ```csharp
   /// <summary>ORGID-01: ListView ItemsSource for the OrgPicker ContentDialog.</summary>
   public ObservableCollection<OrganizationInfo> AvailableOrganizations { get; } = new();

   /// <summary>ORGID-01: selected org in the dialog ListView (TwoWay binding).</summary>
   [ObservableProperty]
   private OrganizationInfo? _selectedOrgPickerItem;

   /// <summary>
   /// ORGID-01: View subscribes to this event and calls OrgPickerDialog.ShowAsync(); the View
   /// returns the dialog result via the TaskCompletionSource on the event payload, allowing the
   /// command to await user choice without owning XAML references.
   /// </summary>
   public event EventHandler<OrgPickerDialogRequest>? RequestOpenOrgPickerDialog;

   /// <summary>Event payload — View completes the TCS with the dialog result.</summary>
   public sealed class OrgPickerDialogRequest
   {
       public TaskCompletionSource<ContentDialogResult> CompletionSource { get; } = new();
   }

   [RelayCommand]
   private async Task OpenOrgPickerAsync()
   {
       AvailableOrganizations.Clear();
       SelectedOrgPickerItem = null;

       // Fetch off-thread; marshal back to UI thread for collection mutation (G-1 alignment)
       var orgs = await _apiService.ListAvailableOrganizationsAsync();
       _dispatcherQueue.TryEnqueue(() =>
       {
           foreach (var o in orgs) AvailableOrganizations.Add(o);
       });

       // Hand control to the View to show the dialog
       var request = new OrgPickerDialogRequest();
       RequestOpenOrgPickerDialog?.Invoke(this, request);
       var result = await request.CompletionSource.Task;

       if (result != ContentDialogResult.Primary || SelectedOrgPickerItem is null)
           return;

       // ORGID-02 / PITFALLS B2: persist new org-id and trigger logout via the verified
       // AuthStateChangedMessage(false) broadcast (Phase 24 DISPATCH-04 handles the cookie-jar
       // reset + nav-to-LoginView path). NOT a direct MainViewModel.LogoutCommand call —
       // honors D-13 (AddTransient → wrong instance via DI resolution).
       _credentialService.SaveOrganizationId(SelectedOrgPickerItem.Uuid);
       WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
   }
   ```

3. Add `using CCInfoWindows.Models;` and `using Microsoft.UI.Xaml.Controls;` (for `ContentDialogResult`).

**B. Register `IClaudeApiService` injection in `App.xaml.cs`** (verify it's already a singleton —
existing `MainViewModel` ctor injection at line 288 confirms it is). Update SettingsViewModel
registration to pass the additional dependency. If `App.xaml.cs` uses constructor auto-resolution
(`AddTransient<SettingsViewModel>()`), no change needed — the new ctor parameter resolves
automatically.

**C. Update `SettingsView.xaml`** — Account tab additions (insert after the Logout button at line
387, BEFORE the closing `</StackPanel>` of the Account panel at line 391):

```xml
<!-- ORGID-01 / D-OG-02: Re-detect organization button -->
<Button l:Uids.Uid="Settings.Account.RedetectButton"
        Command="{x:Bind ViewModel.OpenOrgPickerCommand}"
        HorizontalAlignment="Stretch"
        Margin="12,0,12,12">
    <StackPanel Orientation="Horizontal" Spacing="8">
        <FontIcon Glyph="&#xE8C8;" FontSize="14" />
        <TextBlock l:Uids.Uid="Settings.Account.RedetectButton" />
    </StackPanel>
</Button>
```

(Note: the `l:Uids.Uid` on Button auto-binds Content + AutomationProperties from the resw key.
The inner TextBlock duplicates for visual purposes — keep both for clarity in the layout pattern
established by other Settings buttons.)

**Add the ContentDialog** at the page-level (outside any `<StackPanel>`, near the closing `</Page>`
tag — following the pattern established by `RenameSessionDialog` in Phase 26):

```xml
<!-- ORGID-01..02 / D-OG-03: Org picker ContentDialog -->
<ContentDialog x:Name="OrgPickerDialog"
               l:Uids.Uid="Dialog.OrgPicker.Title"
               PrimaryButtonText=""
               CloseButtonText=""
               DefaultButton="Primary">
    <ContentDialog.Resources>
        <!-- l:Uids.Uid binds Title; PrimaryButtonText / CloseButtonText set via setter on dialog reveal -->
    </ContentDialog.Resources>

    <Grid Width="400" Height="280">
        <ListView ItemsSource="{x:Bind ViewModel.AvailableOrganizations, Mode=OneWay}"
                  SelectedItem="{x:Bind ViewModel.SelectedOrgPickerItem, Mode=TwoWay}"
                  SelectionMode="Single">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="models:OrganizationInfo">
                    <StackPanel Margin="4,8" Spacing="2">
                        <TextBlock Text="{x:Bind Name}" FontWeight="SemiBold" />
                        <TextBlock Text="{x:Bind Uuid}" FontSize="11"
                                   Foreground="{ThemeResource SecondaryTextBrush}" />
                    </StackPanel>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</ContentDialog>
```

Add the `xmlns:models="using:CCInfoWindows.Models"` namespace to the Page root if not already
present.

**Note on PrimaryButtonText/CloseButtonText**: WinUI 3 ContentDialog does NOT honor `l:Uids.Uid`
for these properties directly. Set them in the code-behind from the localizer when the dialog
opens (see step D below).

**D. Update `SettingsView.xaml.cs`** — subscribe to the ViewModel event and show the dialog:

```csharp
// In OnLoaded (or wherever existing event subscriptions live):
if (DataContext is SettingsViewModel vm)
{
    vm.RequestOpenOrgPickerDialog += OnRequestOpenOrgPickerDialog;
}

// Symmetric -= in OnUnloaded (Phase 22 CD-05 pattern).

private async void OnRequestOpenOrgPickerDialog(object? sender, SettingsViewModel.OrgPickerDialogRequest request)
{
    OrgPickerDialog.XamlRoot = this.XamlRoot;
    OrgPickerDialog.PrimaryButtonText = Localizer.Get().GetLocalizedString("Dialog.OrgPicker.SwitchButton");
    OrgPickerDialog.CloseButtonText = Localizer.Get().GetLocalizedString("Dialog.OrgPicker.CancelButton");
    var result = await OrgPickerDialog.ShowAsync();
    request.CompletionSource.TrySetResult(result);
}
```

Add `using WinUI3Localizer;` if not already present.

**E. Update `MainView.xaml`** — insert the OrgMismatch InfoBar AFTER `PricingErrorInfoBar` and
BEFORE `MigrationToastInfoBar` (preserves banner-stack ordering: auth > api > pricing > org > migration):

```xml
<!-- ORGID-03..04 / D-OG-04..06: org-mismatch soft-prompt InfoBar -->
<InfoBar
    x:Name="OrgMismatchInfoBar"
    l:Uids.Uid="MainView.OrgMismatchInfoBar"
    Severity="Warning"
    IsOpen="{x:Bind ViewModel.IsOrgMismatchPromptVisible, Mode=TwoWay}"
    Visibility="{x:Bind ViewModel.IsOrgMismatchPromptVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
    IsClosable="True"
    Margin="0,0,0,12">
    <InfoBar.ActionButton>
        <Button Command="{x:Bind ViewModel.ResolveOrgMismatchCommand}">
            <TextBlock l:Uids.Uid="MainView.OrgMismatchInfoBar.ResolveButton" />
        </Button>
    </InfoBar.ActionButton>
    <InfoBar.Content>
        <CheckBox l:Uids.Uid="MainView.OrgMismatchInfoBar.SuppressCheckbox"
                  Command="{x:Bind ViewModel.SuppressOrgMismatchPromptCommand}"
                  Margin="48,4,0,4" />
    </InfoBar.Content>
</InfoBar>
```
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj --nologo</automated>
  </verify>
  <done>Build is green. SettingsView.xaml + .xaml.cs + SettingsViewModel + MainView.xaml all compile. The new IRecipient handler in SettingsViewModel wraps in `_dispatcherQueue.TryEnqueue` per G-1. ContentDialog markup is parsable. No new compiler warnings related to ORGID code.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 4: Visual smoke — verify org-picker flow + cookie-jar logout + suppress-flag in-memory only</name>
  <files>
    CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml,
    CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  </files>
  <action>
This task is a manual verification pass after Tasks 1-3 ship. NO automation runs here — Tasks 1-3
already covered all automatable verification (build + unit tests). The smoke test confirms:
1. The dialog actually opens and shows orgs from the live API
2. The Logout flow runs (cookie-jar reset per PITFALLS B2)
3. Localization renders correctly in both DE and EN
4. The suppress-checkbox flag is in-memory only (resets on restart)
  </action>
  <what-built>
    Tasks 1-3 produced:
      - `IClaudeApiService.ListAvailableOrganizationsAsync` extracted from private migrator
      - `MainViewModel` ORGID state (counter + visibility property + 2 RelayCommands)
      - `SettingsViewModel.OpenOrgPickerCommand` async dialog flow + `IRecipient<OpenOrgPickerRequestedMessage>` handler
      - `SettingsView.xaml` Re-detect button + ContentDialog (ListView)
      - `SettingsView.xaml.cs` event handler that calls `OrgPickerDialog.ShowAsync()`
      - `MainView.xaml` OrgMismatch InfoBar (ResolveButton + SuppressCheckbox)
      - 8 resw key pairs (DE + EN)
      - 5 OrgMismatchSoftPromptTests cases
  </what-built>
  <how-to-verify>
    Pre-build:
      1. `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — must succeed
      2. `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests|FullyQualifiedName~OrgMismatchSoftPromptTests|FullyQualifiedName~BannerStackPolicyTests|FullyQualifiedName~MessengerThreadingConventionTests" --nologo` — all green

    Manual visual smoke:
      3. `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
      4. Sign in. Wait for first poll to complete (utilization > 0 should display).
      5. Open Settings → Account tab. Confirm a "Re-detect organization" button appears below the Logout row.
      6. Click "Re-detect organization". Expected: ContentDialog opens listing your account's orgs (name + uuid). If you have a single org, the list has 1 entry.
      7. Click Cancel. Dialog closes; nothing changes; you're still signed in.
      8. Click "Re-detect organization" again. Select an org. Click Switch.
         - Expected: app navigates to LoginView (Logout flow ran via AuthStateChangedMessage(false) broadcast → MainViewModel handler clears credentials + cookie jar via _bridge.Reset)
         - Re-authenticate. Expected: usage data loads against the newly-selected org.
      9. Localization smoke: switch OS language to German (or change CurrentUICulture in app settings). Reopen the dialog. All labels display in German ("Organisation neu erkennen", "Organisation auswählen", "Wechseln", "Abbrechen").

    Soft-prompt smoke (skip if difficult to reproduce):
      10. To force the soft-prompt: temporarily change `OrgMismatchPollThreshold = 1` (in MainViewModel.cs), rebuild, then trigger 1 poll where the active session shows utilization 0. Confirm the InfoBar appears at top of MainView with "Re-resolve" + suppress-checkbox text.
      11. Click suppress-checkbox + close InfoBar. Confirm InfoBar stays hidden for the remainder of the session.
      12. Restart app. Confirm the suppress flag is reset (NOT persisted per D-OG-05).
      13. Revert `OrgMismatchPollThreshold = 5` before commit.

    PITFALLS B2 verification:
      14. Confirm that switching orgs requires re-authentication (cannot bypass LoginView). The cookie jar IS per-org partitioned per the documented pitfall.
  </how-to-verify>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests|FullyQualifiedName~OrgMismatchSoftPromptTests|FullyQualifiedName~BannerStackPolicyTests|FullyQualifiedName~MessengerThreadingConventionTests" --nologo</automated>
  </verify>
  <done>All automated tests pass AND user has typed "approved" after running steps 3-14. SUMMARY.md captures any deviations from the planned implementation pattern (especially the AuthStateChangedMessage(false) broadcast vs direct LogoutCommand call decision).</done>
  <resume-signal>
    Type "approved" if all visual smoke passes.
    Type "issues: [list]" if anything fails — common likely issues:
      - DI: SettingsViewModel ctor injection of IClaudeApiService missing → App.xaml.cs DI registration needs explicit factory call.
      - ContentDialog XamlRoot null → ensure OnLoaded fires before first dialog open, or set XamlRoot at construction.
      - Localizer.Get() called on background thread inside command → wrap collection mutation in _dispatcherQueue.TryEnqueue (G-1).
      - Logout broadcast doesn't reach the live MainViewModel → verify MainViewModel.Receive(AuthStateChangedMessage) registration is intact (Phase 24 DISPATCH-04 invariant).
  </resume-signal>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| /api/organizations response → ListAvailableOrganizationsAsync | untrusted-shaped JSON (Uuid + Name strings) — must validate types before use |
| User selection in ContentDialog → CredentialService.SaveOrganizationId | user-chosen value, but constrained to the API-provided list (no free-text input) |
| Cookie jar per-org partitioning (PITFALLS B2) | sensitive — switching orgs WITHOUT logout would leak prior-org session token to new-org context |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-27-04-01 | Spoofing | malicious /api/organizations response | mitigate | TLS pinning at WebView2 + JsonElement.TryGetProperty defensive parse; missing/empty fields skipped silently |
| T-27-04-02 | Tampering | local Credential Manager value | accept | DPAPI-encrypted; OS-level user-context protection |
| T-27-04-03 | Information Disclosure | uuid displayed in dialog ListView | accept | uuid is non-secret (visible in Anthropic console); not credentials |
| T-27-04-04 | Information Disclosure | cross-org cookie leakage | mitigate | CRITICAL: org switch MUST trigger Logout (cookie-jar reset). Implemented via AuthStateChangedMessage(false) broadcast → MainViewModel handler clears credentials + _bridge.Reset (Phase 24 DISPATCH-04 path). Verified by smoke step 8. PITFALLS B2: skipping the logout would persist prior-org cookies to the new-org context. |
| T-27-04-05 | Repudiation | n/a | accept | local-only state; no audit log requirement |
| T-27-04-06 | Denial of Service | malformed JSON crashes ListAvailableOrganizationsAsync | mitigate | broad catch returns empty list; UI handles empty gracefully |
| T-27-04-07 | Elevation of Privilege | n/a | accept | switching orgs requires existing valid session token; no privilege gain |
| T-27-04-08 | Tampering | suppress-flag persistence | accept | in-memory only per D-OG-05; no on-disk surface to tamper |
</threat_model>

<verification>
Pre-build:
  - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` clean
  - `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests|FullyQualifiedName~OrgMismatchSoftPromptTests|FullyQualifiedName~BannerStackPolicyTests" --nologo` all green
  - `MessengerThreadingConventionTests` (Phase 24 G-1) passes — confirms new IRecipient<OpenOrgPickerRequestedMessage> in SettingsViewModel uses _dispatcherQueue.TryEnqueue

Manual smoke (Task 4):
  - Settings → Account → Re-detect → ContentDialog opens with org list
  - Switch flow → LoginView appears (cookie jar cleared per PITFALLS B2)
  - Localization correct in DE + EN
  - Suppress-checkbox flag in-memory only (resets on restart)
</verification>

<success_criteria>
1. ORGID-01: Re-detect button on Account tab opens ContentDialog with org list
2. ORGID-02: org switch persists new uuid + triggers Logout (mandatory re-auth per cookie jar)
3. ORGID-03: 5 consecutive zero-utilization polls trigger InfoBar soft-prompt (active session required)
4. ORGID-04: suppress-checkbox dismisses for session only — NOT persisted
5. ORGID-05: all UI strings localized in DE + EN (8 resw key pairs total)
6. PITFALLS B2 honored: cookie jar reset enforced via AuthStateChangedMessage(false) broadcast (no shortcut path exists)
7. Build green, all tests green, visual smoke approved
</success_criteria>

<output>
After completion, create `.planning/phases/27-nextwin-orgid-pricing-l10n/27-04-SUMMARY.md` documenting:
- 13 files modified
- OrganizationInfo model + OpenOrgPickerRequestedMessage creation
- ListAvailableOrganizationsAsync extraction (DRY refactor of TryMigrateOrgIdAsync)
- 8 resw key pairs added
- 4 ObservableProperty / field additions to MainViewModel
- 2 RelayCommands on MainViewModel + 1 RelayCommand on SettingsViewModel
- ContentDialog implementation pattern (XamlRoot, dispatcher marshaling, event-bridge for showing dialog)
- DI pattern decision: AuthStateChangedMessage(false) broadcast (NOT direct MainViewModel.LogoutCommand call) — addresses the D-13 lesson explicitly
- OrgMismatchSoftPromptTests creation (5 test cases)
- Visual smoke results (Task 4)
</output>

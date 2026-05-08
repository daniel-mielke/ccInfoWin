---
phase: 26-persistent-session-renaming
plan: 03
type: execute
wave: 3
depends_on: [26-02]
files_modified:
  - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
  - CCInfoWindows/CCInfoWindows/Models/SessionRenameItem.cs
  - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
  - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  - CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs
autonomous: false
requirements: [RENAME-02, RENAME-04, RENAME-05, RENAME-06, RENAME-08]
user_setup: []
must_haves:
  truths:
    - "SettingsView has a 5th SegmentedControl item between Account (index 2) and About (index 3); About moves to index 4"
    - "The Sessions tab lists every session known to IJsonlService.Sessions plus orphan entries from ISessionNameStore (RENAME-06 — orphans visible as greyed-out rows)"
    - "Each row shows: ProjectDirName / DefaultName | TextBox(custom name) | Clear button"
    - "TextBox commit on LostFocus OR Enter persists via _sessionNameStore.SetCustomName + SaveAsync; empty value clears (D-04)"
    - "SettingsViewModel takes ISessionNameStore + IJsonlService via constructor injection"
    - "SessionRenameItems is a snapshot collection refreshed on tab activation (CD-03), NOT a live ObservableCollection sync"
    - "Refresh on NameChanged event AND on tab activation; handler wraps in IDispatcherQueue.TryEnqueue per G-1"
    - "5 new resw key pairs exist in BOTH de-DE and en-US: SettingsTabSessions.[ToolTip], Settings.Sessions.Header, Settings.Sessions.NoSessions, Settings.Sessions.OrphanLabel, Settings.Sessions.ClearButton"
    - "5-tab Segmented Control fits within 360px window width (CD-01 — verify during the layout spike; fallback to 28x28 badges if clipping observed)"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Models/SessionRenameItem.cs"
      provides: "Row model for Settings Sessions tab"
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs"
      provides: "SessionRenameItems collection + SaveCustomName/ClearCustomName commands + ISessionNameStore injection"
    - path: "CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml"
      provides: "5th SegmentedControlItem TabSessions + Sessions panel ItemsControl"
  key_links:
    - from: "SettingsView SegmentedItem #5"
      to: "SettingsViewModel.IsSessionsTabVisible"
      via: "selector index 3 (about shifts to 4)"
      pattern: "IsSessionsTabVisible"
    - from: "Sessions tab TextBox LostFocus / Enter"
      to: "SettingsViewModel.SaveSessionCustomNameCommand"
      via: "code-behind event → RelayCommand"
      pattern: "SaveSessionCustomName"
    - from: "ISessionNameStore.NameChanged"
      to: "SettingsViewModel.RefreshSessionRenameItems"
      via: "+= in OnLoaded, -= in OnUnloaded; marshalled via IDispatcherQueue"
      pattern: "RefreshSessionRenameItems"
---

<objective>
Add the secondary rename surface: a 5th "Sessions" tab in the Settings SegmentedControl (inserted between Account and About) listing every known session with inline-editable custom name TextBoxes. Edits commit on LostFocus or Enter; empty TextBox clears the custom name. Orphan entries (custom names whose JSONL files are gone) appear as greyed-out rows with a "Session not found" subtitle, satisfying RENAME-06's "orphans kept across launches" rule with explicit visibility per D-08.

Purpose: Plan 02 ships the inline pencil for "rename one session"; Plan 03 ships the bulk-management view for "see and edit all my custom names in one place". Both surfaces share the same `ISessionNameStore` singleton, so a rename in one immediately reflects in the other through the NameChanged event.

Output: new SessionRenameItem row model, SettingsViewModel additions (ISessionNameStore + IJsonlService injection, SessionRenameItems collection, SaveSessionCustomName/ClearSessionCustomName RelayCommands, OnLoaded/OnUnloaded subscription lifecycle), 5th SegmentedItem + Sessions panel in SettingsView.xaml + xaml.cs, 5 new resw key pairs, ResourceCoverageTests extension, focused SettingsViewModelTests for the rename surface.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@CLAUDE.md
@.planning/phases/26-persistent-session-renaming/26-CONTEXT.md
@.planning/research/PITFALLS.md
@.planning/phases/26-persistent-session-renaming/26-01-SUMMARY.md
@.planning/phases/26-persistent-session-renaming/26-02-SUMMARY.md

@CCInfoWindows/CCInfoWindows/Services/Interfaces/ISessionNameStore.cs
@CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs
@CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
@CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
@CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs
@CCInfoWindows/CCInfoWindows/Helpers/SessionNameSanitizer.cs
@CCInfoWindows/CCInfoWindows/Models/SessionInfo.cs

<interfaces>
SettingsViewModel currently exposes (relevant subset):
```csharp
public bool IsGeneralTabVisible => _selectedTabIndex == 0;
public bool IsUpdatesTabVisible => _selectedTabIndex == 1;
public bool IsAccountTabVisible => _selectedTabIndex == 2;
public bool IsAboutTabVisible  => _selectedTabIndex == 3;

public const int AboutTabIndex = 3;
```

After Plan 03:
- General = 0
- Updates = 1
- Account = 2
- Sessions = 3 (NEW)
- About = 4 (was 3)
- AboutTabIndex constant updates to 4 — every site that references it must be checked.

SettingsView code-behind currently has `OnSegmentedSelectionChanged` and `OnUnloaded` (see existing handlers in SettingsView.xaml). The About-tab DispatcherTimer lifecycle from Phase 22 must continue to start/stop based on AboutTabIndex == 4.

JsonlService exposes `IReadOnlyList<SessionInfo> Sessions` and a `DataUpdated` event used by MainViewModel; SettingsViewModel can read it on tab activation (snapshot per CD-03).

SessionInfo carries `Id` (encoded projectDirName, the SessionNameStore key) and `DisplayName` (auto-derived).
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: SessionRenameItem row model + SettingsViewModel injection + SessionRenameItems collection + commands + AboutTabIndex shift (RENAME-02, RENAME-04, RENAME-05, RENAME-08)</name>
  <read_first>
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs (full file — note AboutTabIndex constant at line 39, IsXxxTabVisible properties, OnSelectedTabIndexChanged at line 59, OnLoaded/OnUnloaded patterns from Phase 22)
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs (Sessions property + DataUpdated event)
    - .planning/phases/26-persistent-session-renaming/26-CONTEXT.md (D-04, CD-03, D-08 orphan visibility)
    - .planning/research/PITFALLS.md (A2-P3 cross-tab live-update — singleton ISessionNameStore is the channel)
    - CLAUDE.md (Clean Code: Small functions, DRY; MVVM Conventions: G-1)
  </read_first>
  <files>
    CCInfoWindows/CCInfoWindows/Models/SessionRenameItem.cs,
    CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs,
    CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs
  </files>
  <behavior>
    - SessionRenameItem is an ObservableObject record-like class with `SessionId` (string), `DefaultName` (string), `IsOrphan` (bool), and `[ObservableProperty] CustomName` (string).
    - SettingsViewModel constructor adds 2 new dependencies: `ISessionNameStore sessionNameStore` and `IJsonlService jsonlService` plus `IDispatcherQueue dispatcherQueue` (if not already injected).
    - `ObservableCollection<SessionRenameItem> SessionRenameItems` is exposed and populated by `RefreshSessionRenameItems()`.
    - `RefreshSessionRenameItems` builds a snapshot:
        - For every `SessionInfo s` in `_jsonlService.Sessions`: yield `new SessionRenameItem { SessionId = s.Id, DefaultName = s.DisplayName, IsOrphan = false, CustomName = _sessionNameStore.GetCustomName(s.Id) ?? "" }`.
        - Then for every key in the store NOT covered by a session: yield `new SessionRenameItem { SessionId = key, DefaultName = key, IsOrphan = true, CustomName = _sessionNameStore.GetCustomName(key) ?? "" }`.
        - Sorted by IsOrphan ascending, then DefaultName ascending.
    - `[RelayCommand] SaveSessionCustomName(SessionRenameItem item)` calls `_sessionNameStore.SetCustomName(item.SessionId, SessionNameSanitizer.Strip(item.CustomName).Trim())` followed by `await _sessionNameStore.SaveAsync()`. Empty/whitespace value calls `ClearCustomName` instead.
    - `[RelayCommand] ClearSessionCustomName(SessionRenameItem item)` calls `_sessionNameStore.ClearCustomName(item.SessionId)` + `await SaveAsync()`.
    - SettingsViewModel subscribes to `_sessionNameStore.NameChanged += OnStoreNameChanged` in a public `Activate()` method (called from view's OnLoaded) and unsubscribes in `Deactivate()` (called from view's OnUnloaded). G-1: handler wraps RefreshSessionRenameItems in `_dispatcherQueue.TryEnqueue`.
    - `IsSessionsTabVisible => _selectedTabIndex == 3` is added; `IsAboutTabVisible => _selectedTabIndex == 4`. `AboutTabIndex` constant updates from 3 to 4. Every other site that depends on `AboutTabIndex` (the About-tab DispatcherTimer plumbing from Phase 22) gets the new value automatically because they reference the constant.
    - `OnSelectedTabIndexChanged` notifies the new IsSessionsTabVisible alongside the existing 4 properties.
    - When the user navigates TO the Sessions tab (tab index becomes 3), `RefreshSessionRenameItems` is called once for snapshot freshness (CD-03).
    - SettingsViewModelTests gains coverage for: tab-index visibility shift; RefreshSessionRenameItems composition (sessions + orphans); SaveSessionCustomName empty input clears; SaveSessionCustomName with control chars sanitizes; ClearSessionCustomName removes entry.
  </behavior>
  <action>
    **Step 1a — Create `CCInfoWindows/CCInfoWindows/Models/SessionRenameItem.cs`**:

    ```csharp
    using CommunityToolkit.Mvvm.ComponentModel;

    namespace CCInfoWindows.Models;

    /// <summary>
    /// Row model for the Settings → Sessions tab. CustomName is two-way bound to a TextBox
    /// in SettingsView; the View's LostFocus / Enter handler invokes SaveSessionCustomNameCommand
    /// to persist via ISessionNameStore.
    /// </summary>
    public partial class SessionRenameItem : ObservableObject
    {
        public required string SessionId { get; init; }
        public required string DefaultName { get; init; }
        public bool IsOrphan { get; init; }

        [ObservableProperty]
        private string _customName = string.Empty;
    }
    ```

    **Step 1b — Update `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs`**:

    a. Add `using` directives: `CCInfoWindows.Helpers;`, `System.Collections.ObjectModel;`, `CCInfoWindows.Models;`.

    b. Add fields and update constructor:
    ```csharp
    private readonly ISessionNameStore _sessionNameStore;       // Phase 26 / RENAME-07
    private readonly IJsonlService _jsonlService;               // Phase 26 / SessionRenameItems source
    private readonly IDispatcherQueue _dispatcherQueue;         // Phase 26 / G-1
    ```

    The constructor must accept these new dependencies. Locate the existing parameterless / DI-resolved constructor in SettingsViewModel and extend its signature to take the 3 new services. Update App.xaml.cs `services.AddTransient<SettingsViewModel>();` if it currently uses no factory — switch to a factory expression:
    ```csharp
    services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(
        sp.GetRequiredService<ISettingsService>(),
        sp.GetRequiredService<ICredentialService>(),
        sp.GetRequiredService<INavigationService>(),
        sp.GetRequiredService<IPricingService>(),
        sp.GetRequiredService<IUsageHistoryService>(),
        sp.GetRequiredService<ISessionNameStore>(),
        sp.GetRequiredService<IJsonlService>(),
        sp.GetRequiredService<IDispatcherQueue>()));
    ```

    (Confirm the current constructor's exact parameter list by reading the file before editing. The 5 services listed above match the field declarations near the top of SettingsViewModel.cs that you read in <read_first>.)

    c. Update tab-index constants and visibility:
    ```csharp
    // Tab order: 0=General, 1=Updates, 2=Account, 3=Sessions (Phase 26 / RENAME-02), 4=About
    public const int SessionsTabIndex = 3;
    public const int AboutTabIndex = 4;   // SHIFTED from 3 — Phase 26 inserts Sessions at index 3

    public bool IsGeneralTabVisible => _selectedTabIndex == 0;
    public bool IsUpdatesTabVisible => _selectedTabIndex == 1;
    public bool IsAccountTabVisible => _selectedTabIndex == 2;
    public bool IsSessionsTabVisible => _selectedTabIndex == 3;   // Phase 26 / RENAME-02
    public bool IsAboutTabVisible    => _selectedTabIndex == 4;
    ```

    d. Update `OnSelectedTabIndexChanged` to notify the new property and refresh on Sessions tab activation:
    ```csharp
    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsGeneralTabVisible));
        OnPropertyChanged(nameof(IsUpdatesTabVisible));
        OnPropertyChanged(nameof(IsAccountTabVisible));
        OnPropertyChanged(nameof(IsSessionsTabVisible));   // Phase 26
        OnPropertyChanged(nameof(IsAboutTabVisible));

        // CD-03: snapshot refresh on tab activation (NOT live ObservableCollection sync).
        if (value == SessionsTabIndex)
        {
            RefreshSessionRenameItems();
        }
    }
    ```

    e. Add the SessionRenameItems collection and refresh method:
    ```csharp
    /// <summary>
    /// Snapshot collection (CD-03) refreshed on tab activation and on ISessionNameStore.NameChanged.
    /// NOT live-synced with IJsonlService.Sessions to avoid stale-snapshot bug class (PITFALLS Cluster A).
    /// </summary>
    public ObservableCollection<SessionRenameItem> SessionRenameItems { get; } = new();

    private void RefreshSessionRenameItems()
    {
        var liveSessions = _jsonlService.Sessions;
        var liveIds = new HashSet<string>(liveSessions.Select(s => s.Id), StringComparer.Ordinal);

        SessionRenameItems.Clear();

        // Live sessions first
        foreach (var s in liveSessions.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            SessionRenameItems.Add(new SessionRenameItem
            {
                SessionId = s.Id,
                DefaultName = s.DisplayName,
                IsOrphan = false,
                CustomName = _sessionNameStore.GetCustomName(s.Id) ?? string.Empty
            });
        }

        // Orphan custom names (D-08): sessions whose JSONL files are gone but a custom name persists.
        // Detected by enumerating store keys not present in live IJsonlService.Sessions.
        // The store does not expose enumeration; we discover orphans by reading session-names.json
        // through a snapshot-friendly enumeration helper. For Phase 26 v1.5 we keep a minimum-API
        // approach: orphans surface only after a NameChanged event referencing an unknown id.
        // (A future v1.6+ enumeration API on ISessionNameStore is deferred per O-01.)
        // For now, attempt to expose orphans via a best-effort json read in EnumerateOrphanIds().
        foreach (var orphanId in EnumerateOrphanIds(liveIds))
        {
            var custom = _sessionNameStore.GetCustomName(orphanId);
            if (string.IsNullOrEmpty(custom)) continue;
            SessionRenameItems.Add(new SessionRenameItem
            {
                SessionId = orphanId,
                DefaultName = orphanId,   // raw projectDirName as fallback label
                IsOrphan = true,
                CustomName = custom
            });
        }
    }

    private IEnumerable<string> EnumerateOrphanIds(HashSet<string> liveIds)
    {
        // Best-effort orphan discovery: read session-names.json directly. Failure returns empty
        // (orphans hidden until a future event). No exception propagates to the UI.
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CCInfoWindows", "session-names.json");
            if (!File.Exists(path)) yield break;
            var json = File.ReadAllText(path);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null) yield break;
            foreach (var key in dict.Keys)
            {
                if (!liveIds.Contains(key)) yield return key;
            }
        }
        finally { }
    }
    ```

    NOTE on EnumerateOrphanIds: this helper bypasses ISessionNameStore intentionally — adding an enumeration API to the store interface is out of scope for v1.5 per O-01. The read is best-effort and runs only on tab-activation snapshot refresh (low frequency). Document inline.

    f. Add the RelayCommands and lifecycle hooks:
    ```csharp
    [RelayCommand]
    private async Task SaveSessionCustomName(SessionRenameItem item)
    {
        if (item == null) return;
        var sanitized = SessionNameSanitizer.Strip(item.CustomName).Trim();
        if (string.IsNullOrEmpty(sanitized))
        {
            _sessionNameStore.ClearCustomName(item.SessionId);
        }
        else
        {
            _sessionNameStore.SetCustomName(item.SessionId, sanitized);
            // Reflect sanitized value back to the bound TextBox (e.g. control chars stripped):
            item.CustomName = sanitized;
        }
        await _sessionNameStore.SaveAsync();
    }

    [RelayCommand]
    private async Task ClearSessionCustomName(SessionRenameItem item)
    {
        if (item == null) return;
        _sessionNameStore.ClearCustomName(item.SessionId);
        item.CustomName = string.Empty;
        await _sessionNameStore.SaveAsync();
    }

    /// <summary>Called from SettingsView.OnLoaded — subscribe to NameChanged + initial snapshot.</summary>
    public void Activate()
    {
        _sessionNameStore.NameChanged += OnStoreNameChanged;
        if (IsSessionsTabVisible) RefreshSessionRenameItems();
    }

    /// <summary>Called from SettingsView.OnUnloaded — unsubscribe to prevent zombie handlers.</summary>
    public void Deactivate()
    {
        _sessionNameStore.NameChanged -= OnStoreNameChanged;
    }

    private void OnStoreNameChanged(object? sender, SessionNameChangedEventArgs args)
    {
        // G-1: NameChanged may arrive off-thread.
        _dispatcherQueue.TryEnqueue(RefreshSessionRenameItems);
    }
    ```

    g. Update `App.xaml.cs` SettingsViewModel registration to use the factory described in step 1b above.

    **Step 1c — Tests** in `CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs`:

    Add (using existing harness pattern in that file):

    - `TabIndex_Three_IsSessionsTab_NotAbout`: set SelectedTabIndex=3, assert IsSessionsTabVisible==true and IsAboutTabVisible==false.
    - `TabIndex_Four_IsAboutTab`: set SelectedTabIndex=4, assert IsAboutTabVisible==true.
    - `RefreshSessionRenameItems_PopulatesFromJsonlService`: setup IJsonlService.Sessions returning 2 sessions; setup ISessionNameStore.GetCustomName returning "Custom1" for session1, null for session2; activate Sessions tab; assert SessionRenameItems has 2 items with correct CustomName values.
    - `SaveSessionCustomName_StripsControlCharsAndPersists`: setup item.CustomName="Bad X" (containing U+0009); invoke SaveSessionCustomNameCommand; assert ISessionNameStore.SetCustomName called with "BadX"; assert SaveAsync called.
    - `SaveSessionCustomName_EmptyValueClears`: invoke command with item.CustomName=""; assert ISessionNameStore.ClearCustomName called.
    - `ClearSessionCustomName_RemovesEntry`: invoke command; assert ISessionNameStore.ClearCustomName called and item.CustomName is "".
    - `Activate_SubscribesToNameChanged_DeactivateUnsubscribes`: trigger event manually before/after Activate / Deactivate; assert refresh count delta.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~SettingsViewModelTests" --nologo</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MessengerThreadingConventionTests" --nologo</automated>
  </verify>
  <acceptance_criteria>
    - File `CCInfoWindows/CCInfoWindows/Models/SessionRenameItem.cs` exists with required SessionId, DefaultName, IsOrphan, [ObservableProperty] CustomName.
    - `grep -c "AboutTabIndex = 4" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` is 1 (constant updated from 3 to 4).
    - `grep -c "SessionsTabIndex = 3" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` is 1.
    - `grep -c "IsSessionsTabVisible" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` is at least 3 (declaration, OnPropertyChanged, Activate guard).
    - `grep -c "ObservableCollection<SessionRenameItem>" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` is 1.
    - `grep -c "_sessionNameStore" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` is at least 5.
    - `grep -c "_dispatcherQueue.TryEnqueue(RefreshSessionRenameItems)" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` is 1 (G-1 in OnStoreNameChanged).
    - `grep -c "GetRequiredService<ISessionNameStore>" CCInfoWindows/CCInfoWindows/App.xaml.cs` is at least 2 (MainViewModel from Plan 02 + SettingsViewModel from Plan 03).
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` succeeds with 0 errors.
    - `dotnet test --filter "FullyQualifiedName~SettingsViewModelTests"` reports the 7 new tests passing alongside existing tests.
    - Phase 24 `MessengerThreadingConventionTests` still passes.
    - Phase 22 About-tab DispatcherTimer tests (`SettingsViewModelTimerTests`) still pass — AboutTabIndex constant change propagates correctly.
  </acceptance_criteria>
  <done>
    SettingsViewModel exposes SessionRenameItems + Save/Clear commands with G-1-compliant NameChanged handling; About tab shifts to index 4; existing Phase 22 timer tests untouched; new SettingsViewModel tests cover the 7 behaviors above.
  </done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: SettingsView 5th SegmentedItem + Sessions panel + 5 resw key pairs (RENAME-02)</name>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml (full structure — 4 SegmentedItem + 4 panels with Visibility binding)
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs (existing OnLoaded, OnSegmentedSelectionChanged, OnUnloaded)
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw (existing Settings.* and Tab* keys)
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - .planning/phases/26-persistent-session-renaming/26-CONTEXT.md (D-04 layout, CD-01 360px verification)
    - CLAUDE.md (MVVM Conventions; secure-coding "No dynamic execution of user data" — TextBox content goes only to ISessionNameStore.SetCustomName which sanitizes)
  </read_first>
  <files>
    CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml,
    CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs,
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  </files>
  <behavior>
    - SettingsView Segmented control has 5 items in order: General, Updates, Account, Sessions (NEW), About.
    - The Sessions tab uses a purple/violet badge (#8B5CF6 or theme-resource SettingsBadgePurpleBrush — define in App.xaml or Styles if absent). Badge size 30x30 same as siblings (CD-01).
    - The Sessions panel binds `Visibility` to `IsSessionsTabVisible`.
    - The panel contains a header `<TextBlock l:Uids.Uid="Settings.Sessions.Header" />`, then an `ItemsControl` ItemsSource bound to `ViewModel.SessionRenameItems`. Each row is a 3-column Grid (DefaultName | TextBox CustomName | Clear Button).
    - Empty state placeholder `<TextBlock l:Uids.Uid="Settings.Sessions.NoSessions" />` is visible when SessionRenameItems is empty.
    - Orphan rows have Opacity=0.5 and a small subtitle bound to `Settings.Sessions.OrphanLabel` ("Sitzung nicht gefunden" / "Session not found").
    - TextBox commits on LostFocus (handler in code-behind invokes `ViewModel.SaveSessionCustomNameCommand`) and on Enter (KeyDown handler does the same).
    - Clear Button is enabled only when CustomName is non-empty.
    - SettingsView.xaml.cs OnLoaded calls `ViewModel.Activate()`; OnUnloaded calls `ViewModel.Deactivate()`. The existing About-tab DispatcherTimer logic stays intact.
    - 5 new resw key pairs exist in BOTH locales (DE+EN parity verified by ResourceCoverageTests).
    - At 360px window width the 5-tab segmented control does NOT clip — verified via SettingsView code-behind comment + manual checkpoint. If clipping is observed, fall back to 28x28 badge size and document in PROJECT.md Key Decisions.
  </behavior>
  <action>
    **Step 2a — `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml`**:

    a. Inside the existing `<controls:Segmented>` (after the Account `<controls:SegmentedItem x:Name="TabAccount">` block, before `<controls:SegmentedItem x:Name="TabAbout">`), insert a new SegmentedItem:

    ```xml
    <!-- Sessions tab (purple badge) — Phase 26 / RENAME-02 -->
    <controls:SegmentedItem x:Name="TabSessions">
        <controls:SegmentedItem.Content>
            <Border Width="30" Height="30" CornerRadius="6"
                    Background="{ThemeResource SettingsBadgePurpleBrush}">
                <FontIcon Glyph="&#xE70F;" FontSize="16" Foreground="White" />
            </Border>
        </controls:SegmentedItem.Content>
    </controls:SegmentedItem>
    ```

    Verify `SettingsBadgePurpleBrush` exists in `App.xaml` ResourceDictionary. If not, add it in both Default + Light theme dictionaries:
    ```xml
    <SolidColorBrush x:Key="SettingsBadgePurpleBrush" Color="#8B5CF6" />
    ```

    b. Inside the panel `<Grid>` row 2 (the area that contains the 4 existing tab StackPanels), add a 5th panel BEFORE the About-tab panel:

    ```xml
    <!-- PANEL 4 — Sessions Tab — Phase 26 / RENAME-02 -->
    <StackPanel Spacing="8"
                Visibility="{x:Bind ViewModel.IsSessionsTabVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">

        <TextBlock l:Uids.Uid="Settings.Sessions.Header"
                   FontSize="11" FontWeight="SemiBold"
                   Foreground="{ThemeResource SectionHeaderBrush}"
                   CharacterSpacing="50" Margin="0,0,0,8" />

        <!-- Empty-state -->
        <TextBlock l:Uids.Uid="Settings.Sessions.NoSessions"
                   FontSize="13"
                   Foreground="{ThemeResource SecondaryTextBrush}"
                   HorizontalAlignment="Center"
                   Margin="0,12,0,0">
            <TextBlock.Visibility>
                <Binding Path="SessionRenameItems.Count" Converter="{StaticResource ZeroToVisibilityConverter}" />
            </TextBlock.Visibility>
        </TextBlock>

        <Border CornerRadius="8"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
            <ItemsControl ItemsSource="{x:Bind ViewModel.SessionRenameItems, Mode=OneWay}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="models:SessionRenameItem">
                        <Grid Padding="12,6"
                              ColumnSpacing="8"
                              Opacity="{x:Bind IsOrphan, Converter={StaticResource OrphanOpacityConverter}}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>

                            <TextBlock Grid.Column="0" Grid.Row="0"
                                       Text="{x:Bind DefaultName}"
                                       FontSize="13"
                                       Foreground="{ThemeResource PrimaryTextBrush}"
                                       TextTrimming="CharacterEllipsis"
                                       VerticalAlignment="Center" />

                            <!-- Orphan subtitle (visible only when IsOrphan==true) -->
                            <TextBlock Grid.Column="0" Grid.Row="1"
                                       l:Uids.Uid="Settings.Sessions.OrphanLabel"
                                       FontSize="11"
                                       Foreground="{ThemeResource TertiaryTextBrush}"
                                       Visibility="{x:Bind IsOrphan, Converter={StaticResource BoolToVisibilityConverter}}" />

                            <TextBox Grid.Column="1" Grid.RowSpan="2"
                                     Text="{x:Bind CustomName, Mode=TwoWay}"
                                     MaxLength="100"
                                     LostFocus="OnSessionRenameTextBoxLostFocus"
                                     KeyDown="OnSessionRenameTextBoxKeyDown"
                                     Tag="{x:Bind}"
                                     VerticalAlignment="Center" />

                            <Button Grid.Column="2" Grid.RowSpan="2"
                                    l:Uids.Uid="Settings.Sessions.ClearButton"
                                    Command="{Binding DataContext.ClearSessionCustomNameCommand,
                                              ElementName=SettingsRootGrid}"
                                    CommandParameter="{x:Bind}"
                                    Background="Transparent"
                                    BorderThickness="0"
                                    VerticalAlignment="Center">
                                <FontIcon Glyph="&#xE894;" FontSize="14" />
                            </Button>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Border>
    </StackPanel>
    ```

    Add to the Page declaration: `xmlns:models="using:CCInfoWindows.Models"` and give the root Grid `x:Name="SettingsRootGrid"` so the Clear button binding resolves the DataContext path correctly.

    Add a converter `OrphanOpacityConverter` in `Converters/` that returns `0.5` when bound bool is true, `1.0` otherwise. Register in `App.xaml` resources.

    Add a converter `ZeroToVisibilityConverter` if not already present (returns Visible when int==0).

    **Step 2b — `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs`**:

    a. In `OnLoaded` (existing handler from Phase 22), after the existing About-tab logic, add:
    ```csharp
    ViewModel.Activate();   // Phase 26: subscribe to NameChanged + snapshot if Sessions tab visible
    ```

    b. In `OnUnloaded`, add BEFORE existing timer cleanup:
    ```csharp
    ViewModel.Deactivate();   // Phase 26: unsubscribe NameChanged
    ```

    c. Add the two new TextBox event handlers:
    ```csharp
    private async void OnSessionRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is SessionRenameItem item)
        {
            await ViewModel.SaveSessionCustomNameCommand.ExecuteAsync(item);
        }
    }

    private async void OnSessionRenameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        if (sender is TextBox tb && tb.Tag is SessionRenameItem item)
        {
            e.Handled = true;
            await ViewModel.SaveSessionCustomNameCommand.ExecuteAsync(item);
            // Move focus off the TextBox so the user sees the commit visually
            (sender as TextBox)?.IsEnabled = (sender as TextBox)?.IsEnabled ?? true;
        }
    }
    ```

    Required `using` directives: `Windows.System;`, `CCInfoWindows.Models;`.

    **Step 2c — Add 5 resw key pairs**:

    DE values (insert in `Strings/de-DE/Resources.resw`):
    | Key | Value |
    |-----|-------|
    | `TabSessions.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | `Sitzungen` |
    | `Settings.Sessions.Header` | `Eigene Sitzungsnamen` |
    | `Settings.Sessions.NoSessions` | `Keine Sitzungen verfügbar.` |
    | `Settings.Sessions.OrphanLabel` | `Sitzung nicht gefunden` |
    | `Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | `Eigenen Namen entfernen` |

    EN values (insert in `Strings/en-US/Resources.resw`):
    | Key | Value |
    |-----|-------|
    | `TabSessions.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | `Sessions` |
    | `Settings.Sessions.Header` | `Custom session names` |
    | `Settings.Sessions.NoSessions` | `No sessions available.` |
    | `Settings.Sessions.OrphanLabel` | `Session not found` |
    | `Settings.Sessions.ClearButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | `Remove custom name` |

    **Step 2d — Extend ResourceCoverageTests** to include the 5 new keys (same enumeration pattern as Plan 02 Task 2 step 2e).
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests" --nologo</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --nologo</automated>
  </verify>
  <acceptance_criteria>
    - `grep -c "TabSessions" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` is at least 1.
    - `grep -c "IsSessionsTabVisible" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` is 1 (panel Visibility binding).
    - `grep -c "OnSessionRenameTextBoxLostFocus" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs` is 1.
    - `grep -c "OnSessionRenameTextBoxKeyDown" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs` is 1.
    - `grep -c "ViewModel.Activate()" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs` is 1.
    - `grep -c "ViewModel.Deactivate()" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs` is 1.
    - `grep -c "Settings.Sessions.Header" CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` is 1.
    - `grep -c "Settings.Sessions.Header" CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` is 1.
    - `grep -c "Settings.Sessions.OrphanLabel" CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` is 1.
    - `grep -c "Settings.Sessions.OrphanLabel" CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` is 1.
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` succeeds with 0 errors.
    - `dotnet test --filter "FullyQualifiedName~ResourceCoverageTests"` reports all 5 keys present in both locales.
    - `dotnet test --nologo` baseline holds.
  </acceptance_criteria>
  <done>
    Settings 5-tab Segmented Control with Sessions panel renders, TextBox commit on LostFocus + Enter persists via ISessionNameStore, orphans show as greyed-out rows, 5 resw keys validated in both locales.
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 3: Manual smoke for Sessions Settings tab + 360px layout fit + cross-tab live update (RENAME-02, RENAME-04, CD-01)</name>
  <what-built>
    5th "Sessions" tab in Settings between Account and About; row-per-session list with inline-editable TextBox; LostFocus/Enter persists via ISessionNameStore; orphans visible as greyed-out rows; cross-tab live update (rename in Settings reflects in MainView dropdown immediately because both share the singleton store + .NET event).
  </what-built>
  <how-to-verify>
    1. Run: `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`.
    2. Login if necessary; let the dropdown populate.
    3. Open Settings (gear icon). Confirm: 5 segmented tabs visible — General, Updates, Account, Sessions, About — with badge colors green/blue/red/purple/orange.
    4. CD-01 layout check: confirm the 5-tab Segmented Control fits within the 360px window without clipping. If clipping is observed, halt the smoke and report; the executor will fall back to 28x28 badges per CD-01.
    5. Click the Sessions tab. Confirm a list appears with one row per session, each row showing: DefaultName | TextBox (likely empty or pre-filled if Plan 02 already saved one) | small Clear button.
    6. Pick a session row. Type "Mein Wichtiges Projekt" in the TextBox. Press Tab (LostFocus). Confirm the TextBox value is preserved.
    7. Switch to MainView (Back button). Confirm the ComboBox now shows "Mein Wichtiges Projekt" for that session — without app restart (RENAME-04 cross-tab live update).
    8. Reopen Settings → Sessions. Find the same row. Clear the TextBox (delete all text). Press Enter. Confirm the TextBox is empty AND the row's auto-derived label remains visible (MainView dropdown reverts to auto-derived name).
    9. Type a name with control chars (paste a string containing `\t` or `\n` if possible). LostFocus. Confirm: the saved value in the TextBox (after roundtrip) is sanitized — control chars stripped (RENAME-05).
    10. Click the Clear Button (trash/x glyph) on a row that has a custom name. Confirm: TextBox empties, MainView dropdown reverts to auto-derived.
    11. Close the app. Manually delete a session's JSONL files from `%USERPROFILE%\.claude\projects\<projectDir>\` (one of the sessions you renamed earlier).
    12. Relaunch. Open Settings → Sessions. Confirm: the deleted session appears as an orphan row (greyed-out, "Sitzung nicht gefunden" subtitle) with its custom name still in the TextBox (RENAME-06 orphans kept).
    13. Switch between General → Updates → Account → Sessions → About → Sessions multiple times. Confirm: no flicker, no exceptions in `%LOCALAPPDATA%\CCInfoWindows\crash.log`.
    14. About tab still works (Phase 22 DispatcherTimer): visit About, confirm the "X minutes ago" timestamp updates each minute.
  </how-to-verify>
  <resume-signal>
    Type "approved" if all 14 steps pass. If step 4 reveals 360px clipping, report — executor will switch to 28x28 badges and document the fallback in PROJECT.md per CD-01. Any other deviation routes to gap closure.
  </resume-signal>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| TextBox input ←→ SettingsViewModel | Untrusted; sanitized via SessionNameSanitizer.Strip + Trim before persistence. |
| session-names.json read in EnumerateOrphanIds ←→ SettingsViewModel | Untrusted file content; failure returns empty enumeration (no exception leak). |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-26-11 | Tampering | Sessions tab TextBox | mitigate | SessionNameSanitizer.Strip + Trim in SaveSessionCustomName command (UI belt) + SessionNameStore.SetCustomName (store suspenders, Plan 01). |
| T-26-12 | DoS | Malformed session-names.json crashes EnumerateOrphanIds | mitigate | Helper is wrapped in try/yield-pattern; deserialization failure returns empty enumeration; no exception bubbles to UI. |
| T-26-13 | Information Disclosure | Orphan rows expose deleted-session projectDirNames | accept | projectDirName is derived from local filesystem path the user already owns (no external secret). User-visible by design per D-08. |
| T-26-14 | Repudiation | Settings NameChanged subscriber leak across SettingsView.OnUnloaded | mitigate | OnUnloaded calls Deactivate() which unsubscribes; symmetric with OnLoaded → Activate(). |
| T-26-15 | DoS | LostFocus + Enter both fire on Enter key | accept | Enter handler sets e.Handled=true; LostFocus is idempotent (SetCustomName for the same value triggers a NameChanged but the snapshot rebuild is cheap O(n) where n is the session count). |
</threat_model>

<verification>
1. `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — 0 errors.
2. `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --nologo` — full suite passes (subject to documented pre-existing baselines).
3. `dotnet test --filter "FullyQualifiedName~SettingsViewModelTests"` — 7 new test cases green.
4. `dotnet test --filter "FullyQualifiedName~SettingsViewModelTimerTests"` — Phase 22 timer logic still passes after AboutTabIndex shift.
5. `dotnet test --filter "FullyQualifiedName~ResourceCoverageTests"` — DE+EN parity validated for the 5 new Plan 03 keys + the 5 Plan 02 keys.
6. `dotnet test --filter "FullyQualifiedName~MessengerThreadingConventionTests"` — Phase 24 convention not regressed.
7. Manual smoke per Task 3 — 14 steps green, 360px layout confirmed, cross-tab live update confirmed.
</verification>

<success_criteria>
- [x] SessionRenameItem row model exists with required fields + [ObservableProperty] CustomName.
- [x] SettingsViewModel injects ISessionNameStore + IJsonlService + IDispatcherQueue (RENAME-07 dependency wiring).
- [x] AboutTabIndex shifts from 3 to 4; SessionsTabIndex = 3; both visibility properties published correctly (RENAME-02).
- [x] SessionRenameItems is a snapshot collection refreshed on Sessions-tab activation + on NameChanged (CD-03).
- [x] SaveSessionCustomNameCommand sanitizes via SessionNameSanitizer.Strip + Trim (RENAME-05).
- [x] ClearSessionCustomNameCommand removes the entry and reverts CustomName binding to "".
- [x] SettingsView 5th SegmentedItem with purple badge inserted between Account and About.
- [x] Sessions panel renders ItemsControl with 3-column rows + LostFocus/Enter handlers.
- [x] Orphan rows render with Opacity=0.5 + "Session not found" subtitle (D-08, RENAME-06).
- [x] 5 resw key pairs validated by ResourceCoverageTests in DE+EN.
- [x] SettingsViewModelTests gains 7 new green tests; Phase 22 timer tests still pass after AboutTabIndex shift.
- [x] Manual smoke verifies 360px layout fit, cross-tab live update, orphan persistence across restart, control-char stripping.
</success_criteria>

<output>
After completion, create `.planning/phases/26-persistent-session-renaming/26-03-SUMMARY.md` summarizing:
- Files modified
- 360px layout result (default 30x30 OR fallback 28x28 — note in PROJECT.md if fallback was triggered)
- Test counts (7 new SettingsViewModelTests + 5 new resw keys)
- Phase 22 SettingsViewModelTimerTests status post-AboutTabIndex shift
- Manual smoke result
- Phase 26 fully delivered: RENAME-01..08 satisfied across Plans 26-01, 26-02, 26-03
</output>

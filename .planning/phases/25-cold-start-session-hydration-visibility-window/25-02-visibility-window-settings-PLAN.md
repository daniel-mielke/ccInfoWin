---
phase: 25-cold-start-session-hydration-visibility-window
plan: 02
type: execute
wave: 2
depends_on: ["25-01-jsonlservice-hardening"]
files_modified:
  - CCInfoWindows/CCInfoWindows/Models/AppSettings.cs
  - CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs
  - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
autonomous: false
requirements: [DROPDOWN-01, DROPDOWN-04]
must_haves:
  truths:
    - "User sees a new ComboBox in the General Settings tab labeled 'Sichtbarkeitsfenster' / 'Visibility window' with options 7d / 30d / 90d / Unbegrenzt"
    - "Default selection is 30 days (matches research SUMMARY Decision 4)"
    - "Changing the ComboBox value triggers SessionVisibilityChangedMessage and the Active Session ComboBox immediately reflects the new filter"
    - "JsonlService aggregations (cost / quota totals) are NOT affected by the new filter -- only the display layer is filtered"
    - "MainViewModel.Receive(SessionVisibilityChangedMessage) wraps RefreshSessionList in _dispatcherQueue.TryEnqueue (G-1 compliant)"
    - "After cold start the Active Session ComboBox lists all sessions whose LastActivity >= UtcNow - SessionVisibilityWindowDays (or all sessions when value is 0)"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Models/AppSettings.cs"
      provides: "SessionVisibilityWindowDays (default 30) + SessionVisibilityMigrationShown (default false)"
      contains: "SessionVisibilityWindowDays"
    - path: "CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs"
      provides: "ValueChangedMessage<int> mirroring SessionTimeoutChangedMessage"
      contains: "SessionVisibilityChangedMessage"
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs"
      provides: "SelectedVisibilityWindowIndex ComboBox-bound observable + persistence + message emission"
      contains: "SelectedVisibilityWindowIndex"
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      provides: "IRecipient<SessionVisibilityChangedMessage> + visibility filter in RefreshSessionList"
      contains: "IRecipient<SessionVisibilityChangedMessage>"
    - path: "CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml"
      provides: "ComboBox row in General tab between Session Timeout and Dark Mode"
      contains: "VisibilityWindowComboBox"
    - path: "CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw"
      provides: "5 new resw key headers + values (German)"
      contains: "SessionVisibilityWindow.Header"
    - path: "CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw"
      provides: "5 new resw key headers + values (English)"
      contains: "SessionVisibilityWindow.Header"
  key_links:
    - from: "SettingsViewModel.OnSelectedVisibilityWindowIndexChanged"
      to: "WeakReferenceMessenger.Send(new SessionVisibilityChangedMessage(...))"
      via: "ComboBox SelectionChanged -> partial method"
      pattern: "Send\\(new SessionVisibilityChangedMessage"
    - from: "MainViewModel.Receive(SessionVisibilityChangedMessage)"
      to: "_dispatcherQueue.TryEnqueue(RefreshSessionList)"
      via: "G-1 marshaling per Phase 24 L-02"
      pattern: "_dispatcherQueue\\.TryEnqueue\\(RefreshSessionList\\)"
    - from: "MainViewModel.RefreshSessionList"
      to: "DateTimeOffset cutoff filter on latestSessions"
      via: "settings.SessionVisibilityWindowDays > 0 -> UtcNow - days, else MinValue"
      pattern: "SessionVisibilityWindowDays"
---

<objective>
Add a configurable `SessionVisibilityWindowDays` setting (7 / 30 / 90 / 0 = unlimited; default 30) to the General Settings tab. The filter applies at the display layer only -- JsonlService continues to aggregate stats across all sessions, so cost / quota totals are unaffected.

Reactive refresh is wired through `SessionVisibilityChangedMessage` (mirror of `SessionTimeoutChangedMessage`). `MainViewModel` becomes `IRecipient<SessionVisibilityChangedMessage>` and the `Receive` body wraps in `_dispatcherQueue.TryEnqueue(RefreshSessionList)` per Phase 24 G-1 -- which `MessengerThreadingConventionTests` automatically validates.

Purpose: closes DROPDOWN-01 (cold-start ComboBox now lists every session within the configured window) and DROPDOWN-04 (Settings ComboBox + reactive filter).
Output: working ComboBox in Settings + immediate Active Session list refresh on change + 5 resw key pairs.

Plan is `autonomous: false` because the new ComboBox needs visual smoke verification.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/phases/25-cold-start-session-hydration-visibility-window/25-CONTEXT.md
@.planning/phases/25-cold-start-session-hydration-visibility-window/25-01-jsonlservice-hardening-PLAN.md
@CLAUDE.md

@CCInfoWindows/CCInfoWindows/Models/AppSettings.cs
@CCInfoWindows/CCInfoWindows/Messages/SessionTimeoutChangedMessage.cs
@CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
@CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
@CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml

<interfaces>
<!-- Existing patterns the executor mirrors. -->

From CCInfoWindows/CCInfoWindows/Messages/SessionTimeoutChangedMessage.cs (precedent shape):
```csharp
using CommunityToolkit.Mvvm.Messaging.Messages;
namespace CCInfoWindows.Messages;
public class SessionTimeoutChangedMessage : ValueChangedMessage<int>
{
    public SessionTimeoutChangedMessage(int thresholdMinutes) : base(thresholdMinutes) { }
}
```

From CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs (precedent for ComboBox-bound observable + partial change handler + Send):
```csharp
private static readonly int[] ThresholdMinuteOptions = [15, 30, 60, 120];

[ObservableProperty]
private int _selectedThresholdIndex;

partial void OnSelectedThresholdIndexChanged(int value)
{
    var settings = _settingsService.LoadSettings();
    settings.SessionActivityThresholdMinutes = MapThresholdIndexToMinutes(value);
    _settingsService.SaveSettings(settings);
    WeakReferenceMessenger.Default.Send(new SessionTimeoutChangedMessage(settings.SessionActivityThresholdMinutes));
}

private static int MapThresholdIndexToMinutes(int index) =>
    (index >= 0 && index < ThresholdMinuteOptions.Length) ? ThresholdMinuteOptions[index] : ThresholdMinuteOptions[1];

private static int MapMinutesToThresholdIndex(int minutes)
{
    var index = Array.IndexOf(ThresholdMinuteOptions, minutes);
    return index >= 0 ? index : 1;
}
```

From CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs (G-1 compliant Receive precedent at line 1043):
```csharp
public void Receive(SessionTimeoutChangedMessage message)
{
    // Dispatched to UI thread -- RefreshSessionList requires it.
    // G-1 compliant: constructor-injected _dispatcherQueue is non-null.
    _dispatcherQueue.TryEnqueue(RefreshSessionList);
}
```

Class declaration to extend (line 48-50):
```csharp
public partial class MainViewModel : ObservableObject,
    IRecipient<AuthStateChangedMessage>,
    IRecipient<SessionTimeoutChangedMessage>   // D-08
```

InitializeAsync messenger registration block (line 314-316):
```csharp
WeakReferenceMessenger.Default.UnregisterAll(this);
WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);
WeakReferenceMessenger.Default.Register<SessionTimeoutChangedMessage>(this);
```

Settings tab Session Timeout row (SettingsView.xaml line 138-156) -- copy this row's shape:
```xml
<!-- Row 3: Session Timeout -->
<Grid Height="40" Padding="12,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock l:Uids.Uid="SettingsSessionTimeout" FontSize="13"
               Foreground="{ThemeResource PrimaryTextBrush}" VerticalAlignment="Center" />
    <ComboBox Grid.Column="1"
              l:Uids.Uid="SessionTimeoutComboBox"
              SelectedIndex="{x:Bind ViewModel.SelectedThresholdIndex, Mode=TwoWay}"
              VerticalAlignment="Center">
        <ComboBoxItem Content="15min" />
        <!-- ... -->
    </ComboBox>
</Grid>
<Border Height="1" Background="{ThemeResource DividerBrush}" Margin="12,4" />
```

</interfaces>
</context>

<tasks>

<task type="auto" tdd="false">
  <name>Task 1: Add AppSettings properties + SessionVisibilityChangedMessage + 5 resw key pairs</name>
  <files>
    CCInfoWindows/CCInfoWindows/Models/AppSettings.cs,
    CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs,
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Models/AppSettings.cs (existing JsonPropertyName style, default values)
    - CCInfoWindows/CCInfoWindows/Messages/SessionTimeoutChangedMessage.cs (file structure to mirror)
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw (resw shape: `<data name="X.Header"><value>Y</value></data>`)
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw (matching English keys)
  </read_first>
  <action>
    **1. Extend `CCInfoWindows/CCInfoWindows/Models/AppSettings.cs`:**

    Add after the existing `SonnetContextSize` property (currently the last property in the class):

    ```csharp
    [JsonPropertyName("sessionVisibilityWindowDays")]
    public int SessionVisibilityWindowDays { get; set; } = 30;

    [JsonPropertyName("sessionVisibilityMigrationShown")]
    public bool SessionVisibilityMigrationShown { get; set; }
    ```

    Default 30 per D-03 / research SUMMARY Decision 4. `SessionVisibilityMigrationShown` defaults to `false` so existing installs trigger the migration toast on first launch (D-04).

    **2. Create `CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs`:**

    File contents (mirror SessionTimeoutChangedMessage exactly):

    ```csharp
    using CommunityToolkit.Mvvm.Messaging.Messages;

    namespace CCInfoWindows.Messages;

    /// <summary>
    /// Sent when SessionVisibilityWindowDays changes in Settings (DROPDOWN-04 / D-03).
    /// MainViewModel receives this to re-apply the display-layer cutoff in RefreshSessionList.
    /// G-1 compliant: receiver wraps body in _dispatcherQueue.TryEnqueue.
    /// </summary>
    public class SessionVisibilityChangedMessage : ValueChangedMessage<int>
    {
        public SessionVisibilityChangedMessage(int newWindowDays) : base(newWindowDays) { }
    }
    ```

    **3. Add 5 resw keys to BOTH `de-DE/Resources.resw` AND `en-US/Resources.resw`:**

    Insert these `<data>` entries (any position inside the root `<root>` element is fine; conventionally near other `Settings.*` keys). Both files MUST get all 5 keys -- L10N-02 invariant validated by `ResourceCoverageTests`.

    de-DE values:
    | name | value |
    |------|-------|
    | `SessionVisibilityWindow.Header` | `Sichtbarkeitsfenster` |
    | `SessionVisibilityWindow.7d` | `7 Tage` |
    | `SessionVisibilityWindow.30d` | `30 Tage` |
    | `SessionVisibilityWindow.90d` | `90 Tage` |
    | `SessionVisibilityWindow.Unlimited` | `Unbegrenzt` |

    en-US values:
    | name | value |
    |------|-------|
    | `SessionVisibilityWindow.Header` | `Visibility window` |
    | `SessionVisibilityWindow.7d` | `7 days` |
    | `SessionVisibilityWindow.30d` | `30 days` |
    | `SessionVisibilityWindow.90d` | `90 days` |
    | `SessionVisibilityWindow.Unlimited` | `Unlimited` |

    Each entry uses the existing resw shape (look at `SettingsSessionTimeout.Text` for the exact XML layout):
    ```xml
    <data name="SessionVisibilityWindow.Header" xml:space="preserve">
      <value>Sichtbarkeitsfenster</value>
    </data>
    ```

    NOTE: The `.Header` key targets the row label `TextBlock` (l:Uids.Uid pattern, so the actual lookup key on the TextBlock will be `l:Uids.Uid="SessionVisibilityWindow"` resolving `.Header` via the existing Localizer. Match the resolution pattern used by `SettingsSessionTimeout.Text` in the existing files; if the project convention uses `.Text` instead of `.Header`, use `.Text` for consistency with adjacent rows.) Verify by grepping the existing resw for `SettingsSessionTimeout` shape and mirror it.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests"</automated>
  </verify>
  <acceptance_criteria>
    - `grep -c "SessionVisibilityWindowDays" CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` returns >= 1.
    - `grep -c "SessionVisibilityMigrationShown" CCInfoWindows/CCInfoWindows/Models/AppSettings.cs` returns >= 1.
    - File `CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs` exists.
    - `grep -c "SessionVisibilityChangedMessage : ValueChangedMessage<int>" CCInfoWindows/CCInfoWindows/Messages/SessionVisibilityChangedMessage.cs` returns 1.
    - Each of the 5 keys (`SessionVisibilityWindow.Header`, `.7d`, `.30d`, `.90d`, `.Unlimited`) appears EXACTLY ONCE in `de-DE/Resources.resw` AND EXACTLY ONCE in `en-US/Resources.resw`. Verify via 10 separate grep counts.
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - `ResourceCoverageTests` passes (no DE/EN key drift introduced).
  </acceptance_criteria>
  <done>AppSettings has both new properties; message class compiles; 5 resw keys exist in both locales; build is green.</done>
</task>

<task type="auto" tdd="false">
  <name>Task 2: Wire SettingsViewModel ComboBox + SettingsView.xaml row</name>
  <files>
    CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs,
    CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs (precedent: SelectedThresholdIndex + OnSelectedThresholdIndexChanged + MapThresholdIndexToMinutes)
    - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml lines 138-158 (Session Timeout row to mirror)
  </read_first>
  <action>
    **1. Extend `SettingsViewModel.cs`:**

    Add a private static array near the other option arrays (after `SonnetContextSizes` line ~92):

    ```csharp
    // DROPDOWN-04 / D-03: visibility window options. 0 == unlimited.
    private static readonly int[] VisibilityWindowDayOptions = [7, 30, 90, 0];
    private const int DefaultVisibilityWindowIndex = 1; // 30 days
    ```

    Add an `[ObservableProperty]` near the existing settings-bound observables (after `_selectedSonnetContextIndex`):

    ```csharp
    [ObservableProperty]
    private int _selectedVisibilityWindowIndex;
    ```

    Extend `Initialize()` to load the persisted value. After the existing `_selectedSonnetContextIndex = ...` line and BEFORE the `OnPropertyChanged(...)` block:

    ```csharp
    _selectedVisibilityWindowIndex = MapVisibilityDaysToIndex(settings.SessionVisibilityWindowDays);
    ```

    Append a corresponding `OnPropertyChanged(nameof(SelectedVisibilityWindowIndex));` line to the `OnPropertyChanged` cascade in `Initialize()`.

    Add the partial change handler near the other `On*Changed` methods (mirror `OnSelectedThresholdIndexChanged`):

    ```csharp
    partial void OnSelectedVisibilityWindowIndexChanged(int value)
    {
        var settings = _settingsService.LoadSettings();
        settings.SessionVisibilityWindowDays = MapIndexToVisibilityDays(value);
        _settingsService.SaveSettings(settings);

        // DROPDOWN-04 / D-03: notify MainViewModel so SortedSessions filter re-applies immediately.
        WeakReferenceMessenger.Default.Send(
            new SessionVisibilityChangedMessage(settings.SessionVisibilityWindowDays));
    }

    private static int MapIndexToVisibilityDays(int index) =>
        (index >= 0 && index < VisibilityWindowDayOptions.Length)
            ? VisibilityWindowDayOptions[index]
            : VisibilityWindowDayOptions[DefaultVisibilityWindowIndex];

    private static int MapVisibilityDaysToIndex(int days)
    {
        var index = Array.IndexOf(VisibilityWindowDayOptions, days);
        return index >= 0 ? index : DefaultVisibilityWindowIndex;
    }
    ```

    **2. Add a ComboBox row to `SettingsView.xaml`:**

    Locate the "Row 3: Session Timeout" Grid (currently line 138-156) and the divider Border immediately after it (line 158).

    INSERT a new Grid + divider AFTER the Session Timeout divider (so between Session Timeout and Dark Mode), preserving alignment with the other 40 px rows:

    ```xml
    <!-- Row 3.5: Session Visibility Window (DROPDOWN-04 / D-03) -->
    <Grid Height="40" Padding="12,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock l:Uids.Uid="SessionVisibilityWindow"
                   FontSize="13" Foreground="{ThemeResource PrimaryTextBrush}"
                   VerticalAlignment="Center" />
        <ComboBox Grid.Column="1"
                  x:Name="VisibilityWindowComboBox"
                  SelectedIndex="{x:Bind ViewModel.SelectedVisibilityWindowIndex, Mode=TwoWay}"
                  VerticalAlignment="Center"
                  MinWidth="120">
            <ComboBoxItem l:Uids.Uid="SessionVisibilityWindow.7d" />
            <ComboBoxItem l:Uids.Uid="SessionVisibilityWindow.30d" />
            <ComboBoxItem l:Uids.Uid="SessionVisibilityWindow.90d" />
            <ComboBoxItem l:Uids.Uid="SessionVisibilityWindow.Unlimited" />
        </ComboBox>
    </Grid>

    <Border Height="1" Background="{ThemeResource DividerBrush}" Margin="12,4" />
    ```

    The `l:Uids.Uid="SessionVisibilityWindow"` on the row label resolves the `.Header` (or `.Text` -- match the project's existing convention as discovered in Task 1) value at runtime.

    Each `ComboBoxItem` uses `l:Uids.Uid` to resolve its localized content from the resw keys added in Task 1. If the WinUI3Localizer pattern requires a `Content` attachment property instead of inline `l:Uids.Uid`, adjust to match the precedent at line 151-154 (`<ComboBoxItem Content="15min" />` is hardcoded EN-only there -- the new ComboBox MUST use localized Uids since the values differ between DE / EN).

    Verify by visual smoke at task end: switch language EN -> DE and confirm the items read "7 days / 30 days / 90 days / Unlimited" then "7 Tage / 30 Tage / 90 Tage / Unbegrenzt".
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-restore</automated>
  </verify>
  <acceptance_criteria>
    - `grep -c "SelectedVisibilityWindowIndex" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` returns >= 3 (declaration + Initialize + partial handler).
    - `grep -c "VisibilityWindowDayOptions = \[7, 30, 90, 0\]" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` returns 1.
    - `grep -c "Send(\\s*new SessionVisibilityChangedMessage" CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs` returns 1.
    - `grep -c "VisibilityWindowComboBox" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` returns 1.
    - `grep -c "SessionVisibilityWindow.7d" CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` returns 1 (and similar single hits for `.30d`, `.90d`, `.Unlimited`).
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - Full `dotnet test` passes (no new failures).
  </acceptance_criteria>
  <done>SettingsViewModel exposes the new index property + handler; SettingsView.xaml has the new row; build green; tests pass.</done>
</task>

<task type="auto" tdd="false">
  <name>Task 3: MainViewModel IRecipient registration + Receive + RefreshSessionList cutoff filter</name>
  <files>CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs</files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs lines 48-51 (class header / IRecipient list)
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs lines 309-340 (InitializeAsync register block)
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs lines 660-740 (RefreshSessionList full body)
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs lines 1043-1049 (existing G-1 Receive precedent)
  </read_first>
  <action>
    **1. Extend the class declaration (line 48-50):**

    Change:
    ```csharp
    public partial class MainViewModel : ObservableObject,
        IRecipient<AuthStateChangedMessage>,
        IRecipient<SessionTimeoutChangedMessage>   // D-08
    ```
    to:
    ```csharp
    public partial class MainViewModel : ObservableObject,
        IRecipient<AuthStateChangedMessage>,
        IRecipient<SessionTimeoutChangedMessage>,   // D-08
        IRecipient<SessionVisibilityChangedMessage>   // DROPDOWN-04 / D-03
    ```

    **2. Register the new message in `InitializeAsync` (after line 316):**

    Insert immediately AFTER:
    ```csharp
    WeakReferenceMessenger.Default.Register<SessionTimeoutChangedMessage>(this);   // D-08
    ```

    Add:
    ```csharp
    WeakReferenceMessenger.Default.Register<SessionVisibilityChangedMessage>(this);   // DROPDOWN-04 / D-03
    ```

    **3. Add the G-1 compliant `Receive` method:**

    Place AFTER the existing `Receive(SessionTimeoutChangedMessage message)` method (currently at line 1043-1049) -- mirror its structure exactly:

    ```csharp
    public void Receive(SessionVisibilityChangedMessage message)
    {
        // DROPDOWN-04 / D-03: re-apply visibility cutoff filter on SortedSessions.
        // Dispatched to UI thread -- RefreshSessionList requires it.
        // G-1 compliant: constructor-injected _dispatcherQueue is non-null. L-02 honored.
        _dispatcherQueue.TryEnqueue(RefreshSessionList);
    }
    ```

    The body MUST be a single `_dispatcherQueue.TryEnqueue(RefreshSessionList);` line per L-02 / Phase 24 G-1. `MessengerThreadingConventionTests` reflects on every `IRecipient<>` and will fail the build if the wrap is missing.

    **4. Apply the visibility cutoff filter in `RefreshSessionList` (currently line 664-740):**

    The current code at lines 690-704 builds `displayItems` from `latestSessions.OrderByDescending(...).Select(...)`.

    Modify the LINQ pipeline so the cutoff is applied BEFORE `OrderByDescending`. Replace the existing block:

    ```csharp
    var thresholdMinutes = settings.SessionActivityThresholdMinutes;
    var displayItems = latestSessions
        .OrderByDescending(s => s.LastActivity)
        .Select(s =>
        {
            var isActive = s.IsActive(threshold);
            return new SessionDisplayItem
            {
                Session = s,
                DisplayName = s.DisplayName,
                IsActive = isActive,
                TooltipText = ComputeTooltipText(s, isActive, thresholdMinutes)
            };
        })
        .ToList();
    ```

    with:

    ```csharp
    var thresholdMinutes = settings.SessionActivityThresholdMinutes;

    // DROPDOWN-01 / DROPDOWN-04 / D-03: display-layer visibility cutoff.
    // JsonlService keeps aggregating ALL sessions (cost / quota totals must NOT lose data) --
    // we only filter the user-visible ComboBox source here.
    var visibilityCutoff = settings.SessionVisibilityWindowDays > 0
        ? DateTimeOffset.UtcNow.AddDays(-settings.SessionVisibilityWindowDays)
        : DateTimeOffset.MinValue;

    var displayItems = latestSessions
        .Where(s => s.LastActivity >= visibilityCutoff)
        .OrderByDescending(s => s.LastActivity)
        .Select(s =>
        {
            var isActive = s.IsActive(threshold);
            return new SessionDisplayItem
            {
                Session = s,
                DisplayName = s.DisplayName,
                IsActive = isActive,
                TooltipText = ComputeTooltipText(s, isActive, thresholdMinutes)
            };
        })
        .ToList();
    ```

    `JsonlService.Sessions` (the source) is unchanged -- aggregation stats over ALL sessions remain intact. `Sessions.Add(session)` immediately above (lines 675-679) still adds every session to the internal collection used by stats; only `SortedSessions` (the ComboBox source) is cutoff-filtered.

    DO NOT also filter the loop at line 675-679 (`Sessions.Clear(); foreach (var session in latestSessions) Sessions.Add(session);`) -- that collection feeds the persisted-selection restoration logic at lines 681-735 and must continue to know about all sessions (so a previously-selected session that fell outside the visibility window is not silently lost from the ComboBox identity comparison; the visibility filter intentionally does NOT remove items the user had explicitly selected).

    EDGE CASE handling: if `previousSessionId != null` AND that session is in `latestSessions` but NOT in `displayItems` (because it fell outside the cutoff), the existing `SortedSessions.FirstOrDefault(d => d.Session.Id == previousSessionId)` at line 711 returns null and the selection is dropped naturally -- this matches expected behavior (the user explicitly narrowed the window).
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MessengerThreadingConventionTests" --no-restore</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-restore</automated>
    Expected: build green; MessengerThreadingConventionTests passes (G-1 enforcement validates the new Receive); full test suite shows no NEW failures vs pre-phase baseline.
  </verify>
  <acceptance_criteria>
    - `grep -c "IRecipient<SessionVisibilityChangedMessage>" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns 1 (the class declaration; the Receive method body itself does not contain that string).
    - `grep -c "Register<SessionVisibilityChangedMessage>(this)" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns 1.
    - `grep -c "public void Receive(SessionVisibilityChangedMessage message)" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns 1.
    - The Receive body is a single `_dispatcherQueue.TryEnqueue(RefreshSessionList);` statement (verified by reading lines around the new method, confirming no other state mutation outside the wrap).
    - `grep -c "SessionVisibilityWindowDays" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns >= 1 (the new cutoff filter).
    - `grep -c "visibilityCutoff" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns >= 1.
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - `dotnet test ... --filter "FullyQualifiedName~MessengerThreadingConventionTests"` reports `Passed`. (CRITICAL -- this is the L-02 G-1 enforcement gate.)
    - Full `dotnet test` shows no NEW failures vs pre-phase baseline.
  </acceptance_criteria>
  <done>MainViewModel implements IRecipient<SessionVisibilityChangedMessage>; cutoff filter live in RefreshSessionList; G-1 convention test still passes.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 4: Visual smoke -- Settings ComboBox + reactive MainView refresh</name>
  <files>CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml, CCInfoWindows/CCInfoWindows/Views/MainView.xaml</files>
  <action>Run dotnet run, exercise the ComboBox per <how-to-verify>, and confirm each step.</action>
  <verify>See <how-to-verify> below.</verify>
  <done>User confirms ComboBox renders, persists value to settings.json, and reactively filters the Active Session ComboBox in DE and EN.</done>
  <what-built>SessionVisibilityWindow ComboBox in Settings + reactive MainView ComboBox refresh.</what-built>
  <how-to-verify>
    1. `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
    2. Open Settings (gear icon). Confirm the General tab shows a new row labeled "Sichtbarkeitsfenster" (DE) or "Visibility window" (EN) between Session Timeout and Dark Mode, with a ComboBox showing "30 Tage" / "30 days" selected by default.
    3. Click the ComboBox -- confirm 4 options visible: 7 Tage / 30 Tage / 90 Tage / Unbegrenzt (or English equivalents).
    4. Open `%APPDATA%\..\Local\CCInfoWindows\settings.json` (use Explorer or `notepad %LOCALAPPDATA%\CCInfoWindows\settings.json`). Confirm `"sessionVisibilityWindowDays": 30` and `"sessionVisibilityMigrationShown": false` are present.
    5. Switch the ComboBox to "7 Tage" / "7 days". Re-read settings.json and confirm `"sessionVisibilityWindowDays": 7`.
    6. Return to MainView (back arrow). Confirm the Active Session ComboBox ONLY shows sessions whose last activity is within the last 7 days -- compare against the previous list.
    7. Switch the ComboBox back to "Unbegrenzt" / "Unlimited". Confirm settings.json shows `"sessionVisibilityWindowDays": 0` and the Active Session ComboBox shows ALL sessions (no cutoff applied).
    8. Toggle the language switch (DE <-> EN) and confirm the row label and 4 ComboBox option texts switch locales correctly.
  </how-to-verify>
  <resume-signal>Type "approved" or describe any visual / functional issues observed.</resume-signal>
</task>

</tasks>

<verification>
- `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` succeeds.
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MessengerThreadingConventionTests"` passes (G-1 honored on new Receive).
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests"` passes (DE / EN parity for 5 new keys).
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceColdStartTests"` still passes (Plan 25-01 regression check).
- Full `dotnet test` baseline failure count unchanged.
- Smoke verification confirms ComboBox renders + persists + reactively filters MainView ComboBox.
</verification>

<success_criteria>
- DROPDOWN-01: cold-start "Active Session" ComboBox lists all sessions whose JSONL files were modified within `SessionVisibilityWindowDays` (verified by smoke + Plan 25-01 cold-start tests).
- DROPDOWN-04: General tab ComboBox bound to `SessionVisibilityWindowDays` (default 30, options 7/30/90/0); change triggers `SessionVisibilityChangedMessage`; filter applies in `MainViewModel.RefreshSessionList`.
- L-02 / G-1 honored: `Receive(SessionVisibilityChangedMessage)` wraps in `_dispatcherQueue.TryEnqueue` and `MessengerThreadingConventionTests` passes.
- L10N parity: 5 new resw keys exist in both `de-DE` and `en-US` files; `ResourceCoverageTests` passes.
- No regression in stats / cost aggregations -- `JsonlService.Sessions` source feeds unchanged data; only `SortedSessions` is filtered.
</success_criteria>

<output>
After completion, create `.planning/phases/25-cold-start-session-hydration-visibility-window/25-02-SUMMARY.md` documenting:
- AppSettings additions + JSON property names.
- The new SessionVisibilityChangedMessage shape.
- SettingsViewModel mapping arrays + handler.
- MainViewModel Receive position (line number after edit) + filter location in RefreshSessionList.
- The 5 resw key pairs + final convention used (.Header vs .Text).
- Smoke verification outcome (DE / EN / persistence / reactive refresh).
</output>

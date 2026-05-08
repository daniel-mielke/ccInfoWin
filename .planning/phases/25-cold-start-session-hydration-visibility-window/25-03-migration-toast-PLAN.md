---
phase: 25-cold-start-session-hydration-visibility-window
plan: 03
type: execute
wave: 3
depends_on: ["25-02-visibility-window-settings"]
files_modified:
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
autonomous: false
requirements: [DROPDOWN-05]
must_haves:
  truths:
    - "On first launch after upgrade (SessionVisibilityMigrationShown == false), an Informational InfoBar appears in MainView with the migration explanation text"
    - "InfoBar text is localized in DE and EN"
    - "Dismissing the InfoBar (clicking the X) immediately persists SessionVisibilityMigrationShown = true to settings.json BEFORE app shutdown"
    - "On the next app launch, the InfoBar does NOT reappear"
    - "On a fresh install with no prior settings.json, the toast still appears once (initial default false)"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      provides: "[ObservableProperty] IsSessionVisibilityMigrationToastVisible + InitializeAsync migration check + DismissMigrationToastCommand"
      contains: "IsSessionVisibilityMigrationToastVisible"
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      provides: "InfoBar bound to IsSessionVisibilityMigrationToastVisible (Mode=TwoWay) + Closed event handler"
      contains: "MigrationToastInfoBar"
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs"
      provides: "OnMigrationToastClosed code-behind that calls VM dismiss"
      contains: "OnMigrationToastClosed"
    - path: "CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw"
      provides: "Toast.SessionVisibilityMigration.Title + .Message keys (German)"
      contains: "Toast.SessionVisibilityMigration"
    - path: "CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw"
      provides: "Toast.SessionVisibilityMigration.Title + .Message keys (English)"
      contains: "Toast.SessionVisibilityMigration"
  key_links:
    - from: "MainViewModel.InitializeAsync"
      to: "IsSessionVisibilityMigrationToastVisible = !settings.SessionVisibilityMigrationShown"
      via: "first-launch migration check (CD-05 site)"
      pattern: "SessionVisibilityMigrationShown"
    - from: "MainView.xaml InfoBar Closed event"
      to: "MainViewModel.DismissMigrationToast (immediate SaveSettings per CD-02)"
      via: "OnMigrationToastClosed code-behind handler"
      pattern: "OnMigrationToastClosed"
---

<objective>
Ship a one-time migration toast for existing installs (`DROPDOWN-05`). When `SessionVisibilityMigrationShown` is `false` at app start, an Informational `InfoBar` appears in MainView explaining the new visibility window default. Dismiss writes the flag to disk synchronously (CD-02) so a crash between dismiss and shutdown does NOT re-show the toast.

Implementation notes:
- InfoBar (NOT Windows Toast Notification) per D-04 -- mirrors `IsSessionExpired` InfoBar in MainView.
- Migration check fires in `MainViewModel.InitializeAsync` per CD-05 (settings already loaded; UI thread guaranteed).
- 2 resw key pairs (Title + Message) in DE + EN.

Plan is `autonomous: false` -- the InfoBar needs visual smoke to confirm rendering, dismissal flow, and persistence.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/phases/25-cold-start-session-hydration-visibility-window/25-CONTEXT.md
@.planning/phases/25-cold-start-session-hydration-visibility-window/25-02-visibility-window-settings-PLAN.md
@CLAUDE.md

@CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
@CCInfoWindows/CCInfoWindows/Views/MainView.xaml

<interfaces>
<!-- Existing InfoBar precedent (Phase 22 pricing-error / Phase 20 session-expired) shape -->

From CCInfoWindows/CCInfoWindows/Views/MainView.xaml lines 56-72 (precedent InfoBar shape):
```xml
<InfoBar
    l:Uids.Uid="SessionExpiredInfoBar"
    Severity="Warning"
    IsOpen="{x:Bind ViewModel.IsSessionExpired, Mode=OneWay}"
    Visibility="{x:Bind ViewModel.IsSessionExpired, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
    IsClosable="False"
    Margin="0,0,0,12">
    <InfoBar.ActionButton>
        <Button Command="{x:Bind ViewModel.ReLoginCommand}">
            ...
        </Button>
    </InfoBar.ActionButton>
</InfoBar>
```

The migration toast deviates: `IsClosable="True"`, `Mode=TwoWay` on `IsOpen`, no ActionButton.

From CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs (precedent for [ObservableProperty] + [RelayCommand]):
```csharp
[ObservableProperty]
private bool _isSessionExpired;

[RelayCommand]
private void ReLogin() { ... }
```

`InitializeAsync` shape -- the migration check site (CD-05):
```csharp
public async Task InitializeAsync()
{
    WeakReferenceMessenger.Default.UnregisterAll(this);
    WeakReferenceMessenger.Default.Register<AuthStateChangedMessage>(this);
    WeakReferenceMessenger.Default.Register<SessionTimeoutChangedMessage>(this);
    WeakReferenceMessenger.Default.Register<SessionVisibilityChangedMessage>(this);   // <-- added by Plan 25-02

    var settings = _settingsService.LoadSettings();
    _refreshIntervalSeconds = settings.RefreshIntervalSeconds;
    // <-- migration check site goes immediately after settings load
    // ... existing code ...
}
```

`ISettingsService` surface (mirror existing usage):
```csharp
// From SettingsViewModel precedent at line 167-168:
var settings = _settingsService.LoadSettings();
settings.SessionVisibilityMigrationShown = true;
_settingsService.SaveSettings(settings);   // synchronous; persists to %LOCALAPPDATA%\CCInfoWindows\settings.json
```

</interfaces>
</context>

<tasks>

<task type="auto" tdd="false">
  <name>Task 1: Add 2 resw key pairs + MainViewModel toast state + dismiss handler</name>
  <files>
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw,
    CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw (existing InfoBar resw shape -- look for `SessionExpiredInfoBar`)
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw (matching English entries)
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs lines 78-90 ([ObservableProperty] precedent: _isUpdateAvailable, _isSessionExpired)
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs lines 309-360 (InitializeAsync: settings load + register block)
  </read_first>
  <action>
    **1. Add 2 resw key pairs to BOTH `de-DE/Resources.resw` AND `en-US/Resources.resw`:**

    de-DE:
    | name | value |
    |------|-------|
    | `Toast.SessionVisibilityMigration.Title` | `Sichtbarkeitsfenster aktiviert` |
    | `Toast.SessionVisibilityMigration.Message` | `Sitzungen aelter als 30 Tage werden jetzt ausgeblendet — anpassbar in Einstellungen.` |

    en-US:
    | name | value |
    |------|-------|
    | `Toast.SessionVisibilityMigration.Title` | `Visibility window enabled` |
    | `Toast.SessionVisibilityMigration.Message` | `Sessions older than 30 days are now hidden — adjustable in Settings.` |

    NOTE on the German text: `aelter` keeps the file ASCII-safe inside this plan; the actual resw value should be the proper UTF-8 `älter`. Encode the resw file in UTF-8 (the existing files already use UTF-8 -- preserve that).

    Use the existing `<data name="..." xml:space="preserve"><value>...</value></data>` shape.

    **2. Extend `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs`:**

    Add a new `[ObservableProperty]` near the auth-state block (insert immediately AFTER `_isSessionExpired` at line 87):

    ```csharp
    // DROPDOWN-05 / D-04: one-time migration toast for existing installs.
    // True only on first launch after upgrade -- persisted via SaveSettings on dismiss (CD-02).
    [ObservableProperty]
    private bool _isSessionVisibilityMigrationToastVisible;
    ```

    Add the migration check inside `InitializeAsync` per CD-05. Locate the line:
    ```csharp
    var settings = _settingsService.LoadSettings();
    _refreshIntervalSeconds = settings.RefreshIntervalSeconds;
    ```
    Insert IMMEDIATELY AFTER that block:
    ```csharp
    // DROPDOWN-05 / D-04 / CD-05: first-launch migration toast.
    // Shown when the persisted flag is false (existing install upgrading to v1.5).
    // Fresh installs also see the toast once -- AppSettings default is false.
    if (!settings.SessionVisibilityMigrationShown)
    {
        IsSessionVisibilityMigrationToastVisible = true;
    }
    ```

    Add a `[RelayCommand]` method that the View invokes when the user dismisses the InfoBar. Place near `ReLoginCommand` or other [RelayCommand]s:

    ```csharp
    /// <summary>
    /// DROPDOWN-05 / D-04 / CD-02: dismiss the migration toast and persist immediately.
    /// CD-02 rule: SaveSettings is synchronous (no app-shutdown dependency) so a crash
    /// between dismiss and shutdown does not re-show the toast on next launch.
    /// </summary>
    [RelayCommand]
    private void DismissMigrationToast()
    {
        IsSessionVisibilityMigrationToastVisible = false;

        var settings = _settingsService.LoadSettings();
        settings.SessionVisibilityMigrationShown = true;
        _settingsService.SaveSettings(settings);
    }
    ```

    The generated command property is `DismissMigrationToastCommand` (CommunityToolkit.Mvvm source generator).
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests"</automated>
  </verify>
  <acceptance_criteria>
    - `Toast.SessionVisibilityMigration.Title` appears EXACTLY ONCE in de-DE/Resources.resw AND EXACTLY ONCE in en-US/Resources.resw.
    - `Toast.SessionVisibilityMigration.Message` appears EXACTLY ONCE in de-DE/Resources.resw AND EXACTLY ONCE in en-US/Resources.resw.
    - `grep -c "_isSessionVisibilityMigrationToastVisible" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns 1.
    - `grep -c "if (!settings.SessionVisibilityMigrationShown)" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns 1.
    - `grep -c "DismissMigrationToast" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns >= 1 (the [RelayCommand] method).
    - `grep -c "SessionVisibilityMigrationShown = true" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` returns 1 (inside DismissMigrationToast).
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - `ResourceCoverageTests` passes.
  </acceptance_criteria>
  <done>2 resw key pairs added; MainViewModel exposes the new ObservableProperty + RelayCommand + InitializeAsync trigger; build green.</done>
</task>

<task type="auto" tdd="false">
  <name>Task 2: Add migration toast InfoBar to MainView.xaml + Closed handler in code-behind</name>
  <files>
    CCInfoWindows/CCInfoWindows/Views/MainView.xaml,
    CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
  </files>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml lines 38-83 (existing InfoBar StackPanel + UpdateInfoBar precedent for Closing="OnUpdateInfoBarClosing")
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs (existing OnUpdateInfoBarClosing handler shape -- mirror)
  </read_first>
  <action>
    **1. Add the InfoBar to `CCInfoWindows/CCInfoWindows/Views/MainView.xaml`:**

    Locate the StackPanel at line 39 (`<StackPanel Grid.Row="0">`). Append a new InfoBar AFTER the existing API error InfoBar at lines 75-82, BEFORE the closing `</StackPanel>` at line 83:

    ```xml
    <!-- DROPDOWN-05 / D-04: Session visibility migration toast (one-time, dismissable) -->
    <InfoBar
        x:Name="MigrationToastInfoBar"
        l:Uids.Uid="SessionVisibilityMigrationInfoBar"
        Severity="Informational"
        IsOpen="{x:Bind ViewModel.IsSessionVisibilityMigrationToastVisible, Mode=TwoWay}"
        Visibility="{x:Bind ViewModel.IsSessionVisibilityMigrationToastVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
        Title="{x:Bind l:Localizer.Get('Toast.SessionVisibilityMigration.Title'), Mode=OneTime}"
        Message="{x:Bind l:Localizer.Get('Toast.SessionVisibilityMigration.Message'), Mode=OneTime}"
        IsClosable="True"
        Closed="OnMigrationToastClosed"
        Margin="0,0,0,12" />
    ```

    NOTE on the `l:Localizer.Get(...)` syntax: the project uses `l:Uids.Uid="..."` for most localized text (auto-resolves `.Title` / `.Message` from resw at runtime). If `Localizer.Get` is not exposed as a static x:Bind helper in this codebase, prefer the existing `l:Uids.Uid` pattern instead -- set:
    ```xml
    l:Uids.Uid="Toast.SessionVisibilityMigration"
    ```
    on the InfoBar element. The Localizer will then automatically resolve `Toast.SessionVisibilityMigration.Title` for `Title` and `Toast.SessionVisibilityMigration.Message` for `Message` (the convention used by the existing `SessionExpiredInfoBar` -- the framework looks up `<Uid>.Title`, `<Uid>.Message` by reflection).

    Use whichever approach matches the existing precedent at lines 56-72 (most likely the `l:Uids.Uid` pattern). The resw keys created in Task 1 already follow `Toast.SessionVisibilityMigration.Title` / `.Message` exactly so the dotted-suffix lookup will work.

    Final InfoBar (using `l:Uids.Uid`):
    ```xml
    <!-- DROPDOWN-05 / D-04: Session visibility migration toast (one-time, dismissable) -->
    <InfoBar
        x:Name="MigrationToastInfoBar"
        l:Uids.Uid="Toast.SessionVisibilityMigration"
        Severity="Informational"
        IsOpen="{x:Bind ViewModel.IsSessionVisibilityMigrationToastVisible, Mode=TwoWay}"
        Visibility="{x:Bind ViewModel.IsSessionVisibilityMigrationToastVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
        IsClosable="True"
        Closed="OnMigrationToastClosed"
        Margin="0,0,0,12" />
    ```

    **2. Add the `OnMigrationToastClosed` handler to `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs`:**

    Mirror the existing `OnUpdateInfoBarClosing` handler shape. Add a new method:

    ```csharp
    /// <summary>
    /// DROPDOWN-05 / D-04 / CD-02: when the user dismisses the migration toast,
    /// invoke the VM command which persists SessionVisibilityMigrationShown = true synchronously.
    /// `Closed` (not `Closing`) fires AFTER the InfoBar collapses; using TwoWay binding on IsOpen
    /// already keeps the VM in sync, but we also need persistence -- the command handles both.
    /// </summary>
    private void OnMigrationToastClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (DataContext is MainViewModel vm && vm.DismissMigrationToastCommand.CanExecute(null))
        {
            vm.DismissMigrationToastCommand.Execute(null);
        }
    }
    ```

    Add the necessary `using` directives if not already present:
    ```csharp
    using Microsoft.UI.Xaml.Controls;   // InfoBar / InfoBarClosedEventArgs
    using CCInfoWindows.ViewModels;
    ```

    Edge case: `DismissMigrationToastCommand.Execute` calls `IsSessionVisibilityMigrationToastVisible = false`, which the TwoWay-bound `IsOpen` would normally set in response to the close action -- this creates a transient set-flag-twice but no infinite loop because the second set is a no-op (already false). The `SaveSettings` call inside the command is the actual side-effect that matters.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --no-restore</automated>
  </verify>
  <acceptance_criteria>
    - `grep -c "MigrationToastInfoBar" CCInfoWindows/CCInfoWindows/Views/MainView.xaml` returns 1.
    - `grep -c "Toast.SessionVisibilityMigration" CCInfoWindows/CCInfoWindows/Views/MainView.xaml` returns 1 (the l:Uids.Uid).
    - `grep -c "IsSessionVisibilityMigrationToastVisible" CCInfoWindows/CCInfoWindows/Views/MainView.xaml` returns 2 (IsOpen + Visibility bindings).
    - `grep -c "Closed=\"OnMigrationToastClosed\"" CCInfoWindows/CCInfoWindows/Views/MainView.xaml` returns 1.
    - `grep -c "OnMigrationToastClosed" CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` returns 1.
    - `grep -c "DismissMigrationToastCommand" CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` returns >= 1.
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` exits 0.
    - Full `dotnet test` shows no NEW failures vs baseline.
  </acceptance_criteria>
  <done>InfoBar renders in MainView when IsSessionVisibilityMigrationToastVisible = true; Closed handler invokes the VM command which persists the flag synchronously.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 3: Visual smoke -- Migration InfoBar appears once + dismiss persists synchronously</name>
  <files>CCInfoWindows/CCInfoWindows/Views/MainView.xaml, %LOCALAPPDATA%/CCInfoWindows/settings.json</files>
  <action>Reset flag, run dotnet run, dismiss the InfoBar, kill via Task Manager, restart, confirm no reappear.</action>
  <verify>See <how-to-verify> below.</verify>
  <done>User confirms InfoBar appears on flag=false launch, dismiss writes flag=true synchronously (crash-resilient), and the toast does not reappear on subsequent launches.</done>
  <what-built>One-time migration toast InfoBar in MainView with synchronous persistence on dismiss.</what-built>
  <how-to-verify>
    Pre-step (ensure clean migration test): close the app fully, then manually edit `%LOCALAPPDATA%\CCInfoWindows\settings.json` and set `"sessionVisibilityMigrationShown": false` (or delete the file entirely to simulate fresh install). Save.

    1. `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
    2. Wait for MainView to load. Confirm an Informational (blue) InfoBar appears at the top of the window with:
       - DE: Title "Sichtbarkeitsfenster aktiviert", Message "Sitzungen älter als 30 Tage werden jetzt ausgeblendet — anpassbar in Einstellungen."
       - EN: Title "Visibility window enabled", Message "Sessions older than 30 days are now hidden — adjustable in Settings."
       (Confirm the locale matches the current language switch state.)
    3. Confirm the InfoBar has a visible close (X) button on the right.
    4. Click the X. Confirm the InfoBar disappears.
    5. WITHOUT closing the app, open `%LOCALAPPDATA%\CCInfoWindows\settings.json` in Notepad. Confirm `"sessionVisibilityMigrationShown": true` is now persisted.
    6. Close the app via the X button (graceful shutdown).
    7. Restart the app. Confirm the InfoBar does NOT reappear on this second launch.
    8. Toggle language (DE <-> EN) and re-do steps 1-7 once with the opposite locale to verify both translations render.
    9. Crash-recovery sanity check: set the flag back to false, run the app, click the X to dismiss, then immediately kill the app via Task Manager (do NOT use the X). Restart -- confirm the toast does NOT reappear (CD-02 synchronous persistence works under crash).
  </how-to-verify>
  <resume-signal>Type "approved" or describe issues. Specifically confirm: (a) toast appears on flag=false, (b) toast text is correctly localized, (c) dismiss persists synchronously even under hard kill, (d) toast does NOT reappear on subsequent launches.</resume-signal>
</task>

</tasks>

<verification>
- `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` succeeds.
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests"` passes (DE / EN parity for 2 new keys).
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MessengerThreadingConventionTests"` still passes (no new IRecipient<> -- sanity check).
- `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~JsonlServiceColdStartTests"` still passes.
- Full `dotnet test` baseline failure count unchanged.
- Smoke verification confirms first-launch trigger, locale correctness, synchronous persistence, no-reappear on relaunch, crash-resilient dismiss.
</verification>

<success_criteria>
- DROPDOWN-05: existing installs see a one-time InfoBar on first v1.5 launch; flag persists immediately on dismiss; tracked by `SessionVisibilityMigrationShown` in `AppSettings`.
- D-04 honored: WinUI InfoBar (NOT Windows Toast Notification); Severity=Informational; IsClosable=true.
- CD-02 honored: `SaveSettings` runs synchronously inside `DismissMigrationToast` -- no shutdown dependency, crash-safe.
- CD-05 honored: migration check fires in `MainViewModel.InitializeAsync` after settings load.
- L10N parity: 2 resw key pairs in DE + EN, validated by `ResourceCoverageTests`.
</success_criteria>

<output>
After completion, create `.planning/phases/25-cold-start-session-hydration-visibility-window/25-03-SUMMARY.md` documenting:
- Final InfoBar XAML location (line range in MainView.xaml).
- The localization Uid pattern chosen (l:Uids.Uid="Toast.SessionVisibilityMigration" with auto-resolved `.Title` / `.Message`).
- Migration check site in InitializeAsync (line number after edit).
- DismissMigrationToast command shape + the synchronous SaveSettings call site.
- Smoke verification outcome (trigger, locale, persistence, crash-resilience, no-reappear).
- Phase 25 milestone completion: all 6 DROPDOWN requirements (01-06) closed -- ready for phase verification.
</output>

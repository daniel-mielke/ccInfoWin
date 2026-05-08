---
phase: 26-persistent-session-renaming
plan: 02
type: execute
wave: 2
depends_on: [26-01]
files_modified:
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
  - CCInfoWindows/CCInfoWindows/App.xaml.cs
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  - CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
  - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
autonomous: false
requirements: [RENAME-01, RENAME-04, RENAME-05, RENAME-08]
user_setup: []
must_haves:
  truths:
    - "MainView shows a pencil button immediately right of the session ComboBox; the ComboBox shrinks by ~32px to fit (CD-02)"
    - "Clicking the pencil opens a ContentDialog with TextBox pre-filled with the current display name, Save and Cancel buttons, and a Reset button visible only if a custom name currently exists"
    - "Save persists the new name via _sessionNameStore.SetCustomName(s.Id, sanitized) + SaveAsync(); the ComboBox updates without app restart (RENAME-04)"
    - "MainViewModel constructor accepts ISessionNameStore as 12th parameter; App.xaml.cs factory passes it; FakeDispatcherQueue + new ISessionNameStore stub flow through both direct-constructor test files"
    - "MainViewModel.RefreshSessionList resolves the displayName via _sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName (RENAME-08)"
    - "MainViewModel subscribes to ISessionNameStore.NameChanged in InitializeAsync; the handler wraps RefreshSessionList in _dispatcherQueue.TryEnqueue per G-1"
    - "MainViewModel.StopTimers (or new IDisposable cleanup) unsubscribes via -= (CD-05) — no zombie subscriptions"
    - "5 new resw key pairs exist in BOTH de-DE and en-US: Dialog.RenameSession.Title, Dialog.RenameSession.SaveButton, Dialog.RenameSession.CancelButton, Dialog.RenameSession.ResetButton, MainView.RenameButton.ToolTip"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      provides: "12th-arg ISessionNameStore field + OpenRenameDialogCommand + NameChanged subscription + display-layer resolution"
      contains: "_sessionNameStore"
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      provides: "Pencil button + ContentDialog x:Name=RenameSessionDialog"
      contains: "RenameSessionDialog"
    - path: "CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw"
      provides: "5 new DE rename UI strings"
    - path: "CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw"
      provides: "5 new EN rename UI strings"
  key_links:
    - from: "MainView.xaml pencil Button"
      to: "MainViewModel.OpenRenameDialogCommand"
      via: "Command={x:Bind ViewModel.OpenRenameDialogCommand}"
      pattern: "OpenRenameDialogCommand"
    - from: "MainViewModel.RefreshSessionList"
      to: "ISessionNameStore.GetCustomName"
      via: "display-layer overlay (RENAME-08)"
      pattern: "_sessionNameStore\\.GetCustomName"
    - from: "ISessionNameStore.NameChanged"
      to: "MainViewModel handler"
      via: "+= in InitializeAsync, -= in StopTimers"
      pattern: "NameChanged \\+="
    - from: "App.xaml.cs MainViewModel factory"
      to: "ISessionNameStore"
      via: "12th GetRequiredService argument"
      pattern: "GetRequiredService<ISessionNameStore>"
---

<objective>
Wire `ISessionNameStore` (from Wave 1) into `MainViewModel` and add the pencil-button + ContentDialog rename UX in MainView. This plan delivers RENAME-01 (pencil + dialog), RENAME-04 (cross-VM propagation via .NET event marshaled through IDispatcherQueue), RENAME-05 (sanitize on save — UI layer belt + store layer suspenders from Plan 01), and RENAME-08 (display-layer resolution in RefreshSessionList).

Purpose: this is the user's primary rename surface. The Sessions Settings tab in Plan 03 is the secondary "manage all" surface; the pencil is the inline single-session affordance.

Output: extended MainViewModel constructor (12 args), new `[RelayCommand] OpenRenameDialog`, NameChanged subscription/disposal, display-layer overlay in RefreshSessionList, pencil button + ContentDialog in MainView XAML, code-behind dialog launcher, 5 resw key pairs (DE+EN), updated DI factory in App.xaml.cs, updated direct-constructor tests.
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

@CCInfoWindows/CCInfoWindows/Services/Interfaces/ISessionNameStore.cs
@CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs
@CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
@CCInfoWindows/CCInfoWindows/Views/MainView.xaml
@CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
@CCInfoWindows/CCInfoWindows/App.xaml.cs
@CCInfoWindows/CCInfoWindows/Helpers/SessionNameSanitizer.cs

<interfaces>
ISessionNameStore (Plan 01 deliverable):
```csharp
public interface ISessionNameStore
{
    string? GetCustomName(string sessionId);
    void SetCustomName(string sessionId, string customName);
    void ClearCustomName(string sessionId);
    bool Save();
    Task<bool> SaveAsync(CancellationToken ct = default);
    event EventHandler<SessionNameChangedEventArgs>? NameChanged;
}
```

MainViewModel current constructor (line 283-294 — 11 args after Phase 24):
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
    IBurnRateNotificationService burnRateNotificationService,
    IDispatcherQueue dispatcherQueue)
```

App.xaml.cs current factory (line 165-176):
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

Existing pencil-relevant XAML (MainView.xaml lines 107-121) — ComboBox currently HorizontalAlignment="Stretch":
```xml
<ComboBox x:Name="SessionComboBox"
          l:Uids.Uid="SessionComboBox"
          ItemsSource="{x:Bind ViewModel.SortedSessions, Mode=OneWay}"
          SelectedItem="{x:Bind ViewModel.SelectedSession, Mode=TwoWay}"
          HorizontalAlignment="Stretch" ...
```
This needs to become a 2-column Grid: ComboBox + Pencil Button.

SelectedSession exposes `Session.Id` (= encoded projectDirName, the SessionNameStore key) and `DisplayName` (auto-derived).

NameChanged subscription site — InitializeAsync (line 315-355). Unsubscribe site — StopTimers (line 638-650).

RefreshSessionList display name derivation — current line 723:
```csharp
DisplayName = s.DisplayName,
```
This becomes:
```csharp
DisplayName = _sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName,
```

Direct-constructor test sites (only 2 files invoke `new MainViewModel(...)`):
- CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs:47-58
- CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs (similar pattern)

Other 5 test files use a harness/reflection — no constructor changes needed.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: MainViewModel constructor 12-arg + ISessionNameStore field + display-layer resolution + NameChanged subscribe/unsubscribe (RENAME-04, RENAME-08)</name>
  <read_first>
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs (lines 1-120 fields, 283-355 ctor + InitializeAsync, 638-650 StopTimers, 679-730 RefreshSessionList)
    - .planning/phases/26-persistent-session-renaming/26-CONTEXT.md (D-05, D-06, L-02, CD-04, CD-05)
    - .planning/research/PITFALLS.md (A2-P3 cross-tab live-update, G-1 marshaling rule)
    - CLAUDE.md (MVVM Conventions: G-1 always-TryEnqueue; Clean Code: Dispose resources explicitly; Secure Coding: Logout must fully terminate)
  </read_first>
  <files>
    CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs,
    CCInfoWindows/CCInfoWindows/App.xaml.cs,
    CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs,
    CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
  </files>
  <behavior>
    - MainViewModel constructor signature gains a 12th parameter `ISessionNameStore sessionNameStore` (after `IDispatcherQueue dispatcherQueue`).
    - The store is assigned to `private readonly ISessionNameStore _sessionNameStore`.
    - InitializeAsync subscribes once: `_sessionNameStore.NameChanged += OnSessionNameChanged;` immediately after the existing UnregisterAll/Register block (after line 323).
    - The handler is a private instance method (NOT a lambda — needed for symmetric `-=` cleanup per CD-05):
      ```csharp
      private void OnSessionNameChanged(object? sender, SessionNameChangedEventArgs args)
      {
          _dispatcherQueue.TryEnqueue(RefreshSessionList);
      }
      ```
    - StopTimers (line 638-650) gains `_sessionNameStore.NameChanged -= OnSessionNameChanged;` BEFORE `WeakReferenceMessenger.Default.UnregisterAll(this);`.
    - RefreshSessionList line 723 changes `DisplayName = s.DisplayName,` to `DisplayName = _sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName,`.
    - App.xaml.cs ConfigureServices factory adds `sp.GetRequiredService<ISessionNameStore>()` as the 12th argument.
    - MainViewModelRefreshTests.cs line 47-58 `new MainViewModel(...)` adds a `Mock<ISessionNameStore>().Object` (or the equivalent — see action) as the 12th argument.
    - MainViewModelAuthFlowTests.cs equivalent constructor call adds the same 12th argument.
    - The build produces 0 errors.
    - All Phase 24 + Phase 25 tests still pass.
    - MessengerThreadingConventionTests still passes (NameChanged is .NET event, NOT IRecipient<> — out of scope of that test per CONTEXT D-06).
  </behavior>
  <action>
    **Step 1a — `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs`**:

    a. Add `using CCInfoWindows.Models;` if not already imported (for `SessionNameChangedEventArgs`).

    b. After line 65 (`private readonly IBurnRateNotificationService _burnRateNotificationService;`), add:
    ```csharp
    private readonly ISessionNameStore _sessionNameStore;   // RENAME-07 / Phase 26
    ```

    c. Update the constructor signature (lines 283-294) to add the 12th parameter:
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
        IBurnRateNotificationService burnRateNotificationService,
        IDispatcherQueue dispatcherQueue,
        ISessionNameStore sessionNameStore)   // Phase 26 / RENAME-07
    ```

    d. In the constructor body, add `_sessionNameStore = sessionNameStore;` after `_dispatcherQueue = dispatcherQueue;` (line 306).

    e. In InitializeAsync, after the existing Register block (after the SonnetContextChangedMessage block ending around line 354), add:
    ```csharp
    // RENAME-04 / D-06 / L-02: subscribe via .NET event (NOT WeakReferenceMessenger — D-13 lesson).
    // Symmetric -= cleanup happens in StopTimers (CD-05).
    _sessionNameStore.NameChanged += OnSessionNameChanged;
    ```

    f. Add the private handler method near the existing message handlers (e.g., before RefreshSessionList line 679):
    ```csharp
    // G-1: NameChanged may arrive off-thread (singleton-published event from any caller).
    //      Always-TryEnqueue per CLAUDE.md MVVM Conventions, no HasThreadAccess shortcut.
    private void OnSessionNameChanged(object? sender, SessionNameChangedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(RefreshSessionList);
    }
    ```

    g. In StopTimers (currently line 638-650), add the unsubscribe line BEFORE `WeakReferenceMessenger.Default.UnregisterAll(this);`:
    ```csharp
    _sessionNameStore.NameChanged -= OnSessionNameChanged;
    ```

    h. In RefreshSessionList, change line 723:
    ```csharp
    // FROM:
    DisplayName = s.DisplayName,
    // TO:
    DisplayName = _sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName,   // RENAME-08
    ```

    **Step 1b — `CCInfoWindows/CCInfoWindows/App.xaml.cs`**:

    Update the `services.AddTransient<MainViewModel>(...)` factory (lines 165-176) to add the 12th argument:
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
        sp.GetRequiredService<IDispatcherQueue>(),
        sp.GetRequiredService<ISessionNameStore>()));   // Phase 26 / RENAME-07
    ```

    **Step 1c — Test files**:

    `CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs` (lines 47-58):
    Add a `var sessionNameStore = new Mock<ISessionNameStore>();` setup at the top of the harness alongside other mocks, then add `sessionNameStore.Object` as the 12th argument (after `new FakeDispatcherQueue()`).

    `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs`:
    Same pattern. Locate `new MainViewModel(...)` and add the 12th argument.

    Default mock behavior: `GetCustomName` returns null for all ids (no custom names in tests), `SetCustomName`/`ClearCustomName` no-ops, `SaveAsync` returns Task.FromResult(true). Tests do not need to assert NameChanged behavior in this task — that is exercised by Plan 01's SessionNameStoreTests.

    **Step 1d — Verify build + full test suite**:
    Run `dotnet build` then `dotnet test`. Confirm zero new failures.

    **Step 1e — Add a focused test** in MainViewModelRefreshTests.cs:
    `RefreshSessionList_AppliesCustomNameOverlay_WhenStoreReturnsValue`:
      - Arrange: setup ISessionNameStore mock so `GetCustomName("sessionA")` returns `"My Custom Name"`.
      - Arrange: setup IJsonlService mock to expose a Sessions list with one SessionInfo whose `Id == "sessionA"` and `DisplayName == "auto-derived"`.
      - Act: invoke RefreshSessionList (or the public path that triggers it — see harness pattern).
      - Assert: `viewModel.SortedSessions.First().DisplayName == "My Custom Name"`.

    `RefreshSessionList_FallsBackToAutoDerived_WhenStoreReturnsNull`:
      - Arrange: GetCustomName returns null.
      - Assert: SortedSessions.First().DisplayName == "auto-derived".
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --nologo</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~MessengerThreadingConventionTests" --nologo</automated>
  </verify>
  <acceptance_criteria>
    - `grep -c "ISessionNameStore sessionNameStore" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is 1 (constructor parameter).
    - `grep -c "_sessionNameStore" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is at least 5 (field decl, ctor assign, NameChanged += , NameChanged -=, GetCustomName call).
    - `grep -c "NameChanged += OnSessionNameChanged" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is 1.
    - `grep -c "NameChanged -= OnSessionNameChanged" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is 1.
    - `grep -c "_sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is 1.
    - `grep -c "_dispatcherQueue.TryEnqueue(RefreshSessionList)" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is 1 (in OnSessionNameChanged handler).
    - `grep -c "GetRequiredService<ISessionNameStore>" CCInfoWindows/CCInfoWindows/App.xaml.cs` is 1.
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` succeeds with 0 errors.
    - `dotnet test --nologo` reports 0 new failures versus baseline (pre-existing JsonlServiceTests + ClaudeApiServiceTests failures unchanged per REQUIREMENTS.md out-of-scope).
    - Phase 24 `MessengerThreadingConventionTests` still passes.
  </acceptance_criteria>
  <done>
    MainViewModel uses ISessionNameStore for display-layer resolution and NameChanged-driven refresh; all 7 test files compile; baseline test suite stays green.
  </done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: OpenRenameDialogCommand + ContentDialog + pencil button + 5 resw key pairs (RENAME-01, RENAME-05)</name>
  <read_first>
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml (lines 107-121 ComboBox row)
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs (existing event handlers)
    - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw (existing key naming pattern)
    - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
    - .planning/phases/26-persistent-session-renaming/26-CONTEXT.md (D-03, CD-02)
    - CLAUDE.md (MVVM Conventions: no code-behind logic — code-behind here is a pure view-side dialog launcher, not business logic)
    - CCInfoWindows/CCInfoWindows/Helpers/SessionNameSanitizer.cs (Plan 01 helper)
  </read_first>
  <files>
    CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs,
    CCInfoWindows/CCInfoWindows/Views/MainView.xaml,
    CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs,
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  </files>
  <behavior>
    - A pencil button appears immediately to the right of the SessionComboBox, with the Segoe MDL2 Pencil glyph (U+E70F).
    - The pencil button has a localized tooltip from `MainView.RenameButton.ToolTip` (DE: "Sitzung umbenennen", EN: "Rename session").
    - The pencil button's IsEnabled is bound to `ViewModel.HasSelectedSession` (true when SelectedSession != null).
    - Clicking the pencil opens a ContentDialog titled per `Dialog.RenameSession.Title` (DE: "Sitzung umbenennen", EN: "Rename Session").
    - The dialog body contains a TextBox pre-filled with the current SortedSessions item's DisplayName (custom OR auto-derived). MaxLength=100.
    - PrimaryButton text from `Dialog.RenameSession.SaveButton` (DE: "Speichern", EN: "Save"). Enabled only when TextBox has at least 1 non-whitespace char.
    - SecondaryButton text from `Dialog.RenameSession.CancelButton` (DE: "Abbrechen", EN: "Cancel").
    - CloseButton (tertiary "Reset") text from `Dialog.RenameSession.ResetButton` (DE: "Zurücksetzen", EN: "Reset"). Visible ONLY when `_sessionNameStore.GetCustomName(s.Id) != null`.
    - Save flow: take TextBox.Text → SessionNameSanitizer.Strip → `_sessionNameStore.SetCustomName(s.Id, sanitized)` → `await _sessionNameStore.SaveAsync()`. NameChanged fires automatically; subscriber refreshes the dropdown.
    - Reset flow: `_sessionNameStore.ClearCustomName(s.Id)` → `await _sessionNameStore.SaveAsync()`.
    - Cancel: dismiss dialog with no side effects.
    - DE+EN resw files contain ALL 5 new keys; ResourceCoverageTests passes.
    - HasSelectedSession property is added to MainViewModel as a derived bool: `public bool HasSelectedSession => SelectedSession != null;` with `[NotifyPropertyChangedFor(nameof(HasSelectedSession))]` on `_selectedSession`.
  </behavior>
  <action>
    **Step 2a — `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs`**:

    a. Locate `[ObservableProperty] private SessionDisplayItem? _selectedSession;` and add `[NotifyPropertyChangedFor(nameof(HasSelectedSession))]` attribute above it (or alongside existing notify attributes).

    b. Add the derived property near other computed properties:
    ```csharp
    /// <summary>True when a session is selected — gates the rename pencil button.</summary>
    public bool HasSelectedSession => SelectedSession != null;
    ```

    c. Add `[RelayCommand]` near other commands:
    ```csharp
    /// <summary>
    /// Triggered by the pencil button. View-layer code-behind handles the actual ContentDialog
    /// because ContentDialog requires an XamlRoot — but MainView passes the SelectedSession
    /// snapshot through this command so all rename logic stays in the ViewModel.
    /// Save flow is invoked by the View via SaveCustomNameAsync below.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private void OpenRenameDialog()
    {
        // Intentionally empty — the View's Click handler queries SelectedSession and shows
        // the dialog. The Command exists so the Button binds with proper CanExecute gating
        // and accessibility (RelayCommand publishes IsEnabled).
    }

    /// <summary>
    /// Persists a new custom name from the rename dialog. View calls this with already-trimmed input.
    /// Returns the resolved sanitized name (empty string => cleared).
    /// </summary>
    public async Task SaveCustomNameAsync(string sessionId, string newName)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        var sanitized = SessionNameSanitizer.Strip(newName).Trim();
        if (string.IsNullOrEmpty(sanitized))
        {
            _sessionNameStore.ClearCustomName(sessionId);
        }
        else
        {
            _sessionNameStore.SetCustomName(sessionId, sanitized);
        }
        await _sessionNameStore.SaveAsync();
    }

    /// <summary>Persists "no custom name" (Reset button in rename dialog).</summary>
    public async Task ClearCustomNameAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _sessionNameStore.ClearCustomName(sessionId);
        await _sessionNameStore.SaveAsync();
    }

    /// <summary>Lookup helper for the View — exposes whether a custom name currently exists.</summary>
    public bool HasCustomName(string sessionId)
        => _sessionNameStore.GetCustomName(sessionId) != null;
    ```

    Add `using CCInfoWindows.Helpers;` if not already present (for SessionNameSanitizer).

    **Step 2b — `CCInfoWindows/CCInfoWindows/Views/MainView.xaml`**:

    Locate the SessionComboBox block (lines 107-121). Replace with a 2-column Grid:

    ```xml
    <!-- ==================== SESSION DROPDOWN + RENAME PENCIL ==================== -->
    <Grid ColumnSpacing="6">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="32" />   <!-- CD-02: ComboBox shrinks by 32px to fit pencil -->
        </Grid.ColumnDefinitions>

        <ComboBox x:Name="SessionComboBox"
                  Grid.Column="0"
                  l:Uids.Uid="SessionComboBox"
                  ItemsSource="{x:Bind ViewModel.SortedSessions, Mode=OneWay}"
                  SelectedItem="{x:Bind ViewModel.SelectedSession, Mode=TwoWay}"
                  HorizontalAlignment="Stretch"
                  Background="{ThemeResource SegmentedBackgroundBrush}"
                  CornerRadius="8">
            <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="viewmodels:SessionDisplayItem">
                    <TextBlock Text="{x:Bind DisplayName}"
                               ToolTipService.ToolTip="{x:Bind TooltipText}"
                               VerticalAlignment="Center" />
                </DataTemplate>
            </ComboBox.ItemTemplate>
        </ComboBox>

        <!-- RENAME-01 / D-03: Pencil button opens ContentDialog -->
        <Button x:Name="RenameSessionButton"
                Grid.Column="1"
                l:Uids.Uid="MainViewRenameButton"
                Click="OnRenamePencilClicked"
                IsEnabled="{x:Bind ViewModel.HasSelectedSession, Mode=OneWay}"
                Background="Transparent"
                BorderThickness="0"
                Padding="4"
                CornerRadius="6"
                Width="32" Height="32"
                VerticalAlignment="Center">
            <FontIcon Glyph="&#xE70F;" FontSize="14"
                      Foreground="{ThemeResource SecondaryTextBrush}" />
        </Button>
    </Grid>
    ```

    **Step 2c — `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs`** — add the Click handler + dialog launcher:

    ```csharp
    private async void OnRenamePencilClicked(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.SelectedSession;
        if (selected == null) return;

        var sessionId = selected.Session.Id;
        var currentDisplayName = selected.DisplayName;
        var hasCustomName = ViewModel.HasCustomName(sessionId);

        var textBox = new TextBox
        {
            Text = currentDisplayName,
            MaxLength = 100,
            AcceptsReturn = false
        };

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = Localizer.Get().GetLocalizedString("Dialog.RenameSession.Title"),
            PrimaryButtonText = Localizer.Get().GetLocalizedString("Dialog.RenameSession.SaveButton"),
            SecondaryButtonText = Localizer.Get().GetLocalizedString("Dialog.RenameSession.CancelButton"),
            CloseButtonText = hasCustomName
                ? Localizer.Get().GetLocalizedString("Dialog.RenameSession.ResetButton")
                : string.Empty,
            DefaultButton = ContentDialogButton.Primary,
            Content = textBox
        };

        // Disable Save if TextBox is whitespace-only
        textBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        };
        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.SaveCustomNameAsync(sessionId, textBox.Text);
        }
        else if (result == ContentDialogResult.None && hasCustomName)
        {
            // CloseButton was the Reset button (only shown when a custom name exists)
            await ViewModel.ClearCustomNameAsync(sessionId);
        }
        // Secondary (Cancel) — no-op
    }
    ```

    Required `using` directives at top of MainView.xaml.cs:
    `using Microsoft.UI.Xaml;`, `using Microsoft.UI.Xaml.Controls;`, `using WinUI3Localizer;`.

    **Step 2d — Add 5 resw key pairs**:

    Both `Strings/de-DE/Resources.resw` and `Strings/en-US/Resources.resw` get these 5 keys. Use the existing `<data name="X" xml:space="preserve">` block format. Insert near other Dialog.* / MainView.* keys.

    DE values:
    | Key | Value |
    |-----|-------|
    | `Dialog.RenameSession.Title` | `Sitzung umbenennen` |
    | `Dialog.RenameSession.SaveButton` | `Speichern` |
    | `Dialog.RenameSession.CancelButton` | `Abbrechen` |
    | `Dialog.RenameSession.ResetButton` | `Zurücksetzen` |
    | `MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | `Sitzung umbenennen` |

    EN values:
    | Key | Value |
    |-----|-------|
    | `Dialog.RenameSession.Title` | `Rename Session` |
    | `Dialog.RenameSession.SaveButton` | `Save` |
    | `Dialog.RenameSession.CancelButton` | `Cancel` |
    | `Dialog.RenameSession.ResetButton` | `Reset` |
    | `MainViewRenameButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` | `Rename session` |

    The pencil button's `l:Uids.Uid="MainViewRenameButton"` will pick up the `ToolTipService.ToolTip` automatically via the `[using:...]` resource pattern (mirrors LoginReloadButton key style from Phase 20).

    **Step 2e — Extend ResourceCoverageTests** to cover the 5 new keys: locate `CCInfoWindows.Tests/L10N/ResourceCoverageTests.cs` (or whatever path it lives at) and add the 5 keys to the validated set. If the test enumerates a list, append; if it iterates an array, append. The structural check requires both files contain the same key names.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests" --nologo</automated>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --nologo</automated>
  </verify>
  <acceptance_criteria>
    - `grep -c "OpenRenameDialog" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is at least 1 (RelayCommand method).
    - `grep -c "SaveCustomNameAsync" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is at least 1.
    - `grep -c "ClearCustomNameAsync" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is at least 1.
    - `grep -c "HasSelectedSession" CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` is at least 2 (declaration + CanExecute).
    - `grep -c "RenameSessionButton" CCInfoWindows/CCInfoWindows/Views/MainView.xaml` is 1.
    - `grep -c "&#xE70F;" CCInfoWindows/CCInfoWindows/Views/MainView.xaml` is 1 (Pencil glyph).
    - `grep -c "OnRenamePencilClicked" CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` is 1.
    - `grep -c "Dialog.RenameSession.Title" CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` is 1.
    - `grep -c "Dialog.RenameSession.Title" CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` is 1.
    - `grep -c "MainViewRenameButton" CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` is at least 1.
    - `grep -c "MainViewRenameButton" CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` is at least 1.
    - `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` succeeds with 0 errors.
    - `dotnet test --filter "FullyQualifiedName~ResourceCoverageTests"` reports all keys present in both locales.
  </acceptance_criteria>
  <done>
    Pencil button + ContentDialog work end-to-end (see human-verify checkpoint below); 5 resw key pairs validated by ResourceCoverageTests; SessionNameSanitizer applied at Save (UI belt + store suspenders).
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <name>Task 3: Manual smoke for pencil + ContentDialog rename flow (RENAME-01, RENAME-04)</name>
  <what-built>
    Pencil button next to MainView session ComboBox; ContentDialog with Save/Cancel + optional Reset; ISessionNameStore.NameChanged event drives ComboBox refresh without restart; display name resolves via _sessionNameStore.GetCustomName(s.Id) ?? autoDerivedName.
  </what-built>
  <how-to-verify>
    1. Run: `dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
    2. Login if necessary. Wait for the session dropdown to populate.
    3. Visually confirm: a pencil glyph (small, secondary-text-brush) is visible immediately right of the ComboBox.
    4. With NO session selected: confirm the pencil button is disabled (greyed).
    5. Select a session from the ComboBox. Confirm the pencil becomes enabled.
    6. Click the pencil. A dialog titled "Sitzung umbenennen" / "Rename Session" appears. The TextBox is pre-filled with the current display name. The "Reset" button is HIDDEN (no custom name exists yet).
    7. Type a new name (e.g., "Mein Hauptprojekt"). Click "Speichern" / "Save".
    8. Confirm: the ComboBox now shows "Mein Hauptprojekt" for that session. No app restart was needed (RENAME-04).
    9. Open `%LOCALAPPDATA%\CCInfoWindows\session-names.json` in a text editor. Confirm: file exists, contains a JSON entry mapping the session's projectDirName → "Mein Hauptprojekt".
    10. Click the pencil again. This time the "Zurücksetzen" / "Reset" button IS visible (custom name exists). Click it.
    11. Confirm: the ComboBox reverts to the auto-derived name. The session-names.json no longer contains that entry.
    12. Open the dialog once more. Type some control characters (paste a string with embedded NUL/Tab if possible — e.g. `Test	Name`). Click Save.
    13. Confirm: the saved name is `TestName` (control chars stripped — RENAME-05). session-names.json contains the sanitized value.
    14. Close the app. Relaunch.
    15. Confirm: any custom name set before close is still present after restart (RENAME-03 persistence).
  </how-to-verify>
  <resume-signal>
    Type "approved" if all 15 steps pass. Describe any deviation and the orchestrator will route to gap closure.
  </resume-signal>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| User TextBox input ←→ MainViewModel | Untrusted; sanitized via SessionNameSanitizer.Strip + Trim before persistence. |
| MainView ContentDialog ←→ ISessionNameStore | View invokes ViewModel methods; ViewModel calls store. No direct view→store coupling. |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-26-07 | Tampering | TextBox input → store | mitigate | SessionNameSanitizer.Strip + Trim applied in MainViewModel.SaveCustomNameAsync (UI belt) and again in SessionNameStore.SetCustomName (store suspenders, Plan 01 D-07). |
| T-26-08 | Information Disclosure | Stack trace in ContentDialog on save failure | mitigate | SaveAsync returns bool; failures fall through silently. NameChanged still fires from in-memory mutation (worst case: rename visible but not persisted — same as v1.4 UsageHistoryService best-effort save). No exception leaks to UI per CLAUDE.md "No sensitive data in errors". |
| T-26-09 | Repudiation | NameChanged subscriber leak across MainViewModel re-instantiation | mitigate | StopTimers explicitly does `-= OnSessionNameChanged` per CD-05; symmetrical with `+=` in InitializeAsync. |
| T-26-10 | Denial of Service | Recursive NameChanged → RefreshSessionList → SetCustomName loop | accept | RefreshSessionList does NOT call SetCustomName — only reads via GetCustomName. No reentrancy path. Documented inline. |
</threat_model>

<verification>
1. `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — 0 errors.
2. `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --nologo` — full suite passes (subject to documented pre-existing baselines).
3. `dotnet test --filter "FullyQualifiedName~MessengerThreadingConventionTests"` — Phase 24 convention not regressed.
4. `dotnet test --filter "FullyQualifiedName~ResourceCoverageTests"` — DE+EN parity for the 5 new keys.
5. Manual smoke per Task 3 — all 15 steps green, including persistence across restart and control-char stripping.
</verification>

<success_criteria>
- [x] MainViewModel constructor takes 12 args with ISessionNameStore as the 12th (RENAME-07 wiring).
- [x] RefreshSessionList resolves DisplayName via GetCustomName overlay (RENAME-08).
- [x] NameChanged event subscribes in InitializeAsync, unsubscribes in StopTimers (CD-05).
- [x] Handler wraps RefreshSessionList in _dispatcherQueue.TryEnqueue (G-1).
- [x] OpenRenameDialogCommand + SaveCustomNameAsync + ClearCustomNameAsync + HasCustomName helpers exist on MainViewModel.
- [x] Pencil button + ContentDialog wired in MainView.xaml + xaml.cs (RENAME-01).
- [x] 5 resw key pairs (DE+EN) cover all new dialog/button strings.
- [x] App.xaml.cs MainViewModel factory passes ISessionNameStore as 12th arg.
- [x] MainViewModelRefreshTests + MainViewModelAuthFlowTests updated; new overlay tests green.
- [x] Phase 24 MessengerThreadingConventionTests still passes.
- [x] Manual smoke verifies persistence across restart + control-char stripping.
</success_criteria>

<output>
After completion, create `.planning/phases/26-persistent-session-renaming/26-02-SUMMARY.md` summarizing:
- Files modified
- Test updates (added / passing) and any baseline diffs
- Manual smoke result
- Open issues for Plan 03 (Sessions Settings tab) — none expected
</output>

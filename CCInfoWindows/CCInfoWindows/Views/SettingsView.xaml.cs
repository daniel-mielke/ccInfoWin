using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinUI3Localizer;
using static CCInfoWindows.ViewModels.SettingsViewModel;

namespace CCInfoWindows.Views;

/// <summary>
/// Settings page with refresh interval, theme toggle, logout, and (Phase 26) session rename.
/// </summary>
public sealed partial class SettingsView : Page
{
    private const string TabLabelLogSource = "SettingsView.ApplyTabTooltips";

    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        // Unloaded is wired via XAML attribute (Page.Unloaded="OnUnloaded")
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Initialize();
        ApplyCodeDrivenLabels();
        ViewModel.LanguageApplied += OnLanguageApplied;
        // D-10: if Settings opens with About already selected (rare — persistence),
        // start the timer immediately so "X minutes ago" is live from t=0.
        if (TabsSegmented.SelectedIndex == SettingsViewModel.AboutTabIndex)
            ViewModel.StartAboutTimestampTimer();
        ViewModel.Activate();   // Phase 26: subscribe to NameChanged + snapshot if Sessions tab visible

        // ORGID-01: subscribe to the org-picker dialog request event
        ViewModel.RequestOpenOrgPickerDialog += OnRequestOpenOrgPickerDialog;
    }

    private void OnSegmentedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // D-10: route Segmented.SelectionChanged to ViewModel timer lifecycle.
        if (TabsSegmented.SelectedIndex == SettingsViewModel.AboutTabIndex)
            ViewModel.StartAboutTimestampTimer();
        else
            ViewModel.StopAboutTimestampTimer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel?.Deactivate();   // Phase 26: unsubscribe NameChanged + ORGID Messenger unregister
        // D-10: belt-and-suspenders — always stop on Page.Unloaded (POLISH-08).
        ViewModel?.StopAboutTimestampTimer();
        // ORGID-01: symmetric unsubscribe (CD-05 pattern)
        if (ViewModel != null)
        {
            ViewModel.RequestOpenOrgPickerDialog -= OnRequestOpenOrgPickerDialog;
            ViewModel.LanguageApplied -= OnLanguageApplied;
        }
    }

    /// <summary>
    /// ORGID-01 / D-OG-03: shows the OrgPicker ContentDialog when SettingsViewModel requests it.
    /// ContentDialog requires XamlRoot — only available in the View layer (Phase 26 / CD-05 pattern).
    /// PrimaryButtonText / CloseButtonText are set from the Localizer (same approach as Phase 26
    /// RenameSessionDialog — WinUI 3 ContentDialog does not honor l:Uids.Uid for button text).
    /// ListView items render via the declarative OrgPickerItemTemplate defined in Page.Resources
    /// (Name bold, Uuid small secondary text).
    /// </summary>
    private bool _orgPickerDialogOpen;

    private async void OnRequestOpenOrgPickerDialog(object? sender, OrgPickerDialogRequest request)
    {
        // WinUI 3 allows only one open ContentDialog per XamlRoot; a second ShowAsync throws,
        // and this method is async void — the exception would tear down the process.
        if (_orgPickerDialogOpen)
        {
            request.CompletionSource.TrySetResult(ContentDialogResult.None);
            return;
        }

        var localizer = Localizer.Get();
        var hasOrgs = ViewModel.AvailableOrganizations.Count > 0;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localizer.GetLocalizedString("OrgPickerDialogTitle"),
            PrimaryButtonText = localizer.GetLocalizedString("OrgPickerDialogSwitchButton"),
            CloseButtonText = localizer.GetLocalizedString("OrgPickerDialogCancelButton"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,  // enabled once an org is selected
        };

        if (hasOrgs)
        {
            var listView = new ListView
            {
                ItemsSource = ViewModel.AvailableOrganizations,
                SelectionMode = ListViewSelectionMode.Single,
                ItemTemplate = (DataTemplate)Resources["OrgPickerItemTemplate"],
            };

            listView.SelectionChanged += (s, e) =>
            {
                if (listView.SelectedItem is OrganizationInfo selected)
                {
                    ViewModel.SelectedOrgPickerItem = selected;
                    dialog.IsPrimaryButtonEnabled = true;
                }
            };

            dialog.Content = new ScrollViewer { Width = 400, MaxHeight = 300, Content = listView };
        }
        else
        {
            // Empty list means the /api/organizations fetch failed (bridge down, expired
            // session, Cloudflare challenge) — say so instead of showing a blank dialog.
            dialog.Content = new TextBlock
            {
                Width = 400,
                Text = localizer.GetLocalizedString("OrgPickerDialogNoOrgs"),
                TextWrapping = TextWrapping.Wrap,
            };
        }

        _orgPickerDialogOpen = true;
        try
        {
            var result = await dialog.ShowAsync();
            request.CompletionSource.TrySetResult(result);
        }
        catch (Exception ex)
        {
            AppLog.Write($"{nameof(SettingsView)}.{nameof(OnRequestOpenOrgPickerDialog)}", ex,
                "org picker dialog could not be shown");
            request.CompletionSource.TrySetResult(ContentDialogResult.None);
        }
        finally
        {
            _orgPickerDialogOpen = false;
        }
    }

    /// <summary>
    /// Everything on this page whose text the localizer cannot re-apply on its own. Runs on Loaded
    /// and again on every successful language switch.
    /// </summary>
    private void ApplyCodeDrivenLabels()
    {
        ApplyTabTooltips();
    }

    /// <summary>
    /// The tab strip renders a glyph badge per tab and no text, so <see cref="IconLabel"/> supplies
    /// both the tooltip and the accessible name from one key. Before this shared call the tabs got a
    /// tooltip only — an unguarded localizer read with no fallback, which painted an empty tooltip
    /// on a missing key and announced nothing at all to a screen reader.
    /// </summary>
    private void ApplyTabTooltips()
    {
        IconLabel.Apply(TabGeneral, "SettingsTabGeneral", "General", TabLabelLogSource);
        IconLabel.Apply(TabUpdates, "SettingsTabUpdates", "Updates", TabLabelLogSource);
        IconLabel.Apply(TabAccount, "SettingsTabAccount", "Account", TabLabelLogSource);
        IconLabel.Apply(TabSessions, "SettingsTabSessions", "Sessions", TabLabelLogSource);  // Phase 26
        IconLabel.Apply(TabAbout, "SettingsTabAbout", "About", TabLabelLogSource);
    }

    private void OnLanguageApplied(object? sender, EventArgs e) => ApplyCodeDrivenLabels();

    /// <summary>
    /// The row Enter has just committed, together with the value it committed. Moving focus off the
    /// TextBox makes WinUI raise LostFocus for that row, and LostFocus is delivered asynchronously,
    /// so the echo would otherwise run the command a second time. Matching on the value as well
    /// keeps the guard from ever swallowing a genuine commit: text the user changed after pressing
    /// Enter no longer matches and is saved normally.
    /// </summary>
    private SessionRenameItem? _rowCommittedByEnter;
    private string _valueCommittedByEnter = string.Empty;

    // Phase 26 / RENAME-02: TextBox commit on LostFocus — persists via ISessionNameStore
    private async void OnSessionRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: SessionRenameItem item }) return;

        if (ReferenceEquals(item, _rowCommittedByEnter)
            && string.Equals(item.CustomName, _valueCommittedByEnter, StringComparison.Ordinal))
        {
            _rowCommittedByEnter = null;
            return;
        }

        await ViewModel.SaveSessionCustomNameCommand.ExecuteAsync(item);
    }

    // Phase 26 / RENAME-02: TextBox commit on Enter key — persists via ISessionNameStore
    private async void OnSessionRenameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        if (sender is not TextBox tb || tb.Tag is not SessionRenameItem item) return;

        e.Handled = true;
        await ViewModel.SaveSessionCustomNameCommand.ExecuteAsync(item);

        // The command writes the sanitized value back, so this is what the echoed LostFocus will see.
        _rowCommittedByEnter = item;
        _valueCommittedByEnter = item.CustomName;

        // Move focus off the TextBox so the user sees the commit visually
        tb.IsEnabled = false;
        tb.IsEnabled = true;
    }
}

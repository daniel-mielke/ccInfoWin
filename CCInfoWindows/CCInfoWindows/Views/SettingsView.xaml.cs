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
    // D-10: tab order defined in SettingsViewModel constants (shared)
    private const int AboutTabIndex = SettingsViewModel.AboutTabIndex;

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
        ApplyTabTooltips();
        // D-10: if Settings opens with About already selected (rare — persistence),
        // start the timer immediately so "X minutes ago" is live from t=0.
        if (TabsSegmented.SelectedIndex == AboutTabIndex)
            ViewModel.StartAboutTimestampTimer();
        ViewModel.Activate();   // Phase 26: subscribe to NameChanged + snapshot if Sessions tab visible

        // ORGID-01: subscribe to the org-picker dialog request event
        ViewModel.RequestOpenOrgPickerDialog += OnRequestOpenOrgPickerDialog;
    }

    private void OnSegmentedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // D-10: route Segmented.SelectionChanged to ViewModel timer lifecycle.
        // ViewModel may be null during early initialization; guard.
        if (ViewModel == null) return;

        if (TabsSegmented.SelectedIndex == AboutTabIndex)
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
            ViewModel.RequestOpenOrgPickerDialog -= OnRequestOpenOrgPickerDialog;
    }

    /// <summary>
    /// ORGID-01 / D-OG-03: shows the OrgPicker ContentDialog when SettingsViewModel requests it.
    /// ContentDialog requires XamlRoot — only available in the View layer (Phase 26 / CD-05 pattern).
    /// PrimaryButtonText / CloseButtonText are set from the Localizer (same approach as Phase 26
    /// RenameSessionDialog — WinUI 3 ContentDialog does not honor l:Uids.Uid for button text).
    /// DataTemplate built programmatically: each item is shown as a StackPanel with Name (bold)
    /// and Uuid (small secondary text).
    /// </summary>
    private async void OnRequestOpenOrgPickerDialog(object? sender, OrgPickerDialogRequest request)
    {
        var listView = new ListView
        {
            ItemsSource = ViewModel.AvailableOrganizations,
            SelectionMode = ListViewSelectionMode.Single,
        };

        // Programmatic item container factory — avoids XamlReader (not available in WinUI 3)
        listView.ContainerContentChanging += (s, e) =>
        {
            if (e.Item is not OrganizationInfo org) return;
            var panel = new StackPanel { Margin = new Thickness(4, 8, 4, 8), Spacing = 2 };
            panel.Children.Add(new TextBlock { Text = org.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = org.Uuid, FontSize = 11 });
            e.ItemContainer.Content = panel;
            e.Handled = true;
        };

        // Wire SelectedItem to ViewModel
        listView.SelectionChanged += (s, e) =>
        {
            if (listView.SelectedItem is OrganizationInfo selected)
                ViewModel.SelectedOrgPickerItem = selected;
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get().GetLocalizedString("Dialog.OrgPicker.Title"),
            PrimaryButtonText = Localizer.Get().GetLocalizedString("Dialog.OrgPicker.SwitchButton"),
            CloseButtonText = Localizer.Get().GetLocalizedString("Dialog.OrgPicker.CancelButton"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                Width = 400,
                MaxHeight = 300,
                Content = listView,
            },
        };

        var result = await dialog.ShowAsync();
        request.CompletionSource.TrySetResult(result);
    }

    private void ApplyTabTooltips()
    {
        var localizer = Localizer.Get();
        ToolTipService.SetToolTip(TabGeneral, localizer.GetLocalizedString("SettingsTabGeneral"));
        ToolTipService.SetToolTip(TabUpdates, localizer.GetLocalizedString("SettingsTabUpdates"));
        ToolTipService.SetToolTip(TabAccount, localizer.GetLocalizedString("SettingsTabAccount"));
        ToolTipService.SetToolTip(TabSessions, localizer.GetLocalizedString("SettingsTabSessions"));  // Phase 26
        ToolTipService.SetToolTip(TabAbout, localizer.GetLocalizedString("SettingsTabAbout"));
    }

    // Phase 26 / RENAME-02: TextBox commit on LostFocus — persists via ISessionNameStore
    private async void OnSessionRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is SessionRenameItem item)
        {
            await ViewModel.SaveSessionCustomNameCommand.ExecuteAsync(item);
        }
    }

    // Phase 26 / RENAME-02: TextBox commit on Enter key — persists via ISessionNameStore
    private async void OnSessionRenameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        if (sender is TextBox tb && tb.Tag is SessionRenameItem item)
        {
            e.Handled = true;
            await ViewModel.SaveSessionCustomNameCommand.ExecuteAsync(item);
            // Move focus off the TextBox so the user sees the commit visually
            tb.IsEnabled = false;
            tb.IsEnabled = true;
        }
    }
}

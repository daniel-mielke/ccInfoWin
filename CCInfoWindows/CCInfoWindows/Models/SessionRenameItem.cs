using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCInfoWindows.Models;

/// <summary>
/// Row model for the Settings Sessions tab. CustomName is two-way bound to a TextBox
/// in SettingsView; the View's LostFocus / Enter handler invokes SaveSessionCustomNameCommand
/// to persist via ISessionNameStore.
/// </summary>
public partial class SessionRenameItem : ObservableObject
{
    public required string SessionId { get; init; }
    public required string DefaultName { get; init; }
    public bool IsOrphan { get; init; }

    /// <summary>
    /// SettingsViewModel.ClearSessionCustomNameCommand, projected onto the row so the Clear button
    /// can reach it with a compile-checked x:Bind. A DataTemplate opens its own XAML namescope, so
    /// neither {Binding ElementName=…} nor x:Bind can address a page-level element from inside the
    /// template — the command has to travel with the item.
    /// </summary>
    public ICommand? ClearCustomNameCommand { get; init; }

    /// <summary>
    /// Localized label for the Clear button, used as both its tooltip and its automation name. It
    /// travels with the row for the same namescope reason as the command, and it is resolved in the
    /// ViewModel rather than by l:Uids.Uid because the localizer's default action for a
    /// property-less key on a ContentControl would replace the button's icon with the text.
    /// </summary>
    public string ClearCustomNameLabel { get; init; } = string.Empty;

    [ObservableProperty]
    private string _customName = string.Empty;
}

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

    [ObservableProperty]
    private string _customName = string.Empty;
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace CCInfoWindows.Models;

/// <summary>
/// A dropdown entry whose caption is translated but whose value is its position.
///
/// The caption has to be observable. A closed ComboBox renders the selected entry from a cached
/// selection box that is only re-read when the selection changes, so assigning
/// <c>ComboBoxItem.Content</c> after the fact updates the open list and leaves the closed control
/// showing the previous language — or, on a fresh page, nothing at all. Raising PropertyChanged on a
/// bound <see cref="Label"/> reaches both, and replaces no item, so <c>SelectedIndex</c> and the
/// user's setting are never disturbed.
/// </summary>
public partial class LabeledOption : ObservableObject
{
    [ObservableProperty]
    private string _label;

    public LabeledOption(string label) => _label = label;
}

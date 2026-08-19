using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Labels a control whose whole content is a glyph, in code, because WinUI3Localizer never applies
/// the "Control.[using:Namespace]Class.Property" attached-property form of a resw key: those entries
/// resolve in the resource tests and reach no control at all.
///
/// MainView's footer buttons and SettingsView's tab strip both need it, and the two hand-written
/// copies had already drifted — MainView set an accessible name and routed the lookup through the
/// shared <see cref="LocalizedText"/> rule, SettingsView did neither, so the icon-only Settings tabs
/// announced nothing to a screen reader and painted an empty tooltip when a key was missing.
/// </summary>
internal static class IconLabel
{
    /// <summary>
    /// One string serves as both the tooltip and the name a screen reader announces: the glyph is the
    /// control's only other content, so there is nothing else either affordance could read.
    /// </summary>
    internal static void Apply(DependencyObject control, string uid, string fallback, string logSource)
    {
        var label = LocalizedText.Resolve(uid, fallback, logSource);
        ToolTipService.SetToolTip(control, label);
        AutomationProperties.SetName(control, label);
    }
}

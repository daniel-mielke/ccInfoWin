namespace CCInfoWindows.Messages;

/// <summary>
/// ORGID-03: sent by MainViewModel.ResolveOrgMismatchCommand to instruct SettingsViewModel
/// to open the OrgPicker ContentDialog after the user navigates to Settings → Account.
/// SettingsViewModel.IRecipient&lt;OpenOrgPickerRequestedMessage&gt;.Receive wraps in
/// IDispatcherQueue.TryEnqueue per G-1 convention.
/// </summary>
public sealed record OpenOrgPickerRequestedMessage();

namespace CCInfoWindows.Models;

/// <summary>
/// Payload for ISessionNameStore.NameChanged. Carries only the affected SessionId per CD-04 —
/// consumers re-resolve the current value via GetCustomName to avoid stale-data races when
/// multiple changes pile up between dispatcher ticks.
/// </summary>
public sealed class SessionNameChangedEventArgs : EventArgs
{
    public required string SessionId { get; init; }
}

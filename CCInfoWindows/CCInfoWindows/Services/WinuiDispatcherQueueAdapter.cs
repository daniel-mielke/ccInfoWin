using CCInfoWindows.Services.Interfaces;
using Microsoft.UI.Dispatching;

namespace CCInfoWindows.Services;

/// <summary>
/// Production adapter wrapping Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().
/// Resolved once at construction in App.xaml.cs ConfigureServices (UI thread context guaranteed
/// because OnLaunched runs on the UI thread). Singleton lifetime (L-02).
/// </summary>
internal sealed class WinuiDispatcherQueueAdapter : IDispatcherQueue
{
    private readonly DispatcherQueue _inner;

    public WinuiDispatcherQueueAdapter()
    {
        // Must be constructed on the UI thread. App.OnLaunched satisfies this contract.
        _inner = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "WinuiDispatcherQueueAdapter must be constructed on a thread that owns a DispatcherQueue. "
                + "Ensure ConfigureServices runs from App.OnLaunched (UI thread).");
    }

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _inner.TryEnqueue(() => action());
    }

    public bool HasThreadAccess => _inner.HasThreadAccess;
}

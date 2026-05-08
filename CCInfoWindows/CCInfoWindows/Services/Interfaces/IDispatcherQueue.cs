namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Abstraction over Microsoft.UI.Dispatching.DispatcherQueue that allows headless unit testing
/// without a Windows App SDK UI context. Production code uses WinuiDispatcherQueueAdapter;
/// tests supply FakeDispatcherQueue.
///
/// G-1 convention (CLAUDE.md, MVVM Conventions): every IRecipient&lt;T&gt;.Receive(T) body that
/// mutates [ObservableProperty] fields, calls INavigationService, or touches XAML controls
/// MUST wrap the body in IDispatcherQueue.TryEnqueue. Always-TryEnqueue, no HasThreadAccess
/// shortcut (PITFALLS C2-P1).
/// </summary>
public interface IDispatcherQueue
{
    /// <summary>Enqueues the action to run on the dispatcher's thread. Returns false if the queue is shut down.</summary>
    bool TryEnqueue(Action action);

    /// <summary>True if the calling thread is the dispatcher's owning thread.</summary>
    bool HasThreadAccess { get; }
}

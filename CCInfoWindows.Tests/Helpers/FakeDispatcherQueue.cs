using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Headless test double for IDispatcherQueue. Two modes:
///   - Inline (default): TryEnqueue executes the action immediately on the calling thread.
///     Mirrors single-threaded xUnit test execution and exposes off-thread bugs synchronously.
///   - Queued: actions are stored and run only when Pump() is called. Use for tests that
///     need to assert ordering or verify fire-and-forget timing.
/// HasThreadAccess defaults to true (test thread "owns" the fake dispatcher); can be overridden
/// via property setter to simulate off-thread Send/Receive paths.
/// </summary>
public sealed class FakeDispatcherQueue : IDispatcherQueue
{
    private readonly Queue<Action> _queued = new();

    public bool ExecuteInline { get; set; } = true;
    public bool HasThreadAccess { get; set; } = true;
    public int InvocationCount { get; private set; }
    public IReadOnlyCollection<Action> PendingActions => _queued;

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvocationCount++;
        if (ExecuteInline)
        {
            action();
            return true;
        }
        _queued.Enqueue(action);
        return true;
    }

    /// <summary>Drains queued actions in FIFO order. Only meaningful when ExecuteInline is false.</summary>
    public int Pump()
    {
        int count = 0;
        while (_queued.Count > 0)
        {
            _queued.Dequeue()();
            count++;
        }
        return count;
    }
}

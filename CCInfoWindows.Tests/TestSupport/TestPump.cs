namespace CCInfoWindows.Tests.TestSupport;

/// <summary>
/// Models the WinUI dispatcher: a captured continuation is Posted to a queue that only the owning
/// thread drains. This one is never drained — that is the point, because the thread that would drain it
/// is the one blocked inside the synchronous writer.
/// </summary>
internal sealed class RecordingPumpContext : SynchronizationContext
{
    private int _postCount;

    public int PostCount => Volatile.Read(ref _postCount);

    public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _postCount);

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}

/// <summary>
/// The one scaffold for the sync-over-async deadlock regression the JSON stores share. Both stores used
/// to carry an independently maintained copy — including the pump double, the pragma and the timeout —
/// so a fix applied to one left the other passing vacuously while the deadlock it guards went
/// unnoticed until a user's window hung on close.
/// </summary>
internal static class TestPump
{
    private static readonly TimeSpan PendingWriteTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Installs an undrainable dispatcher, starts the asynchronous writer, then blocks in the
    /// synchronous one on the same thread — the shape of MainWindow.OnClosing. Without
    /// ConfigureAwait(false) inside the store, the release continuation is queued to this pump, which
    /// the blocked thread can never drain, and the write never completes.
    /// </summary>
    /// <param name="beginAsyncWrite">
    /// Starts the asynchronous write. Its payload must be big enough that the write cannot plausibly
    /// finish before it is awaited, or there is no continuation in flight and the assertion is vacuous.
    /// </param>
    /// <param name="blockingWrite">Calls the store's synchronous writer on this same thread.</param>
    internal static void AssertAsyncWriteSurvivesABlockingWrite(Func<Task> beginAsyncWrite, Action blockingWrite)
    {
        var pump = new RecordingPumpContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(pump);
        try
        {
            var pending = beginAsyncWrite();

            blockingWrite();

            // xUnit1031 (no blocking task operations) cannot be honoured here: the caller must stay
            // synchronous. The pump installed above is deliberately never drained, so awaiting while it
            // is current would hang the test itself, and awaiting with ConfigureAwait(false) would
            // resume the context restore on a pooled thread — leaving the undrainable pump installed on
            // the xUnit worker thread for whatever test runs there next. A bounded Wait is the only
            // correct join.
#pragma warning disable xUnit1031
            Assert.True(pending.Wait(PendingWriteTimeout), "the async write never completed");
#pragma warning restore xUnit1031
            Assert.Equal(0, pump.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

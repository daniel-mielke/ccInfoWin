namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Marks an IRecipient&lt;T&gt;.Receive(T) method as exempt from G-1 thread-marshaling rule.
/// MessengerThreadingConventionTests asserts EITHER this attribute is present OR the method body
/// calls IDispatcherQueue.TryEnqueue. Reason MUST be non-empty (mirrors [Obsolete("reason")] spirit).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ThreadSafeReceiveAttribute : Attribute
{
    public string Reason { get; }

    public ThreadSafeReceiveAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason must be non-empty.", nameof(reason));
        Reason = reason;
    }
}

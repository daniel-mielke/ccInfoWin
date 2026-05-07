using Xunit;

namespace CCInfoWindows.Tests;

/// <summary>
/// xUnit collection definition for tests that use WeakReferenceMessenger.Default.
/// Tests in this collection run sequentially to prevent cross-test messenger
/// contamination (WeakReferenceMessenger.Default is process-global).
/// </summary>
[CollectionDefinition("WeakReferenceMessenger")]
public class MessengerTestCollection : ICollectionFixture<MessengerTestFixture>
{
}

/// <summary>
/// Shared fixture that resets WeakReferenceMessenger.Default before and after
/// each test class execution to ensure a clean messenger state.
/// </summary>
public class MessengerTestFixture : IDisposable
{
    public void Dispose()
    {
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Reset();
    }
}

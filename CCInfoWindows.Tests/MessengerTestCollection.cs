using Xunit;

namespace CCInfoWindows.Tests;

/// <summary>
/// xUnit collection definition for tests that touch WeakReferenceMessenger.Default.
/// Tests in this collection run sequentially to prevent cross-test messenger
/// contamination (WeakReferenceMessenger.Default is process-global).
///
/// MessengerCollectionConventionTests enforces the membership: a test class that touches the messenger
/// from outside this collection runs in parallel with the ViewModels under test, and its Send is
/// delivered to them.
/// </summary>
[CollectionDefinition("WeakReferenceMessenger")]
public class MessengerTestCollection : ICollectionFixture<MessengerTestFixture>
{
}

/// <summary>
/// Shared fixture that resets WeakReferenceMessenger.Default once, when the collection has finished, so
/// registrations made here cannot be delivered to later test classes. xUnit creates a collection fixture
/// once per collection, so this is not per-test or per-class isolation — each class still unregisters its
/// own recipients.
///
/// Reset() unregisters every recipient in the process, which is the second reason membership of this
/// collection is mandatory rather than cosmetic: a messenger-touching class left outside it could be
/// running in parallel and have its registrations wiped mid-test.
/// </summary>
public class MessengerTestFixture : IDisposable
{
    public void Dispose()
    {
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Reset();
    }
}

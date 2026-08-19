using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Moq;

namespace CCInfoWindows.Tests.TestSupport;

/// <summary>
/// Shared fixture for the ClaudeApiService suites: the two mocks the service is built from, a private
/// cache directory, and the messenger teardown.
///
/// Derived suites must still carry <c>[Collection("WeakReferenceMessenger")]</c> themselves — the 401
/// path sends AuthStateChangedMessage on the process-global WeakReferenceMessenger.Default, and xUnit
/// runs collections in parallel, so outside that collection the Send reaches live ViewModels under test
/// elsewhere and drives their navigation.
/// </summary>
public abstract class ClaudeApiServiceTestBase : IDisposable
{
    private readonly TempDirectory _temp;

    protected ClaudeApiServiceTestBase(string tempDirectoryPrefix = "ccinfo_api_")
    {
        BridgeMock.Setup(b => b.IsInitialized).Returns(true);
        _temp = new TempDirectory(tempDirectoryPrefix);
    }

    protected Mock<IWebViewBridge> BridgeMock { get; } = new();

    protected Mock<ICredentialService> CredentialMock { get; } = new();

    /// <summary>Cache directory handed to the service, in place of the real %LOCALAPPDATA%.</summary>
    protected string CacheDirectory => _temp.Path;

    protected ClaudeApiService CreateService()
        => new(BridgeMock.Object, CredentialMock.Object, CacheDirectory);

    /// <summary>
    /// The single unregister point, and the reason no test body needs a try/finally of its own: xUnit
    /// runs Dispose after every test, failed ones included, so a registration cannot survive a failed
    /// assertion and start receiving another test's messages.
    /// </summary>
    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _temp.Dispose();
    }
}

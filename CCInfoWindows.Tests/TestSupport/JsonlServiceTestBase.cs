using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Tests.TestSupport;

/// <summary>
/// Shared fixture for every JsonlService suite: a private projects root, a cache directory inside it,
/// a tracked service list and the one factory that builds the service.
///
/// The cache override is load-bearing, not cosmetic. Without it JsonlService writes jsonl-cache.json
/// to the real %LOCALAPPDATA%\CCInfoWindows, so running the suite overwrites the developer's live
/// cache — an F.I.R.S.T. Independent violation with an effect outside the test process. Four suites
/// each passed it by hand; owning <see cref="BuildService"/> here is what makes a fifth suite unable
/// to forget it.
/// </summary>
public abstract class JsonlServiceTestBase : IDisposable
{
    private const string CacheDirectoryName = "cache";

    private readonly TempDirectory _temp;
    private readonly List<JsonlService> _services = [];

    protected JsonlServiceTestBase(string tempDirectoryPrefix = "jsonl-tests-")
    {
        _temp = new TempDirectory(tempDirectoryPrefix);
        CacheDir = _temp.CreateSubdirectory(CacheDirectoryName);
    }

    /// <summary>The projects root every service is pointed at — this test's own temp directory.</summary>
    protected string ProjectsDir => _temp.Path;

    /// <summary>Cache directory, a subdirectory of <see cref="ProjectsDir"/>.</summary>
    protected string CacheDir { get; }

    /// <summary>
    /// Builds a service against this test's directories and tracks it for teardown.
    /// </summary>
    protected JsonlService BuildService(IPricingService? pricingService = null)
    {
        var service = new JsonlService(
            projectsDirectoryOverride: ProjectsDir,
            cacheDirectoryOverride: CacheDir,
            pricingService: pricingService);
        _services.Add(service);
        return service;
    }

    /// <summary>
    /// Releases every service built by <see cref="BuildService"/>. The in-test Stop() calls do not run
    /// when an assertion fails, and a live FileSystemWatcher on the projects root would then race the
    /// directory delete and mask the real failure. JsonlService.Dispose is idempotent, so the tests
    /// that additionally wrap the instance in <c>using</c> are fine.
    /// </summary>
    protected void ReleaseServices()
    {
        foreach (var service in _services)
            service.Dispose();

        _services.Clear();
    }

    public virtual void Dispose()
    {
        ReleaseServices();
        _temp.Dispose();
    }
}

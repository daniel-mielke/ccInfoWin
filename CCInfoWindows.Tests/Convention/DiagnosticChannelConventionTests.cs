using CCInfoWindows.Tests.Helpers;

namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Finding 34, repo-wide: <c>Debug.WriteLine</c> carries <c>[Conditional("DEBUG")]</c>, so in the
/// Release build CLAUDE.md mandates those call sites are literally absent. Every handled failure the
/// shipped app can hit has to reach <see cref="CCInfoWindows.Helpers.AppLog"/> instead.
///
/// AppHostConventionTests already pins this for App, MainWindow and MainView. This is the whole-project
/// form, so a helper or service reintroducing the erased channel fails here rather than being found by
/// the next reviewer — 41 such sites existed before the remediation, and two survived until this test.
/// </summary>
public class DiagnosticChannelConventionTests
{
    private const string AppLogFileName = "AppLog.cs";

    /// <summary>Matches Debug.Write and Debug.WriteLine, qualified or not.</summary>
    private const string ErasedChannel = "Debug.Write";

    [Fact]
    public void NoProductionFileOutsideAppLog_ReportsThroughTheErasedDebugChannel()
    {
        var offenders = ProductionSourceFiles.FilesContaining(ErasedChannel, AppLogFileName).ToList();

        Assert.True(
            offenders.Count == 0,
            $"These files report through a channel the Release build erases; use AppLog.Write: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void AppLog_IsTheOneFileAllowedToMirrorIntoTheDebugger()
    {
        // Guards the scan above against passing vacuously: a typo in the needle would leave a broken
        // scan looking like a clean repo. AppLog is exempt on purpose — it mirrors each entry into the
        // debugger output in addition to writing the file, which is the visibility the replaced call
        // sites used to provide.
        Assert.Contains(ErasedChannel, ProductionSourceFiles.Read(AppLogFileName));
    }
}

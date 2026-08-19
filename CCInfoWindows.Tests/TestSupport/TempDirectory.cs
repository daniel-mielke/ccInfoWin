using IOPath = System.IO.Path;

namespace CCInfoWindows.Tests.TestSupport;

/// <summary>
/// A throwaway directory under %TEMP%, created on construction and best-effort deleted on Dispose.
///
/// Every stateful suite used to hand-roll this, and the three teardown variants that grew apart were
/// not equivalent: the ones without the IOException swallow fail the whole test class on a teardown
/// race — an AV scanner or a lingering FileSystemWatcher handle — instead of on the assertion that
/// matters. A leaked temp directory is the strictly better failure mode, so it is the only one here.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    /// <param name="prefix">
    /// Prepended to the generated GUID. Only ever read by a human staring at %TEMP% after a crashed
    /// run, which is exactly why suites pass their own.
    /// </param>
    public TempDirectory(string prefix = "ccinfo-test-")
    {
        Path = IOPath.Combine(IOPath.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Absolute path of the directory. It exists from construction until Dispose.</summary>
    public string Path { get; }

    /// <summary>Creates a subdirectory and returns its absolute path.</summary>
    public string CreateSubdirectory(string name)
    {
        var path = IOPath.Combine(Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { /* another handle still open on a temp file; the OS reclaims it */ }
    }
}

using System.Reflection;
using System.Xml.Linq;
using CCInfoWindows.Helpers;
using CCInfoWindows.Services;

namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Guards the build, release and packaging configuration that no compiler checks:
/// the installer's source directory and version derivation, the version strings the
/// update oracle depends on, the trimming ban, NuGet source pinning, and the declared
/// platform surface. Findings 1, 3, 26, 27, 45 and 46 of the 2026-08-06 repo review all
/// describe defects that were invisible because these files are only read by tools.
/// </summary>
public class BuildConfigurationTests
{
    private const string MainCsprojRelativePath = @"CCInfoWindows\CCInfoWindows\CCInfoWindows.csproj";
    private const string TestCsprojRelativePath = @"CCInfoWindows.Tests\CCInfoWindows.Tests.csproj";
    private const string SolutionRelativePath = @"CCInfoWindows\CCInfoWindows.sln";
    private const string InstallerRelativePath = @"installer\setup.iss";
    private const string NuGetConfigRelativePath = "NuGet.config";
    private const string ReadmeRelativePath = "README.md";

    private const string SanctionedReleaseDir =
        @"..\CCInfoWindows\CCInfoWindows\bin\x64\Release\net9.0-windows10.0.19041.0";
    private const string NuGetOrgSourceUrl = "https://api.nuget.org/v3/index.json";
    private const string SolutionAnyCpuPlatform = "Any CPU";

    /// <summary>
    /// Maps each credential-target define in setup.iss to the CredentialService constant it mirrors.
    /// </summary>
    private static readonly (string DefineName, string ConstantName)[] CredentialDefines =
    [
        ("SessionCredentialTarget", "CredentialTarget"),
        ("OrgCredentialTarget", "OrgCredentialTarget"),
    ];

    [Fact]
    public void Installer_PackagesTheSanctionedReleaseDirectory()
    {
        var installer = ReadRepoFile(InstallerRelativePath);

        Assert.Contains($"#define ReleaseDir \"{SanctionedReleaseDir}\"", installer);
        Assert.Contains("Source: \"{#ReleaseDir}\\*\"", installer);

        Assert.DoesNotContain(@"win-x64\publish", installer);
        Assert.Contains("Excludes: \"\\win-x64,*.pdb\"", installer);
    }

    [Fact]
    public void Installer_DerivesItsVersionFromTheBuiltExecutable()
    {
        var installer = ReadRepoFile(InstallerRelativePath);

        Assert.Contains("#define MyAppVersion GetVersionNumbersString(ReleaseExePath)", installer);
        Assert.DoesNotContain("#define MyAppVersion \"", installer);
    }

    [Fact]
    public void Installer_FailsToCompileWhenTheReleaseBuildIsMissing()
    {
        var installer = ReadRepoFile(InstallerRelativePath);

        Assert.Contains("#if !FileExists(ReleaseExePath)", installer);
        Assert.Contains("#error", installer);
    }

    [Fact]
    public void Installer_RemovesTheRuntimeDataDirectoryOnUninstall()
    {
        var installer = ReadRepoFile(InstallerRelativePath);
        var dataDirectoryName = Path.GetFileName(AppPaths.DataDirectory);

        Assert.Contains("[UninstallDelete]", installer);
        Assert.Contains($"#define MyAppDataDir \"{{localappdata}}\\{dataDirectoryName}\"", installer);
        Assert.Contains("Type: filesandordirs; Name: \"{#MyAppDataDir}\"", installer);
    }

    [Fact]
    public void Installer_ClearsTheStoredCredentialsOnUninstall()
    {
        var installer = ReadRepoFile(InstallerRelativePath);

        Assert.Contains("[UninstallRun]", installer);
        Assert.Contains("cmdkey.exe", installer);

        foreach (var (defineName, constantName) in CredentialDefines)
        {
            var target = ConstantValue(constantName);
            Assert.Contains($"#define {defineName} \"{target}\"", installer);
            Assert.Contains($"/delete:{{#{defineName}}}", installer);
        }
    }

    [Fact]
    public void Installer_RemovesTheAutostartValueEvenWhenTheAppWroteIt()
    {
        var installer = ReadRepoFile(InstallerRelativePath);

        Assert.Contains("Flags: dontcreatekey uninsdeletevalue", installer);
    }

    [Fact]
    public void VersionStrings_AgreeAcrossCsproj_Readme_AndCompiledAssembly()
    {
        var csproj = LoadRepoXml(MainCsprojRelativePath);
        var packageVersion = RequiredProperty(csproj, "Version");
        var assemblyVersion = RequiredProperty(csproj, "AssemblyVersion");
        var fileVersion = RequiredProperty(csproj, "FileVersion");

        Assert.Equal(assemblyVersion, fileVersion);
        Assert.Equal($"{packageVersion}.0", assemblyVersion);

        var compiledVersion = typeof(AppPaths).Assembly.GetName().Version;
        Assert.NotNull(compiledVersion);
        Assert.Equal(assemblyVersion, compiledVersion!.ToString());

        Assert.Contains($"**Current version:** v{packageVersion}", ReadRepoFile(ReadmeRelativePath));
    }

    [Fact]
    public void MainProject_PinsTrimmingAndAotOff()
    {
        var csproj = LoadRepoXml(MainCsprojRelativePath);

        Assert.Equal("false", RequiredProperty(csproj, "PublishTrimmed"));
        Assert.Equal("false", RequiredProperty(csproj, "PublishAot"));
    }

    [Fact]
    public void MainProject_FailsThePublishWhenTrimmingIsForcedOnTheCommandLine()
    {
        var csproj = LoadRepoXml(MainCsprojRelativePath);

        var guard = csproj.Descendants("Target")
            .SingleOrDefault(target => target.Attribute("Name")?.Value == "FailOnTrimmedPublish");

        Assert.NotNull(guard);
        Assert.Equal("Publish", guard!.Attribute("BeforeTargets")?.Value);
        Assert.Single(guard.Descendants("Error"));

        var condition = guard.Attribute("Condition")?.Value ?? string.Empty;
        Assert.Contains("PublishTrimmed", condition);
        Assert.Contains("PublishAot", condition);
    }

    [Fact]
    public void BothProjects_RestoreWithALockFile()
    {
        foreach (var project in new[] { MainCsprojRelativePath, TestCsprojRelativePath })
        {
            Assert.Equal("true", RequiredProperty(LoadRepoXml(project), "RestorePackagesWithLockFile"));
        }
    }

    [Fact]
    public void NuGetConfig_PinsNuGetOrgAsTheOnlySource()
    {
        var config = LoadRepoXml(NuGetConfigRelativePath);

        var sources = config.Root?.Element("packageSources");
        Assert.NotNull(sources);
        Assert.Equal("clear", sources!.Elements().First().Name.LocalName);

        var added = sources.Elements("add").ToList();
        Assert.Single(added);
        Assert.Equal(NuGetOrgSourceUrl, added[0].Attribute("value")?.Value);

        var disabled = config.Root?.Element("disabledPackageSources");
        Assert.NotNull(disabled);
        Assert.Single(disabled!.Elements("clear"));
    }

    [Fact]
    public void DeclaredPlatforms_AreBuildableFromTheSolution()
    {
        var solutionPlatforms = SolutionPlatforms();

        foreach (var project in new[] { MainCsprojRelativePath, TestCsprojRelativePath })
        {
            foreach (var platform in DeclaredPlatforms(project))
            {
                Assert.Contains(platform, solutionPlatforms);
            }
        }

        var declaredByBothProjects = DeclaredPlatforms(MainCsprojRelativePath)
            .Intersect(DeclaredPlatforms(TestCsprojRelativePath))
            .ToList();

        foreach (var platform in solutionPlatforms.Where(p => p != SolutionAnyCpuPlatform))
        {
            Assert.Contains(platform, declaredByBothProjects);
        }
    }

    [Fact]
    public void DeclaredPlatforms_HaveMatchingRuntimeIdentifiers()
    {
        foreach (var project in new[] { MainCsprojRelativePath, TestCsprojRelativePath })
        {
            var runtimeIdentifiers = RequiredProperty(LoadRepoXml(project), "RuntimeIdentifiers")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var expected = DeclaredPlatforms(project)
                .Select(platform => $"win-{platform.ToLowerInvariant()}")
                .ToList();

            Assert.Equal(expected, runtimeIdentifiers);
        }
    }

    /// <summary>
    /// Reads a private const from CredentialService so the installer's cmdkey targets stay
    /// tied to the code instead of being a hand-copied duplicate that can rot silently.
    /// </summary>
    private static string ConstantValue(string fieldName)
    {
        var field = typeof(CredentialService)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        return Assert.IsType<string>(field!.GetRawConstantValue());
    }

    private static List<string> DeclaredPlatforms(string projectRelativePath) =>
        RequiredProperty(LoadRepoXml(projectRelativePath), "Platforms")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static List<string> SolutionPlatforms()
    {
        const string sectionStart = "GlobalSection(SolutionConfigurationPlatforms)";
        const string sectionEnd = "EndGlobalSection";

        var lines = ReadRepoFile(SolutionRelativePath).Split('\n').Select(line => line.Trim()).ToList();
        var start = lines.FindIndex(line => line.StartsWith(sectionStart, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{SolutionRelativePath} has no {sectionStart} section.");

        var end = lines.FindIndex(start, line => line == sectionEnd);
        Assert.True(end > start, $"{sectionStart} section is not terminated.");

        return lines.GetRange(start + 1, end - start - 1)
            .Select(line => line.Split('=')[0].Trim())
            .Select(configuration => configuration.Split('|').Last())
            .Distinct()
            .ToList();
    }

    private static string RequiredProperty(XDocument project, string name)
    {
        var values = project.Descendants(name).Select(element => element.Value.Trim()).Distinct().ToList();
        Assert.Single(values);
        return values[0];
    }

    private static XDocument LoadRepoXml(string relativePath) =>
        XDocument.Parse(ReadRepoFile(relativePath));

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    /// <summary>
    /// Walks up from the test output directory to the checkout root, identified by the
    /// installer script (mirrors the locator pattern in ResourceCoverageTests).
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, InstallerRelativePath)))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root ({InstallerRelativePath}) from {AppContext.BaseDirectory}.");
    }
}

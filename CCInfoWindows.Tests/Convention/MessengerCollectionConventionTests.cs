using System.Text.RegularExpressions;

namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Finding 33, repo-wide: <c>WeakReferenceMessenger.Default</c> is process-global and xUnit runs
/// collections in parallel, so a Send from one collection is delivered to live recipients under test in
/// another. The "WeakReferenceMessenger" collection is the only thing that serialises those classes —
/// ClaudeApiServiceTests' 401 case sends <c>AuthStateChangedMessage(false)</c>, which outside the
/// collection drives navigation on ViewModels another test is asserting about, and the symptom surfaces
/// as a flaky <c>Times.Once</c> in a file that never mentions the messenger.
///
/// Enforced as a source scan because collection membership is an attribute on the test class, invisible
/// to anything the tests themselves can observe at runtime.
///
/// Known gap, deliberately not enforced here: a test can touch the global messenger transitively, by
/// constructing production code that Sends (SettingsViewModel.Logout, MainViewModel's chart
/// invalidation). No text scan of the test sources can see that, and widening this scan to every test
/// file that names such a type would flag files whose messenger traffic has no recipient at all.
/// </summary>
public class MessengerCollectionConventionTests
{
    private const string MessengerApi = "WeakReferenceMessenger.Default";
    private const string CollectionName = "WeakReferenceMessenger";
    private const string JoinsTheCollection = "[Collection(\"" + CollectionName + "\")]";
    private const string DefinesTheCollection = "[CollectionDefinition(\"" + CollectionName + "\")]";

    /// <summary>Sits in the test project root; identifies it while walking up from the output directory.</summary>
    private const string TestProjectAnchorFileName = "MessengerTestCollection.cs";

    /// <summary>
    /// This file, and only this file. It spells the hunted API out in a constant and in the stripper's
    /// fixture while neither registering nor sending anything, so it would otherwise report itself.
    /// </summary>
    private const string ScannerFileName = "MessengerCollectionConventionTests.cs";

    /// <summary>Lower bound that makes a broken locator fail loudly instead of scanning nothing.</summary>
    private const int MinimumExpectedFileCount = 20;

    private static readonly Regex BlockCommentPattern = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Whole-line comments only, including XML doc comments. A trailing comment after code is left
    /// alone on purpose: cutting at the first <c>//</c> would also cut a <c>//</c> inside a string
    /// literal and silently delete the call after it, turning a missing guard into a passing scan.
    /// The cost is that prose in a trailing comment can raise a false positive, which is the harmless
    /// direction.
    /// </summary>
    private static readonly Regex WholeLineCommentPattern =
        new(@"^[ \t]*//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyList<TestSourceFile>> LazyTestSources = new(ReadTestSources);

    private readonly record struct TestSourceFile(string Name, string Text);

    [Fact]
    public void EveryTestFileTouchingTheGlobalMessenger_IsPartOfTheSerialisedCollection()
    {
        var messengerUsers = LazyTestSources.Value
            .Where(file => !string.Equals(file.Name, ScannerFileName, StringComparison.OrdinalIgnoreCase))
            .Where(file => WithoutComments(file.Text).Contains(MessengerApi, StringComparison.Ordinal))
            .ToList();

        // Vacuity guard: the scan must see the files that do use the messenger, or a broken locator and a
        // clean repo are indistinguishable.
        Assert.NotEmpty(messengerUsers);

        var offenders = messengerUsers
            .Where(file => !IsPartOfTheCollection(file.Text))
            .Select(file => file.Name)
            .Order()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These test files touch {MessengerApi} but do not carry {JoinsTheCollection}, so xUnit may run "
            + $"them in parallel with the ViewModels under test: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheCommentStripper_DropsProseAndKeepsCode()
    {
        const string source = """
            /// A doc comment naming WeakReferenceMessenger.Default in prose.
            /* A block comment naming WeakReferenceMessenger.Default too. */
            var messenger = WeakReferenceMessenger.Default;
            """;

        var survivingLines = WithoutComments(source)
            .Split('\n')
            .Where(line => line.Contains(MessengerApi, StringComparison.Ordinal))
            .ToList();

        Assert.Contains("var messenger", Assert.Single(survivingLines));
    }

    private static bool IsPartOfTheCollection(string source) =>
        source.Contains(JoinsTheCollection, StringComparison.Ordinal)
        || source.Contains(DefinesTheCollection, StringComparison.Ordinal);

    private static string WithoutComments(string source) =>
        WholeLineCommentPattern.Replace(BlockCommentPattern.Replace(source, string.Empty), string.Empty);

    private static IReadOnlyList<TestSourceFile> ReadTestSources()
    {
        var root = LocateTestSourceRoot();

        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => new TestSourceFile(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();

        Assert.True(
            files.Count >= MinimumExpectedFileCount,
            $"Only {files.Count} test sources found under {root} -- the locator is broken.");

        return files;
    }

    // obj\ and bin\ hold source-generator output and copies of the very files being scanned.
    private static bool IsBuildOutput(string path) =>
        path.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Walks up from the test output directory to the test project's source root — the compiled assembly
    /// carries no source, and ProductionSourceFiles deliberately scans the app project instead.
    /// </summary>
    private static string LocateTestSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "CCInfoWindows.Tests");
            if (File.Exists(Path.Combine(candidate, TestProjectAnchorFileName)))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the test source directory ({TestProjectAnchorFileName}) from {AppContext.BaseDirectory}.");
    }
}

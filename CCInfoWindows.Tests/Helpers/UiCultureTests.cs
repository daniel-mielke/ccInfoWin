using System.Globalization;
using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// The display-language-to-UI-culture rule, asserted once now that App's startup path and the Settings
/// dropdown share it instead of each carrying its own copy of the same three lines.
///
/// The assignment itself is injected. CultureInfo.DefaultThreadCurrentUICulture and CurrentUICulture are
/// process-wide, xUnit runs test classes in parallel, and CountdownFormatterTests reads
/// CultureInfo.CurrentUICulture — a test that really moved them would be exactly the machine-state
/// mutation finding 33 is about. So the selection rule is asserted against a captured culture, and the
/// two lines that write the globals are pinned by the source scans below — which are also the only guard
/// that catches a third hand-rolled copy appearing next year, or the formatting culture being quietly
/// dragged along with the display language.
/// </summary>
public class UiCultureTests
{
    private const string LogSource = nameof(UiCultureTests);
    private const string UiCultureFileName = "UiCulture.cs";

    /// <summary>
    /// Spaces are not legal in a culture name, so this reaches CultureNotFoundException. A well-formed
    /// but unassigned tag would not: on ICU, .NET synthesises a culture for anything shaped like BCP-47.
    /// </summary>
    private const string MalformedCultureName = "not a culture name";

    /// <summary>
    /// The two statics the production assignment writes. Spelled without the assigned expression so
    /// renaming a local cannot fail the guard, and prefixed so the second cannot match the first.
    /// </summary>
    private const string DefaultThreadUiCultureAssignment = "DefaultThreadCurrentUICulture = ";
    private const string CurrentUiCultureAssignment = "CultureInfo.CurrentUICulture = ";

    /// <summary>
    /// Assignment to either UI-culture static, in any spelling. The trailing space keeps a comparison
    /// (<c>== </c>) from reading as an assignment, and the "UI" keeps it from matching the formatting
    /// culture below.
    /// </summary>
    private const string UiCultureAssignment = "CurrentUICulture = ";

    /// <summary>
    /// Assignment to the number/date FORMATTING culture — CurrentCulture or DefaultThreadCurrentCulture.
    /// Regional formatting is a separate Windows setting from the display language, and every numeric
    /// formatter in the app is InvariantCulture-pinned, so following the language would override the
    /// user's own choice and change nothing else. Reads (string.Format(CultureInfo.CurrentCulture, …),
    /// StringComparer.CurrentCulture) are untouched by this needle.
    /// </summary>
    private const string FormattingCultureAssignment = "CurrentCulture = ";

    [Fact]
    public void Apply_HandsTheResolvedCultureToTheAssignment()
    {
        var applied = Capture("de-DE");

        Assert.Equal("de-DE", applied?.Name);

        // The name alone is not the point: a resw pattern is rendered with THIS culture's day and month
        // names, so the resolved culture has to be one that actually speaks the language.
        Assert.Equal("Februar", applied?.DateTimeFormat.GetMonthName(2));
    }

    [Fact]
    public void Apply_ResolvesTheOtherShippedLanguageToo()
    {
        var applied = Capture("en-US");

        Assert.Equal("en-US", applied?.Name);
        Assert.Equal("February", applied?.DateTimeFormat.GetMonthName(2));
    }

    [Fact]
    public void Apply_LeavesTheCultureAlone_WhenTheNameIsNotACultureName()
    {
        // settings.json is user-writable, so the language string is untrusted input, and an escaping
        // throw here would abort a startup path that has already applied the language successfully.
        //
        // The input is malformed rather than merely unknown on purpose: with ICU, .NET accepts any
        // well-formed BCP-47 tag and synthesises a culture for it, so "zz-ZZ" would NOT reach the catch.
        Assert.Null(Capture(MalformedCultureName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_LeavesTheCultureAlone_WhenTheLanguageIsBlank(string language)
    {
        // GetCultureInfo("") answers InvariantCulture instead of throwing. Assigning it would render
        // invariant English month names into a German pattern and look like a translation bug, so a blank
        // code — which can only be a caller defect — must not reach the assignment.
        Assert.Null(Capture(language));
    }

    [Fact]
    public void Apply_WithoutTheInjectedAssignment_LeavesTheRealUiCultureUntouchedForARejectedName()
    {
        // The production overload, reached exactly the way the app reaches it. Only the rejecting paths
        // are exercised — the assigning one writes process-wide statics — and those are the paths where
        // "leave the culture alone" is the entire contract, so the real globals can be asserted here.
        var before = CultureInfo.CurrentUICulture.Name;

        UiCulture.Apply(MalformedCultureName, LogSource);
        UiCulture.Apply(string.Empty, LogSource);

        Assert.Equal(before, CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void UiCulture_IsTheOnlyProductionFileThatMovesTheUiCulture()
    {
        // Finding 30's cross-file half: App.ApplyUiCulture and SettingsViewModel.ApplyUiCulture were
        // byte-identical, and the second was added months after the first without anyone noticing the
        // first existed. A third copy fails here.
        var offenders = ProductionSourceFiles.FilesContaining(UiCultureAssignment, UiCultureFileName).ToList();

        Assert.True(
            offenders.Count == 0,
            "These files assign the UI culture themselves instead of calling UiCulture.Apply: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void NoProductionFile_MovesTheNumberAndDateFormattingCulture()
    {
        // The decision, enforced instead of only documented: a display-language choice must not silently
        // re-format the user's numbers and dates. UiCulture itself is not exempt — it is the file where
        // adding the third line would feel most natural.
        var offenders = ProductionSourceFiles.FilesContaining(FormattingCultureAssignment).ToList();

        Assert.True(
            offenders.Count == 0,
            "Regional formatting is an OS setting, not a display-language consequence. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void UiCulture_StillAssignsBothUiCultureStatics()
    {
        // Guards the "only file" scan against passing vacuously: with the assignment gone or renamed, its
        // needle would match nothing and an app that no longer aligns the culture at all would look
        // compliant. DefaultThreadCurrentUICulture covers threads created later (the poll timer's
        // continuations); CurrentUICulture covers the one already running.
        var uiCulture = ProductionSourceFiles.Read(UiCultureFileName);

        Assert.Contains(DefaultThreadUiCultureAssignment, uiCulture);
        Assert.Contains(CurrentUiCultureAssignment, uiCulture);
    }

    /// <summary>
    /// Runs the real rule with the assignment replaced by a capture, so nothing outlives the test.
    /// Returns null when the rule declined to assign anything.
    /// </summary>
    private static CultureInfo? Capture(string language)
    {
        CultureInfo? applied = null;

        UiCulture.Apply(language, LogSource, culture => applied = culture);

        return applied;
    }
}

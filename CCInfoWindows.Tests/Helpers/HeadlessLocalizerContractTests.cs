using WinUI3Localizer;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Pins the one platform fact every headless assertion about a resw key rests on: xUnit cannot start a
/// WinUI3Localizer host, so <c>Localizer.Get()</c> keeps handing back the library's NullLocalizer,
/// whose GetLocalizedString returns the uid it was asked for.
///
/// SettingsViewModelTimerTests uses that to assert which of the five LastFetch* keys
/// LastFetchRelativeTime reached for, and SettingsViewModelTests does the same for
/// RefreshIntervalManual. Should a package update make the unbuilt localizer answer with the empty
/// string instead, those assertions would all stop discriminating at once — this test names the cause
/// instead of leaving the others to fail mysteriously.
/// </summary>
public class HeadlessLocalizerContractTests
{
    private const string SentinelUid = "UidThatExistsInNoResourceFile";

    [Fact]
    public void WithoutAHost_TheLocalizerEchoesTheUid()
    {
        Assert.Equal(SentinelUid, Localizer.Get().GetLocalizedString(SentinelUid));
    }
}

using System.Reflection;
using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Unit tests for SettingsViewModel timer lifecycle: StartAboutTimestampTimer /
/// StopAboutTimestampTimer idempotency, plus the band the About tab's LastFetchRelativeTime picks.
/// Tick-driven PropertyChanged is not unit-tested headlessly via a real DispatcherTimer
/// (WinRT COM context required); FakeDispatcherTimer covers that path instead.
/// See VALIDATION §Manual-Only for runtime smoke test.
///
/// Finding 32 item 3: the three LastFetchRelativeTime tests here asserted Assert.NotNull on a
/// non-nullable string, so they only ever failed if the getter threw — the Never / JustNow /
/// MinutesAgo / HoursAgo / DaysAgo selection and its 30 s / 60 min / 24 h edges were unasserted, and
/// the three cases differed only in a mock the assertion could not observe. They now assert which
/// resw key the getter reached for, which headlessly is the key itself
/// (see <see cref="HeadlessLocalizerContractTests"/>).
/// </summary>
public class SettingsViewModelTimerTests
{
    private const string NeverUid = "LastFetchNever";
    private const string JustNowUid = "LastFetchJustNow";
    private const string MinutesAgoUid = "LastFetchMinutesAgo";
    private const string HoursAgoUid = "LastFetchHoursAgo";
    private const string DaysAgoUid = "LastFetchDaysAgo";

    private const int OneMinuteInSeconds = 60;
    private const int OneHourInSeconds = 60 * OneMinuteInSeconds;
    private const int OneDayInSeconds = 24 * OneHourInSeconds;

    /// <summary>Elapsed time at which the getter leaves "just now" for a counted minute.</summary>
    private const int JustNowBandEndsAtSeconds = 30;

    private const int SeveralDaysInSeconds = 9 * OneDayInSeconds;

    private static FieldInfo TimerField => typeof(SettingsViewModel)
        .GetField("_aboutTimestampTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static object? GetTimer(SettingsViewModel sut) => TimerField.GetValue(sut);

    private static SettingsViewModel CreateSut(Mock<IPricingService>? pricingMock = null)
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var credentialService = new Mock<ICredentialService>();
        credentialService.Setup(s => s.HasValidToken()).Returns(true);

        var navigationService = new Mock<INavigationService>();

        var pricing = pricingMock ?? new Mock<IPricingService>();
        if (pricingMock == null)
        {
            pricing.Setup(s => s.Source).Returns(PricingSource.Unknown);
            pricing.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);
        }

        var historyService = new Mock<IUsageHistoryService>();

        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetKnownSessionIds()).Returns(Array.Empty<string>());
        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns(Array.Empty<SessionInfo>());
        var dispatcherQueue = new Mock<IDispatcherQueue>();

        var apiService = new Mock<IClaudeApiService>();
        var usageNotifications = new Mock<IUsageNotificationService>();
        var bridge = new Mock<IWebViewBridge>();

        var sut = new SettingsViewModel(
            settingsService.Object,
            credentialService.Object,
            navigationService.Object,
            pricing.Object,
            historyService.Object,
            sessionNameStore.Object,
            jsonlService.Object,
            dispatcherQueue.Object,
            apiService.Object,   // ORGID-01
            usageNotifications.Object,
            bridge.Object);      // finding 18: logout unbinds the API bridge

        // Inject fake timer factory to avoid WinRT COM context requirement in tests.
        sut.TimerFactory = () => new FakeDispatcherTimer();

        return sut;
    }

    [Fact]
    public void AboutTimestampTimer_StartStopLifecycle()
    {
        var sut = CreateSut();

        Assert.Null(GetTimer(sut));

        sut.StartAboutTimestampTimer();
        Assert.NotNull(GetTimer(sut));

        sut.StopAboutTimestampTimer();
        Assert.Null(GetTimer(sut));

        sut.StartAboutTimestampTimer();
        Assert.NotNull(GetTimer(sut));

        sut.StopAboutTimestampTimer();
        Assert.Null(GetTimer(sut));

        sut.StopAboutTimestampTimer(); // double-stop no-op
        Assert.Null(GetTimer(sut));
    }

    [Fact]
    public void AboutTimestampTimer_StartTwice_IsIdempotent()
    {
        var sut = CreateSut();

        sut.StartAboutTimestampTimer();
        var first = GetTimer(sut);

        sut.StartAboutTimestampTimer();
        var second = GetTimer(sut);

        Assert.Same(first, second);

        sut.StopAboutTimestampTimer();
    }

    /// <summary>
    /// A pricing double whose LastFetch is re-derived on every read, so the span the getter computes is
    /// <paramref name="secondsAgo"/> plus only the microseconds between its two statements. A sample
    /// sitting exactly on a band edge therefore always lands in the upper band, however the test host is
    /// scheduled — the edges are asserted from above, and the in-band samples keep a minute of slack.
    /// </summary>
    private static Mock<IPricingService> PricingFetchedSecondsAgo(int secondsAgo)
    {
        var pricing = new Mock<IPricingService>();
        pricing.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricing.SetupGet(s => s.LastFetch).Returns(() => DateTimeOffset.Now.AddSeconds(-secondsAgo));
        return pricing;
    }

    [Fact]
    public void LastFetchRelativeTime_WithoutAnyFetchYet_ReadsNever()
    {
        var pricingMock = new Mock<IPricingService>();
        pricingMock.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingMock.SetupGet(x => x.LastFetch).Returns((DateTimeOffset?)null);

        var sut = CreateSut(pricingMock: pricingMock);

        Assert.Equal(NeverUid, sut.LastFetchRelativeTime);
    }

    [Theory]
    [InlineData(5, JustNowUid)]
    [InlineData(JustNowBandEndsAtSeconds, MinutesAgoUid)]
    [InlineData(OneHourInSeconds - OneMinuteInSeconds, MinutesAgoUid)]
    [InlineData(OneHourInSeconds, HoursAgoUid)]
    [InlineData(OneDayInSeconds - OneHourInSeconds, HoursAgoUid)]
    [InlineData(OneDayInSeconds, DaysAgoUid)]
    [InlineData(SeveralDaysInSeconds, DaysAgoUid)]
    public void LastFetchRelativeTime_ReadsTheKeyOfTheBandTheElapsedTimeFallsIn(
        int elapsedSeconds,
        string expectedUid)
    {
        var sut = CreateSut(PricingFetchedSecondsAgo(elapsedSeconds));

        Assert.Equal(expectedUid, sut.LastFetchRelativeTime);
    }

    [Fact]
    public void LastFetchRelativeTime_WithATimestampInTheFuture_StaysOnJustNow()
    {
        // A clock correction between the fetch and the render makes the span negative. "Just now" is
        // where that belongs; the counted bands would have to render a negative number.
        var sut = CreateSut(PricingFetchedSecondsAgo(-OneHourInSeconds));

        Assert.Equal(JustNowUid, sut.LastFetchRelativeTime);
    }

    [Fact]
    public void StopAboutTimestampTimer_NullifiesField()
    {
        var sut = CreateSut();

        sut.StartAboutTimestampTimer();
        sut.StopAboutTimestampTimer();

        Assert.Null(GetTimer(sut));
    }
}

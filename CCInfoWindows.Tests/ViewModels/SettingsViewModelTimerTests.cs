using System.Reflection;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Fake IDispatcherTimer for headless unit testing — avoids WinRT COM context requirement.
/// Exposes RaiseTick() to simulate the timer firing in tests.
/// </summary>
internal sealed class FakeDispatcherTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsEnabled { get; private set; }

    private event EventHandler<object>? _tick;

    public event EventHandler<object>? Tick
    {
        add => _tick += value;
        remove => _tick -= value;
    }

    public void Start() => IsEnabled = true;
    public void Stop() => IsEnabled = false;

    public void RaiseTick() => _tick?.Invoke(this, new object());
}

/// <summary>
/// Unit tests for SettingsViewModel timer lifecycle: StartAboutTimestampTimer /
/// StopAboutTimestampTimer idempotency and LastFetchRelativeTime formatting.
/// Tick-driven PropertyChanged is not unit-tested headlessly via a real DispatcherTimer
/// (WinRT COM context required); FakeDispatcherTimer covers that path instead.
/// See VALIDATION §Manual-Only for runtime smoke test.
/// </summary>
public class SettingsViewModelTimerTests
{
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

        var sut = new SettingsViewModel(
            settingsService.Object,
            credentialService.Object,
            navigationService.Object,
            pricing.Object);

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

    [Fact]
    public void LastFetchRelativeTime_NullTimestamp_ReturnsNeverFallback()
    {
        var pricingMock = new Mock<IPricingService>();
        pricingMock.SetupGet(x => x.LastFetch).Returns((DateTimeOffset?)null);
        pricingMock.Setup(s => s.Source).Returns(PricingSource.Unknown);

        var sut = CreateSut(pricingMock: pricingMock);

        Assert.Equal("Never", sut.LastFetchRelativeTime);
    }

    [Fact]
    public void LastFetchRelativeTime_FiveMinutesAgo_ReturnsMinutesAgoString()
    {
        var pricingMock = new Mock<IPricingService>();
        pricingMock.SetupGet(x => x.LastFetch).Returns(DateTimeOffset.Now.AddMinutes(-5));
        pricingMock.Setup(s => s.Source).Returns(PricingSource.Unknown);

        var sut = CreateSut(pricingMock: pricingMock);
        var result = sut.LastFetchRelativeTime;

        Assert.Contains("5", result);
        Assert.Contains("minute", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ago", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LastFetchRelativeTime_OneMinuteAgo_ReturnsSingularForm()
    {
        var pricingMock = new Mock<IPricingService>();
        pricingMock.SetupGet(x => x.LastFetch).Returns(DateTimeOffset.Now.AddMinutes(-1));
        pricingMock.Setup(s => s.Source).Returns(PricingSource.Unknown);

        var sut = CreateSut(pricingMock: pricingMock);

        Assert.Equal("1 minute ago", sut.LastFetchRelativeTime);
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

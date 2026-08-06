using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// STATISTIKEN panel formatting, asserted on the real <see cref="MainViewModel.ApplyStatistics"/>.
///
/// Finding 31: this file used to drive a MainViewModelTestHarness whose constructor discarded both
/// injected services and whose ApplyStatistics was a second implementation of the rules under test.
/// That copy had already drifted — it knew nothing about the synthetic/unknown filter, the Distinct
/// or the ordering — and dropping the "> 0" token guard in production left all five tests green.
/// The real ViewModel is built headlessly exactly as MainViewModelAuthFlowTests and
/// MainViewModelRefreshTests build it: FakeDispatcherQueue plus the brushFactory seam.
/// </summary>
public class MainViewModelStatisticsTests
{
    /// <summary>The en dash the panel shows for a field with nothing to report.</summary>
    private const string MissingValuePlaceholder = "\u2013";

    private const string SonnetId = "claude-sonnet-4-5";
    private const string SonnetDisplayName = "Sonnet 4.5";
    private const string HaikuId = "claude-haiku-4-5";
    private const string HaikuDisplayName = "Haiku 4.5";
    private const string OpusId = "claude-opus-5";
    private const string OpusIdWithDateSuffix = "claude-opus-5-20260101";
    private const string OpusDisplayName = "Opus 5";

    private static MainViewModel CreateSut()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var jsonlService = new Mock<IJsonlService>();
        jsonlService.Setup(s => s.Sessions).Returns([]);
        jsonlService.Setup(s => s.IsScanning).Returns(false);
        jsonlService.Setup(s => s.GetStatistics(It.IsAny<TimePeriod>(), It.IsAny<string?>()))
            .Returns(StatisticsSummary.Empty);

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);
        pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);

        var sessionNameStore = new Mock<ISessionNameStore>();
        sessionNameStore.Setup(s => s.GetCustomName(It.IsAny<string>())).Returns((string?)null);

        return new MainViewModel(
            new Mock<ICredentialService>().Object,
            new Mock<INavigationService>().Object,
            new Mock<IClaudeApiService>().Object,
            settingsService.Object,
            new Mock<IUsageHistoryService>().Object,
            jsonlService.Object,
            pricingService.Object,
            new Mock<IUpdateService>().Object,
            new Mock<IUsageNotificationService>().Object,
            new FakeDispatcherQueue(),
            sessionNameStore.Object,
            _ => null!);   // headless brushFactory seam — SolidColorBrush requires WinRT COM
    }

    private static MainViewModel WithStatistics(StatisticsSummary stats)
    {
        var sut = CreateSut();
        sut.ApplyStatistics(stats);
        return sut;
    }

    private static MainViewModel WithModels(params string[] models)
        => WithStatistics(new StatisticsSummary { Models = models });

    [Fact]
    public void ApplyStatistics_WithEstimatedCosts_PrefixesTheCostWithATilde()
    {
        var sut = WithStatistics(new StatisticsSummary { TotalCostUsd = 5.00m, HasEstimatedCosts = true });

        Assert.Equal("~$5.00", sut.StatisticsCost);
    }

    [Fact]
    public void ApplyStatistics_WithExactCosts_LeavesTheCostUnprefixed()
    {
        var sut = WithStatistics(new StatisticsSummary { TotalCostUsd = 3.50m, HasEstimatedCosts = false });

        Assert.Equal("$3.50", sut.StatisticsCost);
    }

    [Fact]
    public void ApplyStatistics_WithNoModels_ShowsThePlaceholder()
    {
        var sut = WithStatistics(StatisticsSummary.Empty);

        Assert.Equal(MissingValuePlaceholder, sut.StatisticsModels);
    }

    [Fact]
    public void ApplyStatistics_WithOneModel_ShowsItsDisplayName()
    {
        var sut = WithModels(SonnetId);

        Assert.Equal(SonnetDisplayName, sut.StatisticsModels);
    }

    [Fact]
    public void ApplyStatistics_WithZeroTokens_ShowsThePlaceholderInEveryTokenField()
    {
        var sut = WithStatistics(StatisticsSummary.Empty);

        // The guard under test is "> 0": without it TokenFormatter renders a literal "0" into all five
        // fields, which reads as a measured zero rather than "nothing recorded for this period".
        Assert.Equal(MissingValuePlaceholder, sut.StatisticsInput);
        Assert.Equal(MissingValuePlaceholder, sut.StatisticsOutput);
        Assert.Equal(MissingValuePlaceholder, sut.StatisticsCacheCreation);
        Assert.Equal(MissingValuePlaceholder, sut.StatisticsCacheRead);
        Assert.Equal(MissingValuePlaceholder, sut.StatisticsTotal);

        // Cost is deliberately exempt: "$0.00" is a real answer for a period that ran free models.
        Assert.Equal("$0.00", sut.StatisticsCost);
    }

    [Fact]
    public void ApplyStatistics_WithNonZeroTokens_FillsEachFieldFromItsOwnCounter()
    {
        // Four distinct magnitudes, so a field wired to the wrong counter cannot pass.
        var sut = WithStatistics(new StatisticsSummary
        {
            InputTokens = 1_500,
            OutputTokens = 2_000,
            CacheCreationTokens = 3_000,
            CacheReadTokens = 4_000
        });

        Assert.Equal("1.5K", sut.StatisticsInput);
        Assert.Equal("2.0K", sut.StatisticsOutput);
        Assert.Equal("3.0K", sut.StatisticsCacheCreation);
        Assert.Equal("4.0K", sut.StatisticsCacheRead);
        Assert.Equal("10.5K", sut.StatisticsTotal);
    }

    [Fact]
    public void ApplyStatistics_DropsTheSyntheticAndUnknownModelIds()
    {
        // The transcript carries these ids for turns no model served (JsonlService.IsSyntheticModel
        // knows the same two spellings). GetDisplayName passes them through verbatim, so an unfiltered
        // row read "<synthetic>, Sonnet 4.5". Matching is case-insensitive.
        var sut = WithModels("<SYNTHETIC>", "Synthetic", "UNKNOWN", SonnetId);

        Assert.Equal(SonnetDisplayName, sut.StatisticsModels);
    }

    [Fact]
    public void ApplyStatistics_WithNothingButSyntheticIds_ShowsThePlaceholder()
    {
        var sut = WithModels("<synthetic>", "unknown");

        Assert.Equal(MissingValuePlaceholder, sut.StatisticsModels);
    }

    [Fact]
    public void ApplyStatistics_CollapsesModelIdsThatShareADisplayName()
    {
        // A dated id and its undated twin both render "Opus 5" — the row used to read "Opus 5, Opus 5".
        var sut = WithModels(OpusIdWithDateSuffix, OpusId);

        Assert.Equal(OpusDisplayName, sut.StatisticsModels);
    }

    [Fact]
    public void ApplyStatistics_SortsTheModelNames()
    {
        // GetStatistics returns a HashSet.ToList(), whose order is not stable between polls; without
        // the sort the row reshuffled on screen for no reason. Input order is reversed on purpose.
        var sut = WithModels(SonnetId, OpusId, HaikuId);

        Assert.Equal($"{HaikuDisplayName}, {OpusDisplayName}, {SonnetDisplayName}", sut.StatisticsModels);
    }
}

using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using Moq;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Unit tests for SettingsViewModel: tab switching, short labels, version text, token status.
/// </summary>
public class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(bool hasValidToken = true)
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var credentialService = new Mock<ICredentialService>();
        credentialService.Setup(s => s.HasValidToken()).Returns(hasValidToken);

        var navigationService = new Mock<INavigationService>();

        var pricingService = new Mock<IPricingService>();
        pricingService.Setup(s => s.Source).Returns(PricingSource.Unknown);
        pricingService.Setup(s => s.LastFetch).Returns((DateTimeOffset?)null);

        return new SettingsViewModel(
            settingsService.Object,
            credentialService.Object,
            navigationService.Object,
            pricingService.Object);
    }

    [Fact]
    public void TabSwitching_DefaultIndex_GeneralTabVisible()
    {
        var vm = CreateViewModel();

        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.True(vm.IsGeneralTabVisible);
        Assert.False(vm.IsUpdatesTabVisible);
        Assert.False(vm.IsAccountTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    [Fact]
    public void TabSwitching_SetIndex1_UpdatesTabVisible()
    {
        var vm = CreateViewModel();

        vm.SelectedTabIndex = 1;

        Assert.False(vm.IsGeneralTabVisible);
        Assert.True(vm.IsUpdatesTabVisible);
        Assert.False(vm.IsAccountTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    [Fact]
    public void TabSwitching_SetIndex2_AccountTabVisible()
    {
        var vm = CreateViewModel();

        vm.SelectedTabIndex = 2;

        Assert.False(vm.IsGeneralTabVisible);
        Assert.False(vm.IsUpdatesTabVisible);
        Assert.True(vm.IsAccountTabVisible);
        Assert.False(vm.IsAboutTabVisible);
    }

    [Fact]
    public void TabSwitching_SetIndex3_AboutTabVisible()
    {
        var vm = CreateViewModel();

        vm.SelectedTabIndex = 3;

        Assert.False(vm.IsGeneralTabVisible);
        Assert.False(vm.IsUpdatesTabVisible);
        Assert.False(vm.IsAccountTabVisible);
        Assert.True(vm.IsAboutTabVisible);
    }

    [Fact]
    public void RefreshOptions_UseShortNotation()
    {
        var vm = CreateViewModel();

        Assert.Equal("30s", vm.RefreshOptions[0].Label);
        Assert.Equal("1min", vm.RefreshOptions[1].Label);
        Assert.Equal("2min", vm.RefreshOptions[2].Label);
        Assert.Equal("5min", vm.RefreshOptions[3].Label);
        Assert.Equal("10min", vm.RefreshOptions[4].Label);
        Assert.Equal("Manuell", vm.RefreshOptions[5].Label);
    }

    [Fact]
    public void RefreshOptions_SecondsValuesUnchanged()
    {
        var vm = CreateViewModel();

        Assert.Equal(30, vm.RefreshOptions[0].Seconds);
        Assert.Equal(60, vm.RefreshOptions[1].Seconds);
        Assert.Equal(120, vm.RefreshOptions[2].Seconds);
        Assert.Equal(300, vm.RefreshOptions[3].Seconds);
        Assert.Equal(600, vm.RefreshOptions[4].Seconds);
        Assert.Equal(0, vm.RefreshOptions[5].Seconds);
    }

    [Fact]
    public void AppVersionText_ReturnsNonEmptyVersion()
    {
        var vm = CreateViewModel();

        Assert.NotEmpty(vm.AppVersionText);
        Assert.Contains(".", vm.AppVersionText);
    }

    [Fact]
    public void IsTokenValid_WhenHasToken_ReturnsTrue()
    {
        var vm = CreateViewModel(hasValidToken: true);

        Assert.True(vm.IsTokenValid);
    }

    [Fact]
    public void IsTokenValid_WhenNoToken_ReturnsFalse()
    {
        var vm = CreateViewModel(hasValidToken: false);

        Assert.False(vm.IsTokenValid);
    }
}

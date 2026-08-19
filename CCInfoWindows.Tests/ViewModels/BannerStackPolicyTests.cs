using CCInfoWindows.ViewModels;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// PRICING-03 / D-PR-05: banner-stack policy verification — the
/// (IsPricingError x IsSessionExpired) matrix on <see cref="MainViewModel.IsPricingErrorVisible"/>.
///
/// The 4-cell truth table used to be asserted against a private copy of the formula in this class,
/// which left the production property with zero coverage: dropping <c>&amp;&amp; !IsSessionExpired</c>
/// kept the whole suite green while the pricing InfoBar rendered on top of the session-expired one.
/// It now drives the real ViewModel, built headlessly like every other MainViewModel suite.
///
/// Rationale: ResourceCoverageTests covers resw-key correctness; this class covers the
/// banner-priority logic that suppresses pricing while auth is showing.
/// </summary>
public class BannerStackPolicyTests
{
    private static bool IsPricingErrorVisible(bool isPricingError, bool isSessionExpired)
    {
        var sut = MainViewModelFactory.Create();
        sut.IsPricingError = isPricingError;
        sut.IsSessionExpired = isSessionExpired;

        return sut.IsPricingErrorVisible;
    }

    [Theory]
    [InlineData(false, false, false)]   // Neither — pricing banner hidden
    [InlineData(true,  false, true)]    // Only pricing error — banner visible
    [InlineData(false, true,  false)]   // Only session expired — pricing banner hidden (auth shows alone)
    [InlineData(true,  true,  false)]   // Both — auth wins, pricing suppressed (banner-stack policy)
    public void IsPricingErrorVisible_FollowsBannerStackPolicy(
        bool isPricingError, bool isSessionExpired, bool expected)
    {
        // ARRANGE / ACT
        var actual = IsPricingErrorVisible(isPricingError, isSessionExpired);

        // ASSERT
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BannerStackPolicy_AuthAlwaysWinsOverPricing()
    {
        // The two-banner cap policy: when auth banner is showing, pricing must be suppressed
        // regardless of pricing-error state.
        Assert.False(IsPricingErrorVisible(isPricingError: true,  isSessionExpired: true));
        Assert.False(IsPricingErrorVisible(isPricingError: false, isSessionExpired: true));
    }
}

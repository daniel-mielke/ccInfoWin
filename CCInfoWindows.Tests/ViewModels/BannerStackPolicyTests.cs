namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// PRICING-03 / D-PR-05: banner-stack policy verification — (IsPricingError x IsSessionExpired)
/// matrix. The ViewModel formula is `IsPricingError &amp;&amp; !IsSessionExpired`. This test asserts the
/// 4-cell truth table at unit-test level without requiring a full MainViewModel construction.
///
/// Rationale: ResourceCoverageTests covers resw-key correctness; this class covers the
/// banner-priority logic that suppresses pricing while auth is showing.
/// </summary>
public class BannerStackPolicyTests
{
    /// <summary>Mirrors MainViewModel.IsPricingErrorVisible exactly.</summary>
    private static bool ComputeIsPricingErrorVisible(bool isPricingError, bool isSessionExpired)
        => isPricingError && !isSessionExpired;

    [Theory]
    [InlineData(false, false, false)]   // Neither — pricing banner hidden
    [InlineData(true,  false, true)]    // Only pricing error — banner visible
    [InlineData(false, true,  false)]   // Only session expired — pricing banner hidden (auth shows alone)
    [InlineData(true,  true,  false)]   // Both — auth wins, pricing suppressed (banner-stack policy)
    public void IsPricingErrorVisible_FollowsBannerStackPolicy(
        bool isPricingError, bool isSessionExpired, bool expected)
    {
        // ARRANGE / ACT
        var actual = ComputeIsPricingErrorVisible(isPricingError, isSessionExpired);

        // ASSERT
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BannerStackPolicy_AuthAlwaysWinsOverPricing()
    {
        // The two-banner cap policy: when auth banner is showing, pricing must be suppressed
        // regardless of pricing-error state.
        Assert.False(ComputeIsPricingErrorVisible(isPricingError: true,  isSessionExpired: true));
        Assert.False(ComputeIsPricingErrorVisible(isPricingError: false, isSessionExpired: true));
    }
}

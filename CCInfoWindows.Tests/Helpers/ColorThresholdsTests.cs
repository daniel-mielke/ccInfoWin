using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

public class ColorThresholdsTests
{
    [Theory]
    [InlineData(0.00, "ProgressGreenBrush")]
    [InlineData(0.49, "ProgressGreenBrush")]
    [InlineData(0.50, "ProgressYellowBrush")]
    [InlineData(0.74, "ProgressYellowBrush")]
    [InlineData(0.75, "ProgressOrangeBrush")]
    [InlineData(0.89, "ProgressOrangeBrush")]
    [InlineData(0.90, "ProgressRedBrush")]
    [InlineData(1.00, "ProgressRedBrush")]
    [InlineData(1.50, "ProgressRedBrush")]
    public void GetThresholdKey_ReturnsCorrectBrush(double utilization, string expectedKey)
    {
        var result = ColorThresholds.GetThresholdKey(utilization);
        Assert.Equal(expectedKey, result);
    }
}

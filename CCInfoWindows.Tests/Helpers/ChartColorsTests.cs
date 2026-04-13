using CCInfoWindows.Helpers;
using Windows.UI;

namespace CCInfoWindows.Tests.Helpers;

public class ChartColorsTests
{
    // --- BuildColorLookup count tests ---

    [Fact]
    public void BuildColorLookup_DarkTheme_Returns101Elements()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        Assert.Equal(101, lookup.Length);
    }

    [Fact]
    public void BuildColorLookup_LightTheme_Returns101Elements()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: false);
        Assert.Equal(101, lookup.Length);
    }

    // --- Dark theme exact stop tests ---

    [Fact]
    public void BuildColorLookup_Index0Dark_ReturnsGreen()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        var expected = Color.FromArgb(255, 0x30, 0xD1, 0x58);
        Assert.Equal(expected, lookup[0]);
    }

    [Fact]
    public void BuildColorLookup_Index50Dark_ReturnsYellow()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        var expected = Color.FromArgb(255, 0xFF, 0xD6, 0x0A);
        Assert.Equal(expected, lookup[50]);
    }

    [Fact]
    public void BuildColorLookup_Index75Dark_ReturnsOrange()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        var expected = Color.FromArgb(255, 0xFF, 0x9F, 0x0A);
        Assert.Equal(expected, lookup[75]);
    }

    [Fact]
    public void BuildColorLookup_Index90Dark_ReturnsRed()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        var expected = Color.FromArgb(255, 0xFF, 0x45, 0x3A);
        Assert.Equal(expected, lookup[90]);
    }

    [Fact]
    public void BuildColorLookup_Index100Dark_SameAsIndex90_ClampedAtRed()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        Assert.Equal(lookup[90], lookup[100]);
    }

    // --- Interpolation test ---

    [Fact]
    public void BuildColorLookup_Index25Dark_IsInterpolatedBetweenGreenAndYellow()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        var green = Color.FromArgb(255, 0x30, 0xD1, 0x58);
        var yellow = Color.FromArgb(255, 0xFF, 0xD6, 0x0A);

        // At index 25 (t=0.25), the position is halfway between green(0.0) and yellow(0.5)
        // t within segment = (0.25 - 0.0) / (0.5 - 0.0) = 0.5
        var expectedR = (byte)(green.R + (yellow.R - green.R) * 0.5);
        var expectedG = (byte)(green.G + (yellow.G - green.G) * 0.5);
        var expectedB = (byte)(green.B + (yellow.B - green.B) * 0.5);
        var expected = Color.FromArgb(255, expectedR, expectedG, expectedB);

        Assert.Equal(expected, lookup[25]);
    }

    // --- Light theme stop tests ---

    [Fact]
    public void BuildColorLookup_Index0Light_ReturnsGreenLight()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: false);
        var expected = Color.FromArgb(255, 0x34, 0xC7, 0x59);
        Assert.Equal(expected, lookup[0]);
    }

    [Fact]
    public void BuildColorLookup_Index90Light_ReturnsRedLight()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: false);
        var expected = Color.FromArgb(255, 0xFF, 0x3B, 0x30);
        Assert.Equal(expected, lookup[90]);
    }

    // --- Theme difference test ---

    [Fact]
    public void BuildColorLookup_DarkAndLightTheme_ReturnDifferentColorsAtSameIndex()
    {
        var darkLookup = ChartColors.BuildColorLookup(isDark: true);
        var lightLookup = ChartColors.BuildColorLookup(isDark: false);

        // Green stop differs between themes
        Assert.NotEqual(darkLookup[0], lightLookup[0]);
    }

    // --- Alpha channel test ---

    [Fact]
    public void BuildColorLookup_AllIndices_HaveAlpha255()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        foreach (var color in lookup)
        {
            Assert.Equal(255, color.A);
        }
    }
}

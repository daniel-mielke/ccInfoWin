using System.Globalization;
using System.Xml.Linq;
using CCInfoWindows.Helpers;
using Windows.UI;

namespace CCInfoWindows.Tests.Helpers;

public class ChartColorsTests
{
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] ProgressBrushKeys =
        ["ProgressGreenBrush", "ProgressYellowBrush", "ProgressOrangeBrush", "ProgressRedBrush"];

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
    public void BuildColorLookup_Index25Dark_IsThirtyPercentTowardsYellow_EasedRamp()
    {
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        var green = Color.FromArgb(255, 0x30, 0xD1, 0x58);
        var yellow = Color.FromArgb(255, 0xFF, 0xD6, 0x0A);

        // Eased ramp: 0.25 is now an explicit anchor at 30% of the green-to-yellow blend, not
        // the 50% a plain 0.00/0.50 interpolation produced. The low end departs from green
        // slowly and the remaining 70% is covered between 25% and 50%, so yellow arrives
        // instead of jumping in.
        const double MixAt25 = 0.30;
        var expectedR = (byte)(green.R + (yellow.R - green.R) * MixAt25);
        var expectedG = (byte)(green.G + (yellow.G - green.G) * MixAt25);
        var expectedB = (byte)(green.B + (yellow.B - green.B) * MixAt25);
        var expected = Color.FromArgb(255, expectedR, expectedG, expectedB);

        Assert.Equal(expected, lookup[25]);
    }

    [Fact]
    public void BuildColorLookup_EasedRamp_StaysBelowTheOldLinearBlendAcrossTheLowEnd()
    {
        // Property form of the same idea: every index between the green anchor and the yellow
        // anchor must be at most as far toward yellow as plain linear interpolation was.
        var lookup = ChartColors.BuildColorLookup(isDark: true);
        var green = Color.FromArgb(255, 0x30, 0xD1, 0x58);
        var yellow = Color.FromArgb(255, 0xFF, 0xD6, 0x0A);

        for (var i = 1; i < 50; i++)
        {
            var linearMix = i / 50.0;
            var linearR = green.R + ((yellow.R - green.R) * linearMix);
            Assert.True(lookup[i].R <= linearR + 1,
                $"index {i}: eased R {lookup[i].R} should not exceed linear {linearR:F1}");
        }
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

    // --- ChartColors is the code-side mirror of Resources/AppTheme.xaml ---

    [Theory]
    [InlineData(true, "Dark")]
    [InlineData(false, "Light")]
    public void ProgressBrushes_MatchAppThemeXaml(bool isDark, string themeKey)
    {
        // MainViewModel's ContextUtilizationBrush / WeeklyUtilizationBrush / SonnetUtilizationBrush
        // resolve the three progress bars through ChartColors instead of Application.Current.Resources
        // (which followed the OS theme, not the app's element theme). That only stays correct while
        // the two palettes agree, so this reads the actual XAML.
        var declared = LoadThemeBrushes(themeKey);

        foreach (var brushKey in ProgressBrushKeys)
        {
            Assert.True(declared.TryGetValue(brushKey, out var expected),
                $"AppTheme.xaml theme dictionary '{themeKey}' declares no {brushKey}.");
            Assert.Equal(expected, ChartColors.GetColor(brushKey, isDark));
        }
    }

    [Theory]
    [InlineData(0.0, "ProgressGreenBrush")]
    [InlineData(0.49, "ProgressGreenBrush")]
    [InlineData(0.50, "ProgressYellowBrush")]
    [InlineData(0.80, "ProgressOrangeBrush")]
    [InlineData(0.95, "ProgressRedBrush")]
    [InlineData(1.50, "ProgressRedBrush")]
    public void GetZoneColor_ReturnsTheThresholdBrushColorForThatZone(double utilization, string expectedKey)
    {
        foreach (var isDark in new[] { true, false })
        {
            Assert.Equal(ChartColors.GetColor(expectedKey, isDark), ChartColors.GetZoneColor(utilization, isDark));
        }
    }

    private static Dictionary<string, Color> LoadThemeBrushes(string themeKey)
    {
        var root = XDocument.Load(FindAppThemePath()).Root!;
        var themeDictionary = root
            .Element(XamlNs + "ResourceDictionary.ThemeDictionaries")!
            .Elements(XamlNs + "ResourceDictionary")
            .Single(d => (string?)d.Attribute(XNs + "Key") == themeKey);

        return themeDictionary
            .Elements(XamlNs + "SolidColorBrush")
            .ToDictionary(
                b => (string)b.Attribute(XNs + "Key")!,
                b => ParseHexColor((string)b.Attribute("Color")!));
    }

    /// <summary>Accepts both #RRGGBB and #AARRGGBB, which AppTheme.xaml mixes.</summary>
    private static Color ParseHexColor(string hex)
    {
        var digits = hex.TrimStart('#');
        var hasAlpha = digits.Length == 8;
        var offset = hasAlpha ? 2 : 0;

        return Color.FromArgb(
            hasAlpha ? Hex(digits, 0) : (byte)255,
            Hex(digits, offset),
            Hex(digits, offset + 2),
            Hex(digits, offset + 4));
    }

    private static byte Hex(string digits, int index) =>
        byte.Parse(digits.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string FindAppThemePath()
    {
        var path = Path.Combine(ProductionSourceFiles.Root, "Resources", "AppTheme.xaml");

        Assert.True(File.Exists(path), $"Resources/AppTheme.xaml not found at {path}.");
        return path;
    }
}

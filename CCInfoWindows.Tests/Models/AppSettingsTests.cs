using System.Text.Json;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal("dark", settings.ColorMode);
        Assert.Null(settings.WindowState);
    }

    [Fact]
    public void Roundtrip_SerializeDeserialize_PreservesAllFields()
    {
        var original = new AppSettings
        {
            WindowState = new WindowState(100, 200, 800, 600),
            RefreshIntervalSeconds = 30,
            ColorMode = "light"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(30, deserialized.RefreshIntervalSeconds);
        Assert.Equal("light", deserialized.ColorMode);
        Assert.NotNull(deserialized.WindowState);
        Assert.Equal(100, deserialized.WindowState.X);
        Assert.Equal(200, deserialized.WindowState.Y);
        Assert.Equal(800, deserialized.WindowState.Width);
        Assert.Equal(600, deserialized.WindowState.Height);
    }

    [Fact]
    public void Deserialize_LegacyJson_WithoutNewFields_AppliesDefaults()
    {
        // Simulates settings.json from Phase 1 (only WindowState)
        var json = """{"windowState":{"X":50,"Y":50,"Width":400,"Height":300}}""";

        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(settings);
        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal("dark", settings.ColorMode);
        Assert.NotNull(settings.WindowState);
        Assert.Equal(50, settings.WindowState.X);
    }
}

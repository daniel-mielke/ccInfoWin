using CCInfoWindows.Models;
using CCInfoWindows.Tests.TestSupport;
using CCInfoWindows.ViewModels;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// The one-line context breakdown under the KONTEXTFENSTER bar ("Basis · Chat · Puffer · Frei"),
/// composed against the shipped resw templates of both locales.
/// </summary>
public class MainViewModelContextBreakdownTests
{
    private static ContextWindowData MidSession() => new()
    {
        TotalTokens = 104_200,
        BaselineTokens = 70_300,
        MaxTokens = 200_000
    };

    // Non-breaking space: binds each label to its value and each dot to the value before it.
    private const char Nbsp = ' ';

    [Fact]
    public void FormatContextBreakdown_German_FillsAllFourSlotsInOrder()
    {
        var text = MainViewModel.FormatContextBreakdown(MidSession(), ReswFixture.Load("de-DE"));

        Assert.Equal("Basis 70.3K · Chat 33.9K · Puffer 33.0K · Frei 62.8K", text.Replace(Nbsp, ' '));
    }

    [Fact]
    public void FormatContextBreakdown_English_FillsAllFourSlotsInOrder()
    {
        var text = MainViewModel.FormatContextBreakdown(MidSession(), ReswFixture.Load("en-US"));

        Assert.Equal("Base 70.3K · Chat 33.9K · Buffer 33.0K · Free 62.8K", text.Replace(Nbsp, ' '));
    }

    /// <summary>
    /// The line wraps at 360 DIP with a 1M model. The only breakable spaces are the ones right after
    /// a dot, so a segment like "Frei 757.6K" moves to the next line whole instead of splitting its
    /// label from its value, and no line starts with a dot. A translation that types plain spaces
    /// breaks that silently — hence this guard on both shipped templates.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void FormatContextBreakdown_BreaksOnlyAfterADot(string locale)
    {
        var text = MainViewModel.FormatContextBreakdown(MidSession(), ReswFixture.Load(locale));

        var segments = text.Split(' ');
        Assert.Equal(4, segments.Length);
        Assert.All(segments[..^1], segment => Assert.EndsWith($"{Nbsp}·", segment, StringComparison.Ordinal));
        Assert.All(segments, segment => Assert.Contains(Nbsp, segment));
    }

    /// <summary>
    /// No baseline means the session has not answered yet, or its head could not be read. The line
    /// then disappears instead of reporting the whole context as "Chat".
    /// </summary>
    [Fact]
    public void FormatContextBreakdown_WithoutBaseline_IsEmpty()
    {
        var text = MainViewModel.FormatContextBreakdown(
            MidSession() with { BaselineTokens = 0 }, _ => throw new InvalidOperationException("must not localize"));

        Assert.Equal(string.Empty, text);
    }
}

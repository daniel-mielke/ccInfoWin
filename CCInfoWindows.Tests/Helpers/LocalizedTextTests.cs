using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// The resw-or-fallback rule, asserted once now that it lives in one place. The four call sites that
/// used to carry their own copy (MainView's bootstrap banner, ExportHelper's PNG captions,
/// CountdownFormatter's date pattern, MainViewModel's action banner) disagreed on what counts as an
/// unusable answer, which is exactly the class of defect finding 30 is about.
///
/// The exception path is asserted by its observable contract — fallback returned, nothing thrown.
/// AppLogTests owns the assertions about what reaches the sink.
/// </summary>
public class LocalizedTextTests
{
    private const string Uid = "SomeCaptionUid";
    private const string Fallback = "Fallback caption";
    private const string LogSource = nameof(LocalizedTextTests);

    [Fact]
    public void Resolve_ReturnsTheTranslation_WhenTheDictionaryAnswers()
    {
        var text = LocalizedText.Resolve(_ => "5-STUNDEN-FENSTER", Uid, Fallback, LogSource);

        Assert.Equal("5-STUNDEN-FENSTER", text);
    }

    [Fact]
    public void Resolve_PassesTheUidToTheLookup()
    {
        var asked = new List<string>();

        LocalizedText.Resolve(uid => { asked.Add(uid); return "translated"; }, Uid, Fallback, LogSource);

        Assert.Equal([Uid], asked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Resolve_FallsBack_WhenTheValueIsBlank(string answer)
    {
        // A built localizer answers an unknown uid with the empty string.
        Assert.Equal(Fallback, LocalizedText.Resolve(_ => answer, Uid, Fallback, LogSource));
    }

    [Fact]
    public void Resolve_FallsBack_WhenTheLookupReturnsNull()
    {
        Assert.Equal(Fallback, LocalizedText.Resolve(_ => null!, Uid, Fallback, LogSource));
    }

    [Fact]
    public void Resolve_FallsBack_WhenTheLocalizerEchoesTheUid()
    {
        // WinUI3Localizer's NullLocalizer — the instance in place before Build() completes — returns
        // the uid it was asked for. Accepting it puts a resource key on screen or into a PNG.
        Assert.Equal(Fallback, LocalizedText.Resolve(uid => uid, Uid, Fallback, LogSource));
    }

    [Fact]
    public void Resolve_AcceptsATranslationThatDiffersFromTheUidOnlyInCase()
    {
        // The echo check is Ordinal on purpose: a locale whose translation happens to be the uid in
        // another casing is a real translation, not a missing one.
        var text = LocalizedText.Resolve(uid => uid.ToLowerInvariant(), Uid, Fallback, LogSource);

        Assert.Equal(Uid.ToLowerInvariant(), text);
    }

    [Fact]
    public void Resolve_FallsBack_WhenTheLookupThrows()
    {
        // Localizer.Get() throws when the host never built, which is one of the startup failures the
        // fallback text exists to describe — it must not become a second exception.
        var text = LocalizedText.Resolve(
            _ => throw new InvalidOperationException("no localizer host"), Uid, Fallback, LogSource);

        Assert.Equal(Fallback, text);
    }

    [Fact]
    public void ResolveOrNull_ReturnsTheTranslation_WhenTheDictionaryAnswers()
    {
        Assert.Equal("ddd dd.MM., HH:mm", LocalizedText.ResolveOrNull(_ => "ddd dd.MM., HH:mm", Uid, LogSource));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveOrNull_ReturnsNull_WhenTheValueIsBlank(string answer)
    {
        Assert.Null(LocalizedText.ResolveOrNull(_ => answer, Uid, LogSource));
    }

    [Fact]
    public void ResolveOrNull_ReturnsNull_WhenTheLocalizerEchoesTheUid()
    {
        // Critical for the date patterns: "WeeklyResetDatePattern" handed to ToString renders as a
        // date, so an echoed uid would silently produce a plausible-looking wrong label.
        Assert.Null(LocalizedText.ResolveOrNull(uid => uid, Uid, LogSource));
    }

    [Fact]
    public void ResolveOrNull_ReturnsNull_WhenTheLookupThrows()
    {
        Assert.Null(LocalizedText.ResolveOrNull(_ => throw new InvalidOperationException(), Uid, LogSource));
    }

    [Fact]
    public void ResolveOrNull_WithoutALocalizerHost_ReturnsNullInsteadOfThrowing()
    {
        // The production overload, exercised the way the app hits it before the localizer is built:
        // Localizer.Get() either throws or hands back the echoing NullLocalizer, and both are "no
        // translation". xUnit can never build the host, so this is the only reachable state here.
        Assert.Null(LocalizedText.ResolveOrNull(Uid, LogSource));
    }

    [Fact]
    public void Resolve_WithoutALocalizerHost_ReturnsTheFallback()
    {
        Assert.Equal(Fallback, LocalizedText.Resolve(Uid, Fallback, LogSource));
    }
}

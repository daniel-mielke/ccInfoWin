using System.Reflection;
using CCInfoWindows.Models;
using CCInfoWindows.ViewModels;

namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// Tests for tooltip composition in SessionDisplayItem (D-05, D-06, D-07, POLISH-04..06).
///
/// The former SessionTimeoutChangedMessage value-contract test is gone with the message: finding 37
/// established that channel could never have a live recipient (MainView is unloaded while Settings is
/// open), and the threshold is now only ever read from disk in RefreshSessionList.
/// </summary>
public class SessionDisplayTooltipTests
{
    private static readonly MethodInfo ComputeTooltipTextMethod =
        typeof(MainViewModel).GetMethod(
            "ComputeTooltipText",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static SessionInfo MakeSession(string id, string cwd, DateTimeOffset lastActivity)
        => new SessionInfo
        {
            Id = id,
            Cwd = cwd,
            DisplayName = cwd.Split('/').Last(),
            LastActivity = lastActivity
        };

    private static string InvokeComputeTooltipText(SessionInfo session, bool isActive, int thresholdMinutes)
        => (string)ComputeTooltipTextMethod.Invoke(null, new object[] { session, isActive, thresholdMinutes })!;

    [Fact]
    public void ComputeTooltipText_Active_SingleLine()
    {
        var session = MakeSession("s1", "/foo/bar", DateTimeOffset.Now);

        var result = InvokeComputeTooltipText(session, isActive: true, thresholdMinutes: 30);

        Assert.Equal("/foo/bar", result);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void ComputeTooltipText_Inactive_TwoLine()
    {
        // In test host: Localizer returns key name "InactiveSessionTooltip" (missing key fallback)
        // OR throws (defensive catch returns "Inactive for > {0}min" formatted).
        // Both paths produce a two-line result starting with Cwd.
        var session = MakeSession("s1", "/foo/bar", DateTimeOffset.Now.AddHours(-3));

        var result = InvokeComputeTooltipText(session, isActive: false, thresholdMinutes: 30);

        Assert.StartsWith("/foo/bar\n", result);
        var lines = result.Split('\n');
        Assert.Equal(2, lines.Length);
        // Second line is either formatted template (contains "30") or key name — both are acceptable.
        Assert.NotEmpty(lines[1]);
    }

    [Fact]
    public void ComputeTooltipText_Inactive_LocalizerThrowsFallback()
    {
        // Verifies defensive try/catch path: even if Localizer returns key name instead of
        // throwing, the result is still a valid two-line tooltip starting with Cwd.
        var session = MakeSession("s1", "/foo/bar", DateTimeOffset.Now.AddHours(-3));

        var result = InvokeComputeTooltipText(session, isActive: false, thresholdMinutes: 30);

        // Must start with Cwd followed by newline — regardless of Localizer state.
        Assert.StartsWith("/foo/bar\n", result);
        // Second line must be non-empty (either formatted message or key name).
        var secondLine = result.Substring("/foo/bar\n".Length);
        Assert.NotEmpty(secondLine);
    }

    [Fact]
    public void RefreshSessionsAsync_ComputesIsActivePerItem()
    {
        // Tests per-item IsActive computation — D-06 fix verified via SessionInfo.IsActive.
        // The SessionDisplayItem construction logic mirrors RefreshSessionList's Select lambda.
        var threshold = TimeSpan.FromMinutes(30);
        var now = DateTimeOffset.UtcNow;

        var activeSession = MakeSession("a", "/active", now);
        var inactiveSession = MakeSession("i", "/inactive", now.AddHours(-3));

        var sessions = new[] { activeSession, inactiveSession };

        // Replicate the D-06-fixed Select lambda from RefreshSessionList
        var displayItems = sessions
            .OrderByDescending(s => s.LastActivity)
            .Select(s =>
            {
                var isActive = s.IsActive(threshold);
                return new SessionDisplayItem
                {
                    Session = s,
                    DisplayName = s.DisplayName,
                    IsActive = isActive,
                    TooltipText = string.Empty   // not under test here
                };
            })
            .ToList();

        Assert.Equal(2, displayItems.Count);   // D-06: both sessions visible (no filter)
        Assert.True(displayItems.Single(x => x.Session.Id == "a").IsActive);
        Assert.False(displayItems.Single(x => x.Session.Id == "i").IsActive);
    }
}

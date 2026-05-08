namespace CCInfoWindows.Tests.ViewModels;

/// <summary>
/// ORGID-03 / ORGID-04 (D-OG-04, D-OG-05): verifies the consecutive-zero-utilization counter
/// state machine. Mirrors MainViewModel.PollUsageCoreAsync logic — DOES NOT instantiate
/// MainViewModel (12-arg ctor + WinRT services).
/// </summary>
public class OrgMismatchSoftPromptTests
{
    private const int Threshold = 5;

    /// <summary>State machine reproducing MainViewModel.PollUsageCoreAsync ORGID block.</summary>
    private sealed class CounterState
    {
        public int Count;
        public bool Suppressed;
        public bool PromptVisible;

        public void OnPoll(double utilization, bool hasActiveSession)
        {
            if (utilization == 0 && hasActiveSession)
            {
                Count++;
                if (Count >= Threshold && !Suppressed)
                    PromptVisible = true;
            }
            else
            {
                Count = 0;
                PromptVisible = false;
            }
        }
    }

    [Fact]
    public void Counter_TriggersAtThreshold_WhenAllPollsZero()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold; i++) s.OnPoll(utilization: 0, hasActiveSession: true);
        Assert.Equal(Threshold, s.Count);
        Assert.True(s.PromptVisible);
    }

    [Fact]
    public void Counter_DoesNotTrigger_BelowThreshold()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold - 1; i++) s.OnPoll(0, true);
        Assert.False(s.PromptVisible);
    }

    [Fact]
    public void Counter_ResetsAndHidesPrompt_OnNonZeroUtilization()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold; i++) s.OnPoll(0, true);
        Assert.True(s.PromptVisible);

        s.OnPoll(utilization: 5, hasActiveSession: true);
        Assert.Equal(0, s.Count);
        Assert.False(s.PromptVisible);
    }

    [Fact]
    public void Counter_DoesNotIncrement_WhenNoActiveSession()
    {
        var s = new CounterState();
        for (int i = 0; i < Threshold + 2; i++) s.OnPoll(utilization: 0, hasActiveSession: false);
        Assert.Equal(0, s.Count);
        Assert.False(s.PromptVisible);
    }

    [Fact]
    public void SuppressionFlag_PreventsPromptAtThreshold()
    {
        var s = new CounterState { Suppressed = true };
        for (int i = 0; i < Threshold + 1; i++) s.OnPoll(0, true);
        Assert.True(s.Count >= Threshold);   // counter still increments
        Assert.False(s.PromptVisible);        // prompt stays hidden
    }
}

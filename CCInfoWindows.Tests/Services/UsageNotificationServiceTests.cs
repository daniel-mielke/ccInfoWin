using CCInfoWindows.Models;
using CCInfoWindows.Services;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.Tests.Helpers;

namespace CCInfoWindows.Tests.Services;

/// <summary>
/// Headless coverage for the threshold and window-reset notifications. Every seam the service
/// exposes exists so these can run without a WinUI host: the clock (the whole feature is
/// time-based and tests must not sleep), the timer factory (WinRT COM), and the toast sink
/// (AppNotificationManager.IsSupported() is false headless, so Show() is unreachable).
/// </summary>
public class UsageNotificationServiceTests
{
    private sealed class InMemoryStore : INotificationStateStore
    {
        private NotificationState _state = new();
        public int SaveCount { get; private set; }

        public InMemoryStore() { }
        public InMemoryStore(NotificationState seed) => _state = seed;

        public NotificationState Load() => _state;
        public void Save(NotificationState state) { _state = state; SaveCount++; }
    }

    /// <summary>Counts how many timers were handed out — the core v1.15.1 assertion.</summary>
    private sealed class CountingTimerFactory
    {
        public List<FakeDispatcherTimer> Created { get; } = [];
        public IDispatcherTimer Create()
        {
            var timer = new FakeDispatcherTimer();
            Created.Add(timer);
            return timer;
        }
    }

    /// <summary>
    /// Advanceable clock. Whether an identity change counts as a rotation now depends on the
    /// current time, so a frozen clock can no longer express "the window really ended".
    /// </summary>
    private sealed class TestClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public DateTimeOffset Read() => UtcNow;
    }

    private static UsageWindow Window(double utilization, DateTimeOffset resetsAt) =>
        new() { Utilization = utilization, ResetsAt = resetsAt };

    private static BurnRatePrediction Prediction(int minutesUntilLimit = 42) =>
        new() { MinutesUntilLimit = minutesUntilLimit, HitsLimitAt = Now.AddMinutes(minutesUntilLimit) };

    private static readonly DateTimeOffset Now = new(2026, 8, 5, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FiveHourReset = Now.AddHours(3);
    private static readonly DateTimeOffset WeeklyReset = Now.AddDays(3);

    private static (UsageNotificationService Sut, List<ToastRequest> Toasts, InMemoryStore Store, CountingTimerFactory Timers)
        CreateSut(NotificationState? seed = null, Func<DateTimeOffset>? clock = null, InMemoryStore? store = null)
    {
        store ??= seed is null ? new InMemoryStore() : new InMemoryStore(seed);
        var toasts = new List<ToastRequest>();
        var timers = new CountingTimerFactory();
        var sut = new UsageNotificationService(store, timers.Create, clock ?? (() => Now), toasts.Add);
        return (sut, toasts, store, timers);
    }

    // --- Window identity (the v1.15.1 fix) ---

    [Fact]
    public void BuildWindowId_IgnoresSubSecondNoise()
    {
        // Real production data: two fields of the SAME API response carried .102078 and .102162.
        var baseline = new DateTimeOffset(2026, 8, 6, 0, 20, 0, TimeSpan.Zero);

        var id = UsageNotificationService.BuildWindowId(baseline);

        Assert.Equal(id, UsageNotificationService.BuildWindowId(baseline.AddMilliseconds(300)));
        Assert.Equal(id, UsageNotificationService.BuildWindowId(baseline.AddMilliseconds(999)));
        Assert.Equal(id, UsageNotificationService.BuildWindowId(baseline.AddTicks(1_020_780)));
    }

    [Fact]
    public void BuildWindowId_IsOffsetInvariant()
    {
        var utc = new DateTimeOffset(2026, 8, 6, 0, 20, 0, TimeSpan.Zero);
        var sameInstantElsewhere = utc.ToOffset(TimeSpan.FromHours(9));

        Assert.Equal(
            UsageNotificationService.BuildWindowId(utc),
            UsageNotificationService.BuildWindowId(sameInstantElsewhere));
    }

    [Fact]
    public void BuildWindowId_DifferentMinutes_DifferentIds()
    {
        var a = new DateTimeOffset(2026, 8, 6, 0, 20, 0, TimeSpan.Zero);

        Assert.NotEqual(
            UsageNotificationService.BuildWindowId(a),
            UsageNotificationService.BuildWindowId(a.AddMinutes(1)));
    }

    // --- Thresholds ---

    [Fact]
    public void CheckWindows_CrossingEighty_FiresOnce()
    {
        var (sut, toasts, _, _) = CreateSut();

        sut.CheckWindows(Window(85, FiveHourReset), null);

        var toast = Assert.Single(toasts);
        Assert.Equal("WindowThresholdNotificationTitle", toast.TitleKey);
        Assert.Equal("FiveHourThresholdNotificationBody", toast.BodyKey);
        Assert.Equal(80, toast.BodyArg);
    }

    [Fact]
    public void CheckWindows_UtilizationWobblingAroundEighty_FiresExactlyOnce()
    {
        // The v1.15.2 guard. There is deliberately no `if (utilization < 80) Notified80 = false`,
        // so API rounding around 79.6/80.4 cannot make the toast repeat.
        var (sut, toasts, _, _) = CreateSut();

        sut.CheckWindows(Window(81, FiveHourReset), null);
        sut.CheckWindows(Window(79, FiveHourReset), null);
        sut.CheckWindows(Window(80.4, FiveHourReset), null);

        Assert.Single(toasts);
    }

    [Fact]
    public void CheckWindows_JumpingStraightToNinetyFive_FiresOnlyTheCriticalToast()
    {
        var (sut, toasts, store, _) = CreateSut();

        sut.CheckWindows(Window(96, FiveHourReset), null);

        var toast = Assert.Single(toasts);
        Assert.Equal(95, toast.BodyArg);
        // Both flags set, so 80 cannot back-fire afterwards.
        Assert.True(store.Load().FiveHour.Notified80);
        Assert.True(store.Load().FiveHour.Notified95);
    }

    [Fact]
    public void CheckWindows_EightyThenNinetyFive_FiresBothOnce()
    {
        var (sut, toasts, _, _) = CreateSut();

        sut.CheckWindows(Window(82, FiveHourReset), null);
        sut.CheckWindows(Window(97, FiveHourReset), null);
        sut.CheckWindows(Window(98, FiveHourReset), null);

        Assert.Equal(2, toasts.Count);
        Assert.Equal(80, toasts[0].BodyArg);
        Assert.Equal(95, toasts[1].BodyArg);
    }

    [Fact]
    public void CheckWindows_WeeklyThreshold_UsesTheWeeklyBody()
    {
        var (sut, toasts, _, _) = CreateSut();

        sut.CheckWindows(null, Window(85, WeeklyReset));

        Assert.Equal("WeeklyThresholdNotificationBody", Assert.Single(toasts).BodyKey);
    }

    [Fact]
    public void CheckWindows_FlagsRestoredFromDisk_DoNotRefireOnFirstPoll()
    {
        // The whole reason the flags are persisted rather than in-memory bools (v1.15.1).
        var seed = new NotificationState
        {
            FiveHour = new WindowNotificationState
            {
                WindowId = UsageNotificationService.BuildWindowId(FiveHourReset),
                ResetsAt = FiveHourReset,
                Notified80 = true,
                PeakUtilization = 85
            }
        };
        var (sut, toasts, _, _) = CreateSut(seed);

        sut.CheckWindows(Window(85, FiveHourReset), null);

        Assert.Empty(toasts);
    }

    [Fact]
    public void CheckWindows_WindowRotation_ReArmsTheThresholds()
    {
        // Finding 20(a): the clock has to pass the old reset time, otherwise a changed identity is
        // a source flip rather than a rotation and deliberately re-arms nothing.
        var clock = new TestClock();
        var (sut, toasts, _, _) = CreateSut(clock: clock.Read);

        sut.CheckWindows(Window(85, FiveHourReset), null);
        clock.UtcNow = FiveHourReset.AddMinutes(1);
        sut.CheckWindows(Window(85, FiveHourReset.AddHours(5)), null);   // new identity

        Assert.Equal(2, toasts.Count(t => t.TitleKey == "WindowThresholdNotificationTitle"));
    }

    [Fact]
    public void CheckWindows_IdentityChangeWhileTheWindowIsStillOpen_DoesNotReArmTheThresholds()
    {
        // The weekly slot is `SevenDayOpus ?? SevenDay`: two windows with independent resets_at, so
        // a poll that transiently omits the primary source changes the identity with nothing reset.
        var (sut, toasts, store, _) = CreateSut();

        sut.CheckWindows(null, Window(85, WeeklyReset));                  // primary source
        toasts.Clear();

        sut.CheckWindows(null, Window(60, WeeklyReset.AddDays(2)));       // fallback source
        sut.CheckWindows(null, Window(85, WeeklyReset));                  // primary source again

        Assert.Empty(toasts);
        Assert.True(store.Load().Weekly.Notified80);
        Assert.Equal(85.0, store.Load().Weekly.PeakUtilization);
    }

    [Fact]
    public void CheckWindows_IdentityChangeWhileTheWindowIsStillOpen_DoesNotAnnounceAReset()
    {
        var (sut, toasts, _, _) = CreateSut();

        sut.CheckWindows(null, Window(40, WeeklyReset));
        toasts.Clear();

        sut.CheckWindows(null, Window(40, WeeklyReset.AddDays(2)));

        Assert.Empty(toasts);
    }

    [Fact]
    public void CheckWindows_WeeklyRotationAfterTheWindowEnded_AnnouncesTheResetAndReArms()
    {
        var clock = new TestClock();
        var (sut, toasts, store, _) = CreateSut(clock: clock.Read);

        sut.CheckWindows(null, Window(85, WeeklyReset));
        toasts.Clear();

        clock.UtcNow = WeeklyReset.AddMinutes(1);
        sut.CheckWindows(null, Window(10, WeeklyReset.AddDays(7)));

        Assert.Equal("WeeklyResetNotificationBody", Assert.Single(toasts).BodyKey);
        Assert.False(store.Load().Weekly.Notified80);
    }

    // --- Countdown arming (the core v1.15.1 fix) ---

    [Fact]
    public void CheckWindows_RepeatedJitterPolls_KeepTheSameArmedTimer()
    {
        // Every 30s poll reaches ArmResetCountdown. Without the _armedWindowIds guard the timer
        // would be recreated each time and its interval would therefore never elapse.
        var (sut, _, _, timers) = CreateSut();

        sut.CheckWindows(Window(10, FiveHourReset), null);
        sut.CheckWindows(Window(11, FiveHourReset.AddMilliseconds(300)), null);
        sut.CheckWindows(Window(12, FiveHourReset.AddMilliseconds(999)), null);

        Assert.Single(timers.Created);
        Assert.True(timers.Created[0].IsEnabled);
    }

    [Fact]
    public void CheckWindows_WindowRotation_ReplacesTheArmedTimer()
    {
        var (sut, _, _, timers) = CreateSut();

        sut.CheckWindows(Window(10, FiveHourReset), null);
        sut.CheckWindows(Window(10, FiveHourReset.AddHours(5)), null);

        Assert.Equal(2, timers.Created.Count);
        Assert.False(timers.Created[0].IsEnabled);   // old one stopped
        Assert.True(timers.Created[1].IsEnabled);
    }

    [Fact]
    public void CheckWindows_ArmsTheCountdownAsADurationNotACalendarMatch()
    {
        // DST immunity: DateTimeOffset subtraction is absolute-time arithmetic, so a reset seven
        // days out is exactly seven days even across a clock change. Fixed offsets keep this
        // independent of the CI machine's time zone database.
        var (sut, _, _, timers) = CreateSut();
        var sevenDaysOut = Now.AddDays(7);

        sut.CheckWindows(null, Window(10, sevenDaysOut));

        Assert.Equal(TimeSpan.FromDays(7), Assert.Single(timers.Created).Interval);
    }

    [Fact]
    public void CheckWindows_ResetAlreadyInThePast_DoesNotArmATimer()
    {
        var (sut, _, _, timers) = CreateSut();

        sut.CheckWindows(Window(10, Now.AddMinutes(-1)), null);

        Assert.Empty(timers.Created);
    }

    // --- Reset notification ---

    [Fact]
    public void ResetTick_WithUsage_FiresTheResetToast()
    {
        var (sut, toasts, store, timers) = CreateSut();
        sut.CheckWindows(Window(40, FiveHourReset), null);
        toasts.Clear();

        timers.Created[0].RaiseTick();

        var toast = Assert.Single(toasts);
        Assert.Equal("WindowResetNotificationTitle", toast.TitleKey);
        Assert.Equal("FiveHourResetNotificationBody", toast.BodyKey);
        Assert.True(store.Load().FiveHour.NotifiedReset);
    }

    [Fact]
    public void ResetTick_IsAOneShot()
    {
        // The tick cancels its own timer first — a repeating 5-hour interval would otherwise
        // duplicate the toast on every subsequent tick.
        var (sut, toasts, _, timers) = CreateSut();
        sut.CheckWindows(Window(40, FiveHourReset), null);
        toasts.Clear();

        timers.Created[0].RaiseTick();
        timers.Created[0].RaiseTick();

        Assert.Single(toasts);
        Assert.False(timers.Created[0].IsEnabled);
    }

    [Fact]
    public void ResetTick_FiveHourWindowNeverUsed_SuppressesButAdvancesTheFlag()
    {
        var (sut, toasts, store, timers) = CreateSut();
        sut.CheckWindows(Window(0, FiveHourReset), null);
        toasts.Clear();

        timers.Created[0].RaiseTick();

        Assert.Empty(toasts);
        Assert.True(store.Load().FiveHour.NotifiedReset);
    }

    [Fact]
    public void ResetTick_FiveHourWindowAtTheLimit_Fires()
    {
        // Strictly-greater-than-zero gating includes 100%: sitting at the limit is exactly when
        // the reminder is worth most.
        var (sut, toasts, _, timers) = CreateSut();
        sut.CheckWindows(Window(100, FiveHourReset), null);
        toasts.Clear();

        timers.Created[0].RaiseTick();

        Assert.Single(toasts);
    }

    [Fact]
    public void ResetTick_WeeklyWindowUnused_StillFires()
    {
        // The weekly window reports regardless of usage; only the 5-hour one is gated.
        var (sut, toasts, _, timers) = CreateSut();
        sut.CheckWindows(null, Window(0, WeeklyReset));
        toasts.Clear();

        timers.Created[0].RaiseTick();

        Assert.Equal("WeeklyResetNotificationBody", Assert.Single(toasts).BodyKey);
    }

    [Fact]
    public void CheckWindows_RotationReportedAsZeroPercent_FiresFromTheOldPeak()
    {
        // v1.15.1: the API sometimes reports the rotated window as 0%/unused in the very poll
        // where resets_at jumps. PeakUtilization is what makes that still report.
        var clock = new TestClock();
        var (sut, toasts, _, _) = CreateSut(clock: clock.Read);
        sut.CheckWindows(Window(70, FiveHourReset), null);
        toasts.Clear();

        clock.UtcNow = FiveHourReset.AddMinutes(1);
        sut.CheckWindows(Window(0, FiveHourReset.AddHours(5)), null);

        Assert.Equal("FiveHourResetNotificationBody", Assert.Single(toasts).BodyKey);
    }

    [Fact]
    public void CheckWindows_RotationAfterAVeryLateRestart_SuppressesTheStaleToast()
    {
        var seed = new NotificationState
        {
            FiveHour = new WindowNotificationState
            {
                WindowId = UsageNotificationService.BuildWindowId(Now.AddHours(-4)),
                ResetsAt = Now.AddHours(-4),   // well past MaxLateResetToastAge
                PeakUtilization = 60
            }
        };
        var (sut, toasts, _, _) = CreateSut(seed);

        sut.CheckWindows(Window(5, FiveHourReset), null);

        Assert.Empty(toasts);
    }

    [Fact]
    public void CheckWindows_RotationJustAfterTheBoundary_StillDeliversTheToast()
    {
        var seed = new NotificationState
        {
            FiveHour = new WindowNotificationState
            {
                WindowId = UsageNotificationService.BuildWindowId(Now.AddMinutes(-5)),
                ResetsAt = Now.AddMinutes(-5),   // inside MaxLateResetToastAge
                PeakUtilization = 60
            }
        };
        var (sut, toasts, _, _) = CreateSut(seed);

        sut.CheckWindows(Window(5, FiveHourReset), null);

        Assert.Equal("FiveHourResetNotificationBody", Assert.Single(toasts).BodyKey);
    }

    [Fact]
    public void CheckWindows_FirstEverPoll_DoesNotAnnounceAReset()
    {
        var (sut, toasts, _, _) = CreateSut();

        sut.CheckWindows(Window(10, FiveHourReset), Window(10, WeeklyReset));

        Assert.Empty(toasts);
    }

    // --- Logout ---

    [Fact]
    public void CancelAll_StopsBothTimersAndClearsState()
    {
        var (sut, _, store, timers) = CreateSut();
        sut.CheckWindows(Window(85, FiveHourReset), Window(85, WeeklyReset));

        sut.CancelAll();

        Assert.All(timers.Created, timer => Assert.False(timer.IsEnabled));
        var state = store.Load();
        Assert.Null(state.FiveHour.WindowId);
        Assert.False(state.FiveHour.Notified80);
        Assert.Equal(0.0, state.Weekly.PeakUtilization);
    }

    [Fact]
    public void CancelAll_ThenANewPoll_NotifiesAgain()
    {
        var (sut, toasts, _, _) = CreateSut();
        sut.CheckWindows(Window(85, FiveHourReset), null);
        sut.CancelAll();
        toasts.Clear();

        sut.CheckWindows(Window(85, FiveHourReset), null);

        Assert.Single(toasts);
    }

    // --- Dispose vs logout (finding 20b) ---

    [Fact]
    public void Dispose_StopsTheTimersButKeepsThePersistedState()
    {
        // Aliasing Dispose to CancelAll would re-arm every threshold toast whenever a
        // ServiceProvider (or a `using` in a test) is disposed.
        var (sut, _, store, timers) = CreateSut();
        sut.CheckWindows(Window(85, FiveHourReset), null);
        var savesBeforeDispose = store.SaveCount;

        sut.Dispose();

        Assert.All(timers.Created, timer => Assert.False(timer.IsEnabled));
        Assert.Equal(savesBeforeDispose, store.SaveCount);
        Assert.Equal(UsageNotificationService.BuildWindowId(FiveHourReset), store.Load().FiveHour.WindowId);
        Assert.True(store.Load().FiveHour.Notified80);
    }

    [Fact]
    public void Dispose_ThenANewPoll_DoesNotRefireTheThresholdToast()
    {
        var store = new InMemoryStore();
        var (first, _, _, _) = CreateSut(store: store);
        first.CheckWindows(Window(85, FiveHourReset), null);
        first.Dispose();

        var (second, toasts, _, _) = CreateSut(store: store);
        second.CheckWindows(Window(85, FiveHourReset), null);

        Assert.Empty(toasts);
    }

    // --- Burn rate (finding 20c) ---

    [Fact]
    public void CheckBurnRate_BeforeAnyWindowIsTracked_WaitsForTheIdentity()
    {
        var (sut, toasts, _, _) = CreateSut();

        sut.CheckBurnRate(Prediction());

        Assert.Empty(toasts);
    }

    [Fact]
    public void CheckBurnRate_OncePerWindow_FiresOnlyOnce()
    {
        var (sut, toasts, _, _) = CreateSut();
        sut.CheckWindows(Window(50, FiveHourReset), null);

        sut.CheckBurnRate(Prediction());
        sut.CheckBurnRate(Prediction());

        Assert.Equal("BurnRateNotificationTitle", Assert.Single(toasts).TitleKey);
    }

    [Fact]
    public void CheckBurnRate_AfterARestartWithinTheSameWindow_DoesNotRefire()
    {
        // The flag is persisted for exactly this reason: history is rehydrated from disk, so the
        // first poll after a restart already has the >= 3 points BurnRateCalculator.Predict needs.
        var store = new InMemoryStore();
        var (first, _, _, _) = CreateSut(store: store);
        first.CheckWindows(Window(50, FiveHourReset), null);
        first.CheckBurnRate(Prediction());

        var (second, toasts, _, _) = CreateSut(store: store);
        second.CheckWindows(Window(55, FiveHourReset), null);
        second.CheckBurnRate(Prediction());

        Assert.Empty(toasts);
    }

    [Fact]
    public void CheckBurnRate_AfterAWindowRotation_ReArms()
    {
        var clock = new TestClock();
        var (sut, toasts, _, _) = CreateSut(clock: clock.Read);
        sut.CheckWindows(Window(50, FiveHourReset), null);
        sut.CheckBurnRate(Prediction());

        clock.UtcNow = FiveHourReset.AddMinutes(1);
        sut.CheckWindows(Window(50, FiveHourReset.AddHours(5)), null);   // rotation updates the identity
        toasts.Clear();                                                 // drop the reset toast it owes
        sut.CheckBurnRate(Prediction());

        Assert.Equal("BurnRateNotificationTitle", Assert.Single(toasts).TitleKey);
    }

    [Fact]
    public void CheckBurnRate_WithdrawnPrediction_ReArms()
    {
        var (sut, toasts, store, _) = CreateSut();
        sut.CheckWindows(Window(50, FiveHourReset), null);
        sut.CheckBurnRate(Prediction());
        toasts.Clear();

        sut.CheckBurnRate(null);
        Assert.Null(store.Load().BurnRateNotifiedWindowId);

        sut.CheckBurnRate(Prediction());

        Assert.Single(toasts);
    }

    [Fact]
    public void CheckBurnRate_WithoutAPredictionOrAFlag_DoesNotWriteTheStore()
    {
        var (sut, _, store, _) = CreateSut();

        sut.CheckBurnRate(null);

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void CancelAll_ClearsTheBurnRateFlag()
    {
        var (sut, toasts, _, _) = CreateSut();
        sut.CheckWindows(Window(50, FiveHourReset), null);
        sut.CheckBurnRate(Prediction());
        sut.CancelAll();
        toasts.Clear();

        sut.CheckWindows(Window(50, FiveHourReset), null);
        sut.CheckBurnRate(Prediction());

        Assert.Equal("BurnRateNotificationTitle", Assert.Single(toasts).TitleKey);
    }

    // --- Null handling ---

    [Fact]
    public void CheckWindows_NullWindows_AreNoOps()
    {
        var (sut, toasts, store, timers) = CreateSut();

        sut.CheckWindows(null, null);

        Assert.Empty(toasts);
        Assert.Empty(timers.Created);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void CheckWindows_WindowWithoutResetsAt_IsANoOp()
    {
        var (sut, toasts, _, timers) = CreateSut();

        sut.CheckWindows(new UsageWindow { Utilization = 99 }, null);

        Assert.Empty(toasts);
        Assert.Empty(timers.Created);
    }
}

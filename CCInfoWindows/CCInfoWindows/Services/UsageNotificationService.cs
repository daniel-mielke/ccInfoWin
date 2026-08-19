using System.Globalization;
using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WinUI3Localizer;

namespace CCInfoWindows.Services;

/// <summary>
/// Usage toasts: burn rate, 80/95% thresholds, and window reset.
///
/// This implements the END STATE of upstream v1.5.0 + v1.15.0/1/2 rather than reproducing the
/// three bugs and then fixing them. The parts that matter:
///
///   - Window identity is resets_at truncated to whole minutes, persisted as a string. An
///     equality test on the raw DateTimeOffset never matches between two polls because the API
///     carries sub-second noise, which is what made the upstream reset toast re-register every
///     30 seconds.
///   - Threshold flags are re-armed by an identity change and by NOTHING else. There is
///     deliberately no `if (utilization &lt; 80) Notified80 = false`, so API rounding around
///     79.6/80.4 and receding weekly usage cannot make it fire twice.
///   - Flags come off disk, so a restart does not refire them. That is precisely why the old
///     in-memory bool was not enough. The burn-rate flag is persisted the same way, keyed by the
///     5-hour identity.
///   - An identity change only counts as a ROTATION once the tracked window's own reset time has
///     passed (see IsRealRotation). The weekly slot is fed by seven_day_opus with a seven_day
///     fallback, two windows with independent resets_at, so an identity change is not by itself
///     proof that anything reset.
///
/// Platform deviation from macOS, decided deliberately: the reset countdown is an in-process
/// IDispatcherTimer, so the reset toast only fires while the app runs. If the app is closed
/// across a window boundary the toast is skipped and the state advances, so it cannot fire late
/// (see MaxLateResetToastAge). Moving to OS scheduling later would only change ArmResetCountdown
/// — the window identity is already exactly the ScheduledToastNotification Id.
///
/// Threading: no IRecipient&lt;T&gt;, so the G-1 IL scan does not apply and no [ThreadSafeReceive]
/// is needed. The timer tick touches service fields and AppNotificationManager only — no
/// [ObservableProperty], no XAML.
///
/// Lifetime: Dispose stops the countdowns and NOTHING else. Wiping the persisted state is the
/// logout semantics of CancelAll, and aliasing the two would let a disposed ServiceProvider (or a
/// `using` in a test) silently re-arm every threshold toast for a window the user was warned about.
/// </summary>
public sealed class UsageNotificationService : IUsageNotificationService, IDisposable
{
    public const double WarnThreshold = 80.0;
    public const double CriticalThreshold = 95.0;

    /// <summary>
    /// How late a missed reset toast may still be delivered when the app restarts after a window
    /// boundary. Beyond this the state advances silently rather than announcing a reset the user
    /// noticed hours ago.
    /// </summary>
    public static readonly TimeSpan MaxLateResetToastAge = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Clock-skew allowance when deciding whether an identity change is a real rotation: resets_at
    /// comes from the server while the comparison uses the local clock.
    ///
    /// Public because MainViewModel.IsWindowReset reads this very value for the history-clearing
    /// decision instead of keeping its own copy — the two answers have to agree at the boundary, or
    /// the chart splices a new window onto the old curve while the toast announces a reset.
    /// </summary>
    public static readonly TimeSpan RotationClockSkewTolerance = TimeSpan.FromMinutes(2);

    private const string BurnRateTag = "usage-burnrate";
    private const string ThresholdTagPrefix = "usage-threshold-";
    private const string ResetTagPrefix = "usage-reset-";

    private readonly INotificationStateStore _store;
    private readonly Func<IDispatcherTimer> _timerFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<ToastRequest> _showToast;

    private readonly Dictionary<UsageWindowKind, IDispatcherTimer> _armedTimers = [];

    /// <summary>
    /// Window identity each countdown is currently armed for. This guard IS the v1.15.1 fix:
    /// every 30-second poll reaches ArmResetCountdown, and without it the timer would be
    /// recreated each time and the interval would therefore never elapse.
    /// </summary>
    private readonly Dictionary<UsageWindowKind, string> _armedWindowIds = [];

    public UsageNotificationService(
        INotificationStateStore store,
        Func<IDispatcherTimer>? timerFactory = null,
        Func<DateTimeOffset>? clock = null,
        Action<ToastRequest>? showToast = null)
    {
        _store = store;
        // Not a DI singleton: each window needs its own one-shot timer, so a shared instance
        // would be wrong. A factory is the right shape here.
        _timerFactory = timerFactory ?? (() => new WinuiDispatcherTimerAdapter());
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _showToast = showToast ?? ShowToastViaAppNotificationManager;
    }

    /// <summary>
    /// resets_at -> UTC -> truncated to whole minutes -> invariant "O".
    ///
    /// Not the 2-minute tolerance IsWindowReset uses: that is correct as a boolean rotation test
    /// but useless as a persisted key, because "roughly this time" cannot be serialized and found
    /// again after a restart. Truncation yields a canonical string that round-trips exactly.
    ///
    /// Honest edge case: a real resets_at sitting a few milliseconds from a minute boundary can
    /// change bucket once and cost one extra toast. Accepted over the v1.15.2 failure mode
    /// (alert fatigue).
    /// </summary>
    public static string BuildWindowId(DateTimeOffset resetsAt)
    {
        var utc = resetsAt.ToUniversalTime();
        var truncated = new DateTimeOffset(
            utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
        return truncated.ToString("O", CultureInfo.InvariantCulture);
    }

    public void CheckBurnRate(BurnRatePrediction? prediction)
    {
        var state = _store.Load();

        if (prediction == null)
        {
            // The prediction was withdrawn (usage flattened out): re-arm for the next genuine warning.
            if (state.BurnRateNotifiedWindowId is null) return;
            state.BurnRateNotifiedWindowId = null;
            _store.Save(state);
            return;
        }

        // MainViewModel calls this BEFORE CheckWindows, so on the poll that observes a rotation the
        // stored identity is still the previous one and the re-arm lands one poll (30 s) later. That
        // is right after a reset, where utilization is near zero and no prediction exists anyway.
        // A null identity means no window has been tracked yet (fresh install / after logout);
        // suppressing until CheckWindows has run once is what keeps the toast from firing twice.
        var windowId = state.FiveHour.WindowId;
        if (windowId is null || state.BurnRateNotifiedWindowId == windowId) return;

        state.BurnRateNotifiedWindowId = windowId;
        _store.Save(state);

        _showToast(new ToastRequest(
            BurnRateTag,
            "BurnRateNotificationTitle",
            "BurnRateNotificationBody",
            BurnRateFormatter.FormatTimeLabel(prediction.MinutesUntilLimit)));
    }

    public void CheckWindows(UsageWindow? fiveHour, UsageWindow? weekly)
    {
        var state = _store.Load();

        var changed = ProcessWindow(UsageWindowKind.FiveHour, fiveHour, state.FiveHour);
        changed |= ProcessWindow(UsageWindowKind.Weekly, weekly, state.Weekly);

        if (changed) _store.Save(state);
    }

    public void CancelAll()
    {
        CancelTimers();
        _store.Save(new NotificationState());
    }

    public void Dispose() => CancelTimers();

    private void CancelTimers()
    {
        foreach (var kind in Enum.GetValues<UsageWindowKind>())
        {
            CancelTimer(kind);
        }
    }

    private bool ProcessWindow(UsageWindowKind kind, UsageWindow? window, WindowNotificationState ws)
    {
        if (window?.ResetsAt is null) return false;

        var resetsAt = window.ResetsAt.Value;
        var windowId = BuildWindowId(resetsAt);
        var changed = false;

        if (ws.WindowId != windowId)
        {
            if (IsRealRotation(ws))
            {
                // The rotation is observed here, so the OLD identity's peak decides whether a reset
                // toast is still owed. Must run before the flags are cleared.
                SendResetToastIfDue(kind, ws);
                ClearNotificationFlags(ws);
            }

            // Adopted either way: the countdown has to track whatever reset time is being reported.
            ws.WindowId = windowId;
            ws.ResetsAt = resetsAt;
            changed = true;
        }

        var utilization = window.Utilization;
        if (utilization > ws.PeakUtilization)
        {
            ws.PeakUtilization = utilization;
            changed = true;
        }

        if (!ws.Notified95 && utilization >= CriticalThreshold)
        {
            // Reaching 95 sets both flags: no back-firing of the 80% toast afterwards.
            ws.Notified95 = true;
            ws.Notified80 = true;
            changed = true;
            SendThresholdToast(kind, CriticalThreshold);
        }
        else if (!ws.Notified80 && utilization >= WarnThreshold)
        {
            ws.Notified80 = true;
            changed = true;
            SendThresholdToast(kind, WarnThreshold);
        }

        ArmResetCountdown(kind, windowId, resetsAt);
        return changed;
    }

    /// <summary>
    /// A new window identity is only a rotation once the tracked window's own reset time has passed.
    ///
    /// MainViewModel feeds the weekly slot with `data.SevenDayOpus ?? data.SevenDay` — two API
    /// windows with independent resets_at. A poll that transiently omits seven_day_opus therefore
    /// changes the identity without anything having rotated, and announcing that as a reset (while
    /// clearing the 80/95 flags so they re-fire when the source flips back) is exactly the alert
    /// fatigue this class exists to prevent. Re-identification keeps the flags and the peak, so the
    /// user is warned once per real window regardless of which source reported it.
    ///
    /// An identity change while the tracked window is still open therefore never re-arms anything.
    /// The cost is one suppressed reset toast if a source flip straddles the real boundary (the
    /// fallback's later resets_at becomes the deadline, so the primary's rotation reads as another
    /// flip); the following rotation reports normally. Pinning the weekly source in MainViewModel
    /// would remove even that.
    /// </summary>
    private bool IsRealRotation(WindowNotificationState previous)
    {
        if (previous.WindowId is null) return true;   // nothing tracked yet

        if (previous.ResetsAt is { } previousReset)
        {
            return _clock() >= previousReset - RotationClockSkewTolerance;
        }

        return true;   // tracked identity without a reset time: cannot judge, so keep the old behaviour
    }

    private static void ClearNotificationFlags(WindowNotificationState ws)
    {
        ws.Notified80 = false;
        ws.Notified95 = false;
        ws.NotifiedReset = false;
        ws.PeakUtilization = 0.0;
    }

    /// <summary>
    /// The half of the reset-toast rule both delivery paths share — the poll that observes a rotation
    /// and the armed countdown that elapses. Whether the user gets the toast must not depend on which
    /// of the two saw the boundary first.
    ///
    /// 5-hour: only report a window that was actually used. Strictly greater includes 100% and excludes
    /// only a genuinely untouched window — sitting at the limit is exactly when the reminder is worth
    /// most. Weekly windows are always reported.
    ///
    /// Deliberately does NOT decide the whole question: each path keeps its own extra guards (first run
    /// ever and late delivery here, a stale window id there) and its own persistence, because only the
    /// tick path has state to save and only the rotation path can be stale.
    /// </summary>
    private static bool IsResetToastOwed(UsageWindowKind kind, WindowNotificationState ws)
        => kind != UsageWindowKind.FiveHour || ws.PeakUtilization > 0.0;

    private void SendResetToastIfDue(UsageWindowKind kind, WindowNotificationState previous)
    {
        if (previous.WindowId is null) return;      // first run ever — nothing rotated
        if (previous.NotifiedReset) return;         // the countdown already delivered it

        var owed = IsResetToastOwed(kind, previous);
        previous.NotifiedReset = true;

        // App was closed across the boundary and came back much later: advance without firing so
        // it cannot arrive as stale news.
        if (previous.ResetsAt is { } previousReset && _clock() - previousReset > MaxLateResetToastAge) return;

        if (owed) SendResetToast(kind);
    }

    /// <summary>
    /// Interval = resetsAt - now. A duration, and DateTimeOffset subtraction is absolute-time
    /// arithmetic, so this is DST-immune by construction — unlike matching calendar fields,
    /// which drift by an hour across a transition (the weekly window ends at 23:59:59, right on
    /// a calendar boundary).
    /// </summary>
    private void ArmResetCountdown(UsageWindowKind kind, string windowId, DateTimeOffset resetsAt)
    {
        if (_armedWindowIds.TryGetValue(kind, out var armedId) && armedId == windowId)
        {
            return;   // pure jitter poll — re-arming here would restart the countdown forever
        }

        CancelTimer(kind);

        var delay = resetsAt - _clock();
        if (delay <= TimeSpan.Zero) return;   // already past; the rotation path handles it

        var timer = _timerFactory();
        timer.Interval = delay;
        timer.Tick += (_, _) => OnResetTick(kind, windowId);
        timer.Start();

        _armedTimers[kind] = timer;
        _armedWindowIds[kind] = windowId;
    }

    private void OnResetTick(UsageWindowKind kind, string windowId)
    {
        // First statement: make this a one-shot. A repeating 5-hour interval would duplicate the
        // toast on every subsequent tick.
        CancelTimer(kind);

        var state = _store.Load();
        var ws = state.For(kind);

        if (ws.WindowId != windowId) return;   // stale timer from a window that already rotated
        if (ws.NotifiedReset) return;

        var owed = IsResetToastOwed(kind, ws);
        ws.NotifiedReset = true;
        _store.Save(state);

        if (owed) SendResetToast(kind);
    }

    private void CancelTimer(UsageWindowKind kind)
    {
        if (_armedTimers.Remove(kind, out var timer))
        {
            timer.Stop();
        }

        _armedWindowIds.Remove(kind);
    }

    private void SendThresholdToast(UsageWindowKind kind, double threshold) =>
        _showToast(new ToastRequest(
            ThresholdTagPrefix + kind,
            "WindowThresholdNotificationTitle",
            kind == UsageWindowKind.FiveHour
                ? "FiveHourThresholdNotificationBody"
                : "WeeklyThresholdNotificationBody",
            (int)threshold));

    private void SendResetToast(UsageWindowKind kind) =>
        _showToast(new ToastRequest(
            ResetTagPrefix + kind,
            "WindowResetNotificationTitle",
            kind == UsageWindowKind.FiveHour
                ? "FiveHourResetNotificationBody"
                : "WeeklyResetNotificationBody"));

    private static void ShowToastViaAppNotificationManager(ToastRequest request)
    {
        if (!AppNotificationManager.IsSupported()) return;

        var title = Localizer.Get().GetLocalizedString(request.TitleKey);
        var template = Localizer.Get().GetLocalizedString(request.BodyKey);
        var body = request.BodyArg is null
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, request.BodyArg);

        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(body)
            .BuildNotification();

        notification.Tag = request.Tag;
        AppNotificationManager.Default.Show(notification);
    }
}

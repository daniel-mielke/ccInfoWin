using CCInfoWindows.Helpers;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WinUI3Localizer;

namespace CCInfoWindows.Services;

/// <summary>
/// Sends a Windows toast notification when burn rate prediction is active.
/// Fires exactly once per warning cycle (resets when prediction clears).
/// </summary>
public class BurnRateNotificationService : IBurnRateNotificationService
{
    private const string NotificationTag = "usage-burnrate";

    private bool _notifiedBurnRate;

    /// <summary>
    /// Checks the current burn rate prediction and fires a toast notification
    /// on the first trigger in each warning cycle.
    /// </summary>
    public void CheckBurnRate(BurnRatePrediction? prediction)
    {
        if (prediction == null)
        {
            _notifiedBurnRate = false;
            return;
        }

        if (_notifiedBurnRate) return;

        _notifiedBurnRate = true;
        SendToast(prediction.MinutesUntilLimit);
    }

    private static void SendToast(int minutesUntilLimit)
    {
        if (!AppNotificationManager.IsSupported()) return;

        var timeLabel = BurnRateFormatter.FormatTimeLabel(minutesUntilLimit);
        var title = Localizer.Get().GetLocalizedString("BurnRateNotificationTitle");
        var body = string.Format(Localizer.Get().GetLocalizedString("BurnRateNotificationBody"), timeLabel);

        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(body)
            .BuildNotification();

        notification.Tag = NotificationTag;
        AppNotificationManager.Default.Show(notification);
    }
}

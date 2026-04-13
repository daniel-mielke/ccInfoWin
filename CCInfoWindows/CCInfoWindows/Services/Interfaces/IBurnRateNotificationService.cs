using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

public interface IBurnRateNotificationService
{
    void CheckBurnRate(BurnRatePrediction? prediction);
}

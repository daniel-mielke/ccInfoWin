namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Abstraction over Microsoft.UI.Xaml.DispatcherTimer that allows headless unit testing
/// without a Windows App SDK UI context. Production code uses WinuiDispatcherTimerAdapter;
/// tests supply a fake implementation.
/// </summary>
public interface IDispatcherTimer
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    event EventHandler<object> Tick;
    void Start();
    void Stop();
}

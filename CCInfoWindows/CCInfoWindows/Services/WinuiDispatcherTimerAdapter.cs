using CCInfoWindows.Services.Interfaces;
using Microsoft.UI.Xaml;

namespace CCInfoWindows.Services;

/// <summary>
/// Production adapter wrapping Microsoft.UI.Xaml.DispatcherTimer.
/// Supplied via the default TimerFactory in SettingsViewModel to enable
/// headless unit testing with a fake IDispatcherTimer.
/// </summary>
internal sealed class WinuiDispatcherTimerAdapter : IDispatcherTimer
{
    private readonly DispatcherTimer _inner = new();

    public TimeSpan Interval
    {
        get => _inner.Interval;
        set => _inner.Interval = value;
    }

    public bool IsEnabled => _inner.IsEnabled;

    // Relay the WinRT TypedEventHandler as a standard .NET EventHandler<object>
    // by subscribing once and re-raising to our own event field.
    private event EventHandler<object>? _tick;

    public event EventHandler<object>? Tick
    {
        add
        {
            if (_tick == null)
                _inner.Tick += ForwardTick;
            _tick += value;
        }
        remove
        {
            _tick -= value;
            if (_tick == null)
                _inner.Tick -= ForwardTick;
        }
    }

    private void ForwardTick(object? sender, object e)
    {
        _tick?.Invoke(sender, e);
    }

    public void Start() => _inner.Start();
    public void Stop() => _inner.Stop();
}

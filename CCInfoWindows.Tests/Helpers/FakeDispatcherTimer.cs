using CCInfoWindows.Services.Interfaces;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Fake IDispatcherTimer for headless unit testing — avoids WinRT COM context requirement.
/// Exposes RaiseTick() to simulate the timer firing in tests.
/// </summary>
internal sealed class FakeDispatcherTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsEnabled { get; private set; }

    private event EventHandler<object>? _tick;

    public event EventHandler<object>? Tick
    {
        add => _tick += value;
        remove => _tick -= value;
    }

    public void Start() => IsEnabled = true;
    public void Stop() => IsEnabled = false;

    public void RaiseTick() => _tick?.Invoke(this, new object());
}

using System.Diagnostics;
using CCInfoWindows.Services.Interfaces;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace CCInfoWindows.Services;

/// <summary>
/// Wraps WinUI 3 Frame control for page navigation.
/// </summary>
public class NavigationService : INavigationService
{
    private Frame? _frame;

    public bool CanGoBack => _frame?.CanGoBack == true;

    public void Initialize(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public void NavigateTo<TPage>() where TPage : Page
    {
        Debug.Assert(_frame is not null, "NavigationService.Initialize must be called before NavigateTo");
        _frame?.Navigate(
            typeof(TPage),
            null,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }
}

using Microsoft.UI.Xaml.Controls;

namespace CCInfoWindows.Services.Interfaces;

/// <summary>
/// Frame-based page navigation service.
/// </summary>
public interface INavigationService
{
    bool CanGoBack { get; }

    void Initialize(Frame frame);

    void NavigateTo<TPage>() where TPage : Page;

    void GoBack();
}

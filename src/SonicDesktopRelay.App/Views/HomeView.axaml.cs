using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AppPage = SonicDesktopRelay.Presentation.Page;

namespace SonicDesktopRelay.App.Views;

[SupportedOSPlatform("windows10.0.19041.0")]
public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private async void OnShare(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Shell shell) await shell.ShareAsync(CancellationToken.None);
    }

    private void OnWatch(object? sender, RoutedEventArgs e)
    {
        // Watching needs a code, and the code entry lives on its own page.
        if (DataContext is Shell shell) shell.ViewModel.CurrentPage = AppPage.Watch;
    }

    private async void OnStop(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Shell shell) await shell.StopAsync(CancellationToken.None);
    }
}

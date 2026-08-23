using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AppPage = SonicDesktopRelay.Presentation.Page;

namespace SonicDesktopRelay.App.Views;

[SupportedOSPlatform("windows10.0.19041.0")]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new Shell();
    }

    private void OnNavigate(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag }
            && Enum.TryParse<AppPage>(tag, out var page)
            && DataContext is Shell shell)
        {
            shell.ViewModel.CurrentPage = page;
        }
    }
}

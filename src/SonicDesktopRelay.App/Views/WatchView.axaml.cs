using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SonicDesktopRelay.App.Views;

[SupportedOSPlatform("windows")]
public partial class WatchView : UserControl
{
    private const int CodeLength = 6;

    public WatchView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Codes are issued uppercase and matched uppercase, so the box shows what will actually
    /// be sent rather than letting the user believe their lowercase entry is something else.
    /// </summary>
    private void OnCodeChanged(object? sender, TextChangedEventArgs e)
    {
        var text = CodeBox.Text ?? string.Empty;
        var cleaned = new string([.. text.Where(char.IsLetterOrDigit)]).ToUpperInvariant();
        if (cleaned != text)
        {
            var caret = CodeBox.CaretIndex;
            CodeBox.Text = cleaned;
            CodeBox.CaretIndex = Math.Min(caret, cleaned.Length);
            return;
        }

        WatchButton.IsEnabled = cleaned.Length == CodeLength
            && DataContext is Shell { ViewModel.CanWatch: true };
    }

    private async void OnWatch(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Shell shell) return;
        var code = CodeBox.Text ?? string.Empty;
        if (code.Length != CodeLength) return;
        await shell.WatchAsync(code, CancellationToken.None);
    }

    private async void OnStop(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Shell shell) await shell.StopAsync(CancellationToken.None);
    }
}

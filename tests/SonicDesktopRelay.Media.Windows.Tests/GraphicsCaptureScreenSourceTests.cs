using SonicDesktopRelay.Media;
using SonicDesktopRelay.Media.Windows;
using Xunit;

namespace SonicDesktopRelay.Media.Windows.Tests;

public sealed class GraphicsCaptureScreenSourceTests
{
    [Fact]
    public async Task Capturing_the_primary_monitor_delivers_frames()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        if (!GraphicsCaptureScreenSource.IsSupported) return;

        var monitor = new MonitorEnumerator().List().Single(x => x.IsPrimary);
        await using var source = new GraphicsCaptureScreenSource();
        var frames = 0;
        VideoFrame? last = null;
        source.FrameCaptured += frame =>
        {
            frames++;
            last = frame;
        };

        await source.StartAsync(monitor, VideoQuality.Default, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await source.StopAsync();

        Assert.True(frames > 0, "no frames were captured in two seconds");
        Assert.Equal(monitor.Width, last!.Width);
        Assert.Equal(monitor.Height, last.Height);
        // BGRA8888: four bytes per pixel, tightly packed.
        Assert.Equal(last.Width * last.Height * 4, last.Bgra.Length);
    }

    [Fact]
    public async Task Stopping_ends_frame_delivery()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        if (!GraphicsCaptureScreenSource.IsSupported) return;

        var monitor = new MonitorEnumerator().List().Single(x => x.IsPrimary);
        await using var source = new GraphicsCaptureScreenSource();
        await source.StartAsync(monitor, VideoQuality.Default, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        await source.StopAsync();
        var after = 0;
        source.FrameCaptured += _ => after++;
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0, after);
    }
}

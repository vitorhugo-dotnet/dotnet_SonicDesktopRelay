using System.Runtime.Versioning;

namespace SonicDesktopRelay.Media.Windows;

/// <summary>
/// The connected monitors, keyed by device name (<c>\\.\DISPLAY1</c>). The device name is what
/// gets persisted as the user's monitor preference: it is stable across restarts and is the
/// key the capture source uses to find the HMONITOR again.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class MonitorEnumerator : IMonitorEnumerator
{
    public IReadOnlyList<MonitorInfo> List()
    {
        var monitors = new List<MonitorInfo>();

        foreach (var display in CaptureInterop.ListDisplays())
        {
            var label = display.IsPrimary
                ? $"{display.DeviceName} — primary ({display.Width}×{display.Height})"
                : $"{display.DeviceName} ({display.Width}×{display.Height})";
            monitors.Add(new MonitorInfo(
                display.DeviceName, label, display.Width, display.Height, display.IsPrimary));
        }

        return monitors;
    }
}

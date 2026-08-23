using SonicDesktopRelay.Media.Windows;
using Xunit;

namespace SonicDesktopRelay.Media.Windows.Tests;

public sealed class MonitorEnumeratorTests
{
    [Fact]
    public void A_machine_with_a_display_reports_at_least_one_monitor()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var monitors = new MonitorEnumerator().List();

        Assert.NotEmpty(monitors);
    }

    [Fact]
    public void Exactly_one_monitor_is_primary()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var monitors = new MonitorEnumerator().List();

        Assert.Single(monitors, x => x.IsPrimary);
    }

    [Fact]
    public void Every_monitor_has_an_id_a_name_and_a_positive_size()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        foreach (var monitor in new MonitorEnumerator().List())
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.Id));
            Assert.False(string.IsNullOrWhiteSpace(monitor.Name));
            Assert.True(monitor.Width > 0);
            Assert.True(monitor.Height > 0);
        }
    }

    [Fact]
    public void Monitor_ids_are_unique()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var monitors = new MonitorEnumerator().List();

        Assert.Equal(monitors.Count, monitors.Select(x => x.Id).Distinct().Count());
    }
}

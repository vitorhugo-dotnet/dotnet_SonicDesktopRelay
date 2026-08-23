using System.Runtime.Versioning;
using SonicDesktopRelay.Core.Identity;
using Xunit;

namespace SonicDesktopRelay.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class FileDeviceCredentialStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sdr-{Guid.NewGuid():N}.bin");

    [Fact]
    public async Task Reading_before_anything_was_written_returns_null()
    {
        var store = new FileDeviceCredentialStore(_path);

        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_written_credential_round_trips()
    {
        var store = new FileDeviceCredentialStore(_path);
        var credential = new DeviceCredential(Guid.NewGuid(), "s3cr3t-value", 4);

        await store.WriteAsync(credential, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(credential, read);
    }

    [Fact]
    public async Task Writing_twice_replaces_rather_than_appends()
    {
        var store = new FileDeviceCredentialStore(_path);
        await store.WriteAsync(new DeviceCredential(Guid.NewGuid(), "first", 1), CancellationToken.None);
        var second = new DeviceCredential(Guid.NewGuid(), "second", 2);

        await store.WriteAsync(second, CancellationToken.None);

        Assert.Equal(second, await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_secret_is_not_readable_as_plain_text_on_disk()
    {
        var store = new FileDeviceCredentialStore(_path);
        await store.WriteAsync(new DeviceCredential(Guid.NewGuid(), "plain-secret-marker", 1), CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(_path);

        Assert.DoesNotContain("plain-secret-marker", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task A_corrupt_file_reads_as_no_credential_rather_than_throwing()
    {
        await File.WriteAllTextAsync(_path, "this is not protected data");
        var store = new FileDeviceCredentialStore(_path);

        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Clearing_removes_the_credential()
    {
        var store = new FileDeviceCredentialStore(_path);
        await store.WriteAsync(new DeviceCredential(Guid.NewGuid(), "gone", 1), CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.ReadAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

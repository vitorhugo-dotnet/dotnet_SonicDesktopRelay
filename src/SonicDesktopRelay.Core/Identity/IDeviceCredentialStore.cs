namespace SonicDesktopRelay.Core.Identity;

public interface IDeviceCredentialStore
{
    Task<DeviceCredential?> ReadAsync(CancellationToken ct);

    Task WriteAsync(DeviceCredential credential, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}

namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// A short-lived DeviceBearer token. <see cref="RotatedCredentialSecret"/> being non-null
/// means the backend replaced the identity during this exchange.
/// </summary>
public sealed record AccessToken(
    string Value,
    DateTimeOffset ExpiresAt,
    Guid DeviceId,
    int CredentialVersion,
    string? RotatedCredentialSecret);

/// <summary>
/// The device endpoints, declared in Core so identity logic does not depend on the HTTP
/// layer. ApiClient implements it; the dependency points inward.
/// </summary>
public interface IDeviceApi
{
    Task<DeviceCredential> BootstrapAsync(string name, CancellationToken ct);

    Task<AccessToken> TokenAsync(Guid deviceId, string secret, CancellationToken ct);
}

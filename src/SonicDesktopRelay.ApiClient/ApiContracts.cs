namespace SonicDesktopRelay.ApiClient;

public sealed record BootstrapRequest(string Name, string DeviceType, string Platform);

public sealed record BootstrapResponse(Guid DeviceId, string CredentialSecret, int CredentialVersion);

public sealed record TokenRequest(Guid DeviceId, string CredentialSecret);

/// <summary>
/// A non-null <see cref="RotatedCredentialSecret"/> means the backend replaced this identity:
/// <see cref="DeviceId"/> is a new id and the secret is its new one. Both must be persisted in
/// place of what was stored, because the previous pair no longer exists.
/// </summary>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string[] Scopes,
    Guid DeviceId,
    int CredentialVersion,
    string? RotatedCredentialSecret);

public static class DeviceConstants
{
    public const string DeviceType = "windows_desktop";
    public const string Platform = "windows";
}

namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// The device's durable identity with the backend. The secret is shown by the API exactly
/// once, at bootstrap or when a token exchange rotates it, so losing this record means
/// re-registering as a new device and re-sharing codes.
/// </summary>
public sealed record DeviceCredential(Guid DeviceId, string CredentialSecret, int CredentialVersion);

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

public sealed record CreateSessionRequest(int MaxViewers, string Mode);

public sealed record JoinSessionRequest(string Code);

/// <summary>
/// <c>Code</c> is present only on the responses that issue one (create and rotate); reading a
/// session back never re-exposes it.
/// </summary>
public sealed record SessionResponse(
    Guid Id,
    Guid SourceDeviceId,
    string Status,
    string Mode,
    int MaxViewers,
    DateTimeOffset CodeExpiresAt,
    string? Code);

public sealed record ParticipantResponse(Guid ParticipantId, string Role, string Status, bool IsSelf);

public sealed record ParticipantsResponse(Guid SessionId, string Mode, ParticipantResponse[] Participants);

public static class SessionModes
{
    public const string ScreenShare = "screen_share";
}

public static class ApiErrorCodes
{
    public const string InvalidCode = "invalid_code";
    public const string NotPaired = "not_paired";
    public const string DeviceTypeNotAllowed = "device_type_not_allowed";
    public const string InvalidSessionMode = "invalid_session_mode";
}

/// <summary>
/// One ICE server. <c>Urls</c> is an array in the WebRTC configuration dictionary, and the
/// backend follows that shape even when it only ever returns one entry.
/// </summary>
public sealed record IceServerResponse(string[] Urls, string? Username, string? Credential);

public sealed record IceServersResponse(IceServerResponse[] IceServers);

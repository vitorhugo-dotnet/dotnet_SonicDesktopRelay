namespace SonicDesktopRelay.Rtc;

public sealed record IceServer(string Url, string? Username, string? Credential);

/// <summary>
/// ICE configuration from <c>GET /api/webrtc/ice-servers</c>. <see cref="ForceRelay"/> is the
/// Settings toggle: useful for proving a relay path works, expensive to leave on.
/// </summary>
public sealed record IceServerSettings(IReadOnlyList<IceServer> Servers, bool ForceRelay)
{
    public static IceServerSettings None { get; } = new([], ForceRelay: false);
}

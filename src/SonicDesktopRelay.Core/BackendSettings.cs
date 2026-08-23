namespace SonicDesktopRelay.Core;

/// <summary>
/// Where the SonicRelay backend lives. Parsed rather than constructed so a bad value typed
/// into Settings fails at the edge, with the UI still able to explain itself, instead of
/// throwing from inside an HTTP call later.
/// </summary>
public sealed record BackendSettings(Uri BaseAddress)
{
    public static BackendSettings? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return new BackendSettings(uri);
    }

    /// <summary>
    /// The authenticated signaling endpoint for one session. The scheme tracks the base
    /// address: an https backend must not be reached over a plaintext socket.
    /// </summary>
    public Uri SignalingUri(Guid sessionId)
    {
        var builder = new UriBuilder(BaseAddress)
        {
            Scheme = BaseAddress.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/ws/signaling",
            Query = $"sessionId={sessionId}"
        };
        return builder.Uri;
    }
}

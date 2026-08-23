namespace SonicDesktopRelay.Signaling;

/// <summary>
/// The socket, behind a seam. ClientWebSocket cannot be driven from a test without a real
/// listener, and what needs testing is the reconnect and dispatch logic above it.
/// </summary>
public interface IWebSocketAdapter : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct);

    Task SendAsync(string json, CancellationToken ct);

    /// <summary>Returns the next text frame, or null once the socket has closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken ct);
}

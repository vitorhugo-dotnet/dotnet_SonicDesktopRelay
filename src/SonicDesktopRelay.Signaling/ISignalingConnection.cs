namespace SonicDesktopRelay.Signaling;

public enum SignalingState
{
    Disconnected,
    Connecting,
    Connected,

    /// <summary>
    /// The socket dropped but the session is still live. The server holds the participant for
    /// its grace period, so peers are told to wait rather than to tear anything down.
    /// </summary>
    Reconnecting,

    /// <summary>The session is over. No further attempt will be made.</summary>
    Terminated
}

public interface ISignalingConnection : IAsyncDisposable
{
    SignalingState State { get; }

    event Action<SignalingEnvelope>? FrameReceived;

    event Action<SignalingState>? StateChanged;

    Task StartAsync(Guid sessionId, CancellationToken ct);

    Task SendAsync(string type, Guid? to, object? payload, CancellationToken ct);
}

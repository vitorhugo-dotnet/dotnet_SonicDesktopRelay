using SonicDesktopRelay.Signaling;

namespace SonicDesktopRelay.Presentation;

public enum SessionPhase
{
    Idle,
    Preparing,
    Sharing,
    Joining,
    Watching,
    Ending,
    Failed
}

/// <summary>
/// Everything the UI is allowed to know, as one immutable value. Screens bind to this rather
/// than reaching into the runtime, so no page can hold a stale private copy of the state.
/// </summary>
public sealed record SessionSnapshot(
    SessionPhase Phase,
    string? Code,
    Guid? SessionId,
    int ViewerCount,
    SignalingState Signaling,
    string? Error)
{
    public static SessionSnapshot Idle { get; } =
        new(SessionPhase.Idle, null, null, 0, SignalingState.Disconnected, null);

    public bool IsBusy => Phase is SessionPhase.Preparing or SessionPhase.Joining or SessionPhase.Ending;
}

public sealed record CreatedSession(Guid SessionId, string Code);

/// <summary>
/// A backend refusal, carrying the API's machine-readable code so the runtime can put it in
/// the snapshot without the presentation layer depending on HTTP types.
/// </summary>
public sealed class SessionApiFailure(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ISessionApi
{
    Task<CreatedSession> CreateScreenShareAsync(int maxViewers, CancellationToken ct);

    Task<Guid> JoinAsync(string code, CancellationToken ct);

    Task EndAsync(Guid sessionId, CancellationToken ct);
}

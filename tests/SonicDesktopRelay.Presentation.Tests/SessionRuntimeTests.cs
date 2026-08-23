using SonicDesktopRelay.Signaling;
using Xunit;

namespace SonicDesktopRelay.Presentation.Tests;

public sealed class SessionRuntimeTests
{
    private static readonly Guid SessionId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    [Fact]
    public void A_fresh_runtime_is_idle()
    {
        var runtime = new SessionRuntime(new FakeSessionApi(), () => new FakeConnection());

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
        Assert.Null(runtime.Snapshot.Code);
    }

    [Fact]
    public async Task Sharing_creates_the_session_and_exposes_its_code()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartSharingAsync(3, CancellationToken.None);

        Assert.Equal(SessionPhase.Sharing, runtime.Snapshot.Phase);
        Assert.Equal("AB12CD", runtime.Snapshot.Code);
        Assert.Equal(SessionId, runtime.Snapshot.SessionId);
        Assert.Equal(3, api.RequestedMaxViewers);
    }

    [Fact]
    public async Task Sharing_passes_through_preparing()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        var phases = new List<SessionPhase>();
        runtime.Changed += snapshot => phases.Add(snapshot.Phase);

        await runtime.StartSharingAsync(3, CancellationToken.None);

        Assert.Equal([SessionPhase.Preparing, SessionPhase.Sharing], phases);
    }

    [Fact]
    public async Task Watching_joins_with_the_code_and_reaches_watching()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartWatchingAsync("ab12cd", CancellationToken.None);

        Assert.Equal(SessionPhase.Watching, runtime.Snapshot.Phase);
        Assert.Equal("ab12cd", api.JoinedWithCode);
    }

    [Fact]
    public async Task A_join_failure_lands_in_failed_with_the_error_code()
    {
        var api = new FakeSessionApi { JoinFailureCode = "invalid_code" };
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartWatchingAsync("ZZZZZZ", CancellationToken.None);

        Assert.Equal(SessionPhase.Failed, runtime.Snapshot.Phase);
        Assert.Equal("invalid_code", runtime.Snapshot.Error);
    }

    [Fact]
    public async Task Failing_leaves_no_session_behind()
    {
        var api = new FakeSessionApi { JoinFailureCode = "device_type_not_allowed" };
        var runtime = new SessionRuntime(api, () => new FakeConnection());

        await runtime.StartWatchingAsync("AB12CD", CancellationToken.None);

        Assert.Null(runtime.Snapshot.SessionId);
    }

    [Fact]
    public async Task Sharing_while_already_sharing_is_refused_rather_than_stacking_sessions()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        await runtime.StartSharingAsync(3, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.StartWatchingAsync("AB12CD", CancellationToken.None));

        Assert.Equal(SessionPhase.Sharing, runtime.Snapshot.Phase);
        Assert.Equal(1, api.CreateCalls);
    }

    [Fact]
    public async Task Stopping_a_shared_session_ends_it_and_returns_to_idle()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        await runtime.StartSharingAsync(3, CancellationToken.None);

        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
        Assert.Equal(1, api.EndCalls);
        Assert.Null(runtime.Snapshot.Code);
    }

    [Fact]
    public async Task Stopping_a_watched_session_does_not_end_it_for_everyone()
    {
        var api = new FakeSessionApi();
        var runtime = new SessionRuntime(api, () => new FakeConnection());
        await runtime.StartWatchingAsync("AB12CD", CancellationToken.None);

        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
        // Only the publishing device may end a session; a viewer leaving just disconnects.
        Assert.Equal(0, api.EndCalls);
    }

    [Fact]
    public async Task A_session_ended_frame_returns_a_viewer_to_idle()
    {
        var api = new FakeSessionApi();
        var connection = new FakeConnection();
        var runtime = new SessionRuntime(api, () => connection);
        await runtime.StartWatchingAsync("AB12CD", CancellationToken.None);

        connection.Emit(SignalingMessageTypes.SessionEnded);

        Assert.Equal(SessionPhase.Idle, runtime.Snapshot.Phase);
    }

    [Fact]
    public async Task Viewers_joining_and_leaving_move_the_viewer_count()
    {
        var api = new FakeSessionApi();
        var connection = new FakeConnection();
        var runtime = new SessionRuntime(api, () => connection);
        await runtime.StartSharingAsync(3, CancellationToken.None);

        connection.Emit(SignalingMessageTypes.SessionJoined);
        connection.Emit(SignalingMessageTypes.SessionJoined);
        connection.Emit(SignalingMessageTypes.SessionLeft);

        Assert.Equal(1, runtime.Snapshot.ViewerCount);
    }

    private sealed class FakeSessionApi : ISessionApi
    {
        public int CreateCalls { get; private set; }

        public int EndCalls { get; private set; }

        public int RequestedMaxViewers { get; private set; }

        public string? JoinedWithCode { get; private set; }

        public string? JoinFailureCode { get; init; }

        public Task<CreatedSession> CreateScreenShareAsync(int maxViewers, CancellationToken ct)
        {
            CreateCalls++;
            RequestedMaxViewers = maxViewers;
            return Task.FromResult(new CreatedSession(SessionId, "AB12CD"));
        }

        public Task<Guid> JoinAsync(string code, CancellationToken ct)
        {
            JoinedWithCode = code;
            if (JoinFailureCode is not null)
                throw new SessionApiFailure(JoinFailureCode, "Join refused.");
            return Task.FromResult(SessionId);
        }

        public Task EndAsync(Guid sessionId, CancellationToken ct)
        {
            EndCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnection : ISignalingConnection
    {
        public SignalingState State { get; private set; } = SignalingState.Disconnected;

        public event Action<SignalingEnvelope>? FrameReceived;

        public event Action<SignalingState>? StateChanged;

        public Task StartAsync(Guid sessionId, CancellationToken ct)
        {
            State = SignalingState.Connected;
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public Task SendAsync(string type, Guid? to, object? payload, CancellationToken ct) =>
            Task.CompletedTask;

        public void Emit(string type) =>
            FrameReceived?.Invoke(new SignalingEnvelope(type, null, null, null, null, null, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

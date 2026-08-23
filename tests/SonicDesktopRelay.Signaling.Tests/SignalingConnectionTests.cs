using System.Threading.Channels;
using SonicDesktopRelay.Core;
using Xunit;

namespace SonicDesktopRelay.Signaling.Tests;

public sealed class SignalingConnectionTests
{
    private static readonly Guid SessionId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
    private static readonly BackendSettings Settings = BackendSettings.TryParse("https://relay.example.com")!;

    [Fact]
    public async Task Starting_connects_to_the_session_signaling_uri_with_the_token()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));

        await connection.StartAsync(SessionId, CancellationToken.None);

        Assert.Equal($"wss://relay.example.com/ws/signaling?sessionId={SessionId}", socket.ConnectedTo!.ToString());
        Assert.Equal("jwt", socket.AccessToken);
        Assert.Equal(SignalingState.Connected, connection.State);
    }

    [Fact]
    public async Task Received_frames_are_surfaced_as_envelopes()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        var received = new List<SignalingEnvelope>();
        connection.FrameReceived += received.Add;
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Push("""{"type":"session.joined","payload":{"role":"publisher"}}""");
        await socket.DrainAsync();

        Assert.Single(received);
        Assert.Equal("session.joined", received[0].Type);
    }

    [Fact]
    public async Task An_unparseable_frame_is_dropped_without_closing_the_connection()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        var received = new List<SignalingEnvelope>();
        connection.FrameReceived += received.Add;
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Push("{ garbage");
        socket.Push("""{"type":"ping"}""");
        await socket.DrainAsync();

        Assert.Single(received);
        Assert.Equal(SignalingState.Connected, connection.State);
    }

    [Fact]
    public async Task Session_ended_is_terminal_and_stops_the_connection()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Push("""{"type":"session.ended","payload":{}}""");
        await socket.DrainAsync();

        Assert.Equal(SignalingState.Terminated, connection.State);
    }

    [Fact]
    public async Task Sending_writes_the_outbound_envelope_shape()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"));
        await connection.StartAsync(SessionId, CancellationToken.None);
        var to = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96401");

        await connection.SendAsync(SignalingMessageTypes.ViewerReady, to, new { }, CancellationToken.None);

        Assert.Contains("\"type\":\"viewer.ready\"", socket.Sent[0]);
        Assert.Contains(to.ToString(), socket.Sent[0]);
    }

    [Fact]
    public async Task A_dropped_socket_moves_to_reconnecting_and_reconnects()
    {
        var socket = new FakeWebSocket();
        await using var connection = new SignalingConnection(socket, Settings, _ => Task.FromResult("jwt"))
        {
            ReconnectDelay = TimeSpan.Zero
        };
        var states = new List<SignalingState>();
        connection.StateChanged += states.Add;
        await connection.StartAsync(SessionId, CancellationToken.None);

        socket.Close();
        await socket.WaitForReconnectAsync();

        Assert.Contains(SignalingState.Reconnecting, states);
        Assert.Equal(2, socket.ConnectCount);
    }

    private sealed class FakeWebSocket : IWebSocketAdapter
    {
        private Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
        private TaskCompletionSource _reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri? ConnectedTo { get; private set; }

        public string? AccessToken { get; private set; }

        public int ConnectCount { get; private set; }

        public List<string> Sent { get; } = [];

        public Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct)
        {
            ConnectedTo = uri;
            AccessToken = accessToken;
            ConnectCount++;
            if (ConnectCount > 1)
            {
                _inbound = Channel.CreateUnbounded<string?>();
                _reconnected.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task SendAsync(string json, CancellationToken ct)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken ct) =>
            await _inbound.Reader.ReadAsync(ct);

        public void Push(string frame) => _inbound.Writer.TryWrite(frame);

        public void Close() => _inbound.Writer.TryWrite(null);

        /// <summary>Yields until the receive loop has drained everything pushed so far.</summary>
        public async Task DrainAsync()
        {
            while (_inbound.Reader.Count > 0) await Task.Delay(1);
            await Task.Delay(20);
        }

        public Task WaitForReconnectAsync() => _reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

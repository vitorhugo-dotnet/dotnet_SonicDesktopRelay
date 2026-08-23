using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SonicDesktopRelay.Media;

namespace SonicDesktopRelay.Rtc;

/// <summary>
/// A viewer's connection, backed by SIPSorcery. The video track is <c>sendonly</c>: this phase
/// publishes a screen and receives nothing back.
/// </summary>
public sealed class SipSorceryPeerConnection : IPeerConnection
{
    /// <summary>H.264 over WebRTC is a dynamic payload type; 96 is the conventional first one.</summary>
    private const int H264PayloadId = 96;

    /// <summary>The RTP clock for video is 90 kHz, fixed by RFC 3551.</summary>
    private const uint VideoClockRate = 90_000;

    private readonly RTCPeerConnection _connection;
    private readonly object _gate = new();
    private bool _negotiated;
    private bool _closed;

    public SipSorceryPeerConnection(Guid participantId, IceServerSettings ice)
    {
        ParticipantId = participantId;

        var configuration = new RTCConfiguration
        {
            iceServers = ice.Servers
                .Select(x => new RTCIceServer { urls = x.Url, username = x.Username, credential = x.Credential })
                .ToList(),
            iceTransportPolicy = ice.ForceRelay ? RTCIceTransportPolicy.relay : RTCIceTransportPolicy.all
        };
        _connection = new RTCPeerConnection(configuration);

        // packetization-mode=1 is what every browser and native decoder expects for H.264 over
        // WebRTC; without it a viewer negotiates single-NAL mode and chokes on the first frame
        // larger than an MTU.
        var videoTrack = new MediaStreamTrack(
            new VideoFormat(VideoCodecsEnum.H264, H264PayloadId, 90_000, "packetization-mode=1"),
            MediaStreamStatusEnum.SendOnly);
        _connection.addTrack(videoTrack);

        _connection.onicecandidate += candidate =>
        {
            if (candidate is null) return;
            IceCandidateGathered?.Invoke(candidate.candidate, candidate.sdpMid, candidate.sdpMLineIndex);
        };

        _connection.OnReceiveReport += (_, mediaType, report) =>
        {
            if (mediaType != SDPMediaTypesEnum.video || report is null) return;
            OnRtcpReport(report);
        };

        _connection.onconnectionstatechange += state =>
        {
            // A viewer that has just connected has no reference frame at all, and keyframes are
            // only produced on demand here, so ask for one the moment the transport is usable.
            if (state == RTCPeerConnectionState.connected) KeyFrameRequested?.Invoke();
        };
    }

    public Guid ParticipantId { get; }

    public event Action<string, string?, int?>? IceCandidateGathered;

    public event Action? KeyFrameRequested;

    public event Action<double>? PacketLossReported;

    public async Task<string> CreateOfferAsync(CancellationToken ct)
    {
        var offer = _connection.createOffer();
        await _connection.setLocalDescription(offer).WaitAsync(ct);
        return offer.sdp;
    }

    public Task ApplyAnswerAsync(string sdp, CancellationToken ct)
    {
        var result = _connection.setRemoteDescription(
            new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp });
        if (result != SetDescriptionResultEnum.OK)
            throw new InvalidOperationException($"The viewer's answer was rejected: {result}.");

        lock (_gate) _negotiated = true;
        return Task.CompletedTask;
    }

    public Task AddIceCandidateAsync(string candidate, string? sdpMid, int? sdpMLineIndex, CancellationToken ct)
    {
        _connection.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = (ushort)(sdpMLineIndex ?? 0)
        });
        return Task.CompletedTask;
    }

    public void SendVideo(EncodedVideoSample sample)
    {
        // Frames keep coming from the shared pipeline while this particular viewer is still
        // negotiating. Dropping them is correct: the viewer has no decoder yet, and throwing
        // would take down the capture loop that every other viewer depends on.
        lock (_gate)
        {
            if (_closed || !_negotiated) return;
        }

        if (_connection.connectionState != RTCPeerConnectionState.connected) return;

        try
        {
            _connection.SendVideo(VideoClockRate / 30, sample.Data.ToArray());
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException
                                      or ApplicationException or System.Net.Sockets.SocketException)
        {
            // One viewer's socket dying must never propagate into the fan-out loop.
        }
    }

    private void OnRtcpReport(RTCPCompoundPacket report)
    {
        // A PLI (or FIR) is a viewer saying "I cannot decode what you are sending" — the answer
        // is a keyframe, and with on-demand-only keyframes this is the sole trigger.
        var feedbackType = report.Feedback?.Header?.PayloadFeedbackMessageType;
        if (feedbackType is PSFBFeedbackTypesEnum.PLI or PSFBFeedbackTypesEnum.FIR)
            KeyFrameRequested?.Invoke();

        var samples = report.ReceiverReport?.ReceptionReports
                      ?? report.SenderReport?.ReceptionReports;
        if (samples is null || samples.Count == 0) return;

        // FractionLost is an 8-bit fixed-point fraction of the interval since the last report.
        var worst = samples.Max(x => x.FractionLost);
        PacketLossReported?.Invoke(worst / 256.0);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closed) return ValueTask.CompletedTask;
            _closed = true;
        }

        _connection.close();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class SipSorceryPeerConnectionFactory(IceServerSettings ice) : IPeerConnectionFactory
{
    public IPeerConnection Create(Guid participantId) => new SipSorceryPeerConnection(participantId, ice);
}

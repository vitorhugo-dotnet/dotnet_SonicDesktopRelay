using SonicDesktopRelay.Rtc;
using Xunit;

namespace SonicDesktopRelay.Rtc.Tests;

public sealed class SipSorceryPeerConnectionTests
{
    private static readonly IceServerSettings Ice = new(
        [new IceServer("stun:stun.example.com:3478", null, null)], ForceRelay: false);

    [Fact]
    public async Task An_offer_advertises_a_sendonly_h264_video_track()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice);
        await using var peer = factory.Create(Guid.NewGuid());

        var sdp = await peer.CreateOfferAsync(CancellationToken.None);

        Assert.Contains("m=video", sdp);
        Assert.Contains("H264", sdp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a=sendonly", sdp);
    }

    [Fact]
    public async Task The_offer_contains_no_audio_track_in_this_phase()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice);
        await using var peer = factory.Create(Guid.NewGuid());

        var sdp = await peer.CreateOfferAsync(CancellationToken.None);

        Assert.DoesNotContain("m=audio", sdp);
    }

    [Fact]
    public async Task The_peer_reports_the_participant_it_was_created_for()
    {
        var participantId = Guid.NewGuid();
        var factory = new SipSorceryPeerConnectionFactory(Ice);

        await using var peer = factory.Create(participantId);

        Assert.Equal(participantId, peer.ParticipantId);
    }

    [Fact]
    public async Task Forcing_relay_produces_a_relay_only_offer()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice with { ForceRelay = true });
        await using var peer = factory.Create(Guid.NewGuid());

        var sdp = await peer.CreateOfferAsync(CancellationToken.None);

        // With relay forced and no TURN server configured, no host candidates may leak.
        Assert.DoesNotContain("typ host", sdp);
    }

    [Fact]
    public async Task Sending_video_before_the_answer_arrives_does_not_throw()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice);
        await using var peer = factory.Create(Guid.NewGuid());
        await peer.CreateOfferAsync(CancellationToken.None);

        // Frames keep arriving from the pipeline while negotiation is still in flight; the
        // peer must drop them quietly rather than take down the capture loop.
        var exception = Record.Exception(() => peer.SendVideo(
            new Media.EncodedVideoSample(new byte[8], TimeSpan.Zero, true, 1920, 1080)));

        Assert.Null(exception);
    }
}

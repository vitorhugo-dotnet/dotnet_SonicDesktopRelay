using System.Text.Json;
using Xunit;

namespace SonicDesktopRelay.Signaling.Tests;

public sealed class SignalingEnvelopeTests
{
    [Fact]
    public void An_outbound_message_carries_only_type_to_and_payload()
    {
        var to = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        var json = SignalingEnvelope.Serializer.ToJson(SignalingMessageTypes.ViewerReady, to, new { });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("viewer.ready", root.GetProperty("type").GetString());
        Assert.Equal(to, root.GetProperty("to").GetGuid());
        Assert.True(root.TryGetProperty("payload", out _));
        // sessionId, from and timestamp are the server's to assign; sending them is at best
        // ignored and at worst a client claiming an identity it does not have.
        Assert.False(root.TryGetProperty("sessionId", out _));
        Assert.False(root.TryGetProperty("from", out _));
        Assert.False(root.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public void A_broadcast_message_omits_the_recipient()
    {
        var json = SignalingEnvelope.Serializer.ToJson(SignalingMessageTypes.Ping, to: null, payload: null);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("to", out _));
    }

    [Fact]
    public void A_server_frame_parses_into_its_fields()
    {
        const string frame = """
            {"type":"session.joined","messageId":"6f9619ff-8b86-d011-b42d-00cf4fc96401",
             "sessionId":"6f9619ff-8b86-d011-b42d-00cf4fc96402","from":null,
             "to":"6f9619ff-8b86-d011-b42d-00cf4fc96403","timestamp":"2026-08-23T14:00:00Z",
             "payload":{"participantId":"6f9619ff-8b86-d011-b42d-00cf4fc96403","role":"publisher"}}
            """;

        var envelope = SignalingEnvelope.Serializer.TryParse(frame);

        Assert.NotNull(envelope);
        Assert.Equal("session.joined", envelope!.Type);
        Assert.Null(envelope.From);
        Assert.Equal(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96403"), envelope.To);
        Assert.Equal("publisher", envelope.Payload!.Value.GetProperty("role").GetString());
    }

    [Fact]
    public void An_unparseable_frame_returns_null_rather_than_throwing()
    {
        Assert.Null(SignalingEnvelope.Serializer.TryParse("{ this is not json"));
    }

    [Fact]
    public void A_frame_without_a_type_returns_null()
    {
        Assert.Null(SignalingEnvelope.Serializer.TryParse("""{"messageId":"x"}"""));
    }

    [Fact]
    public void An_unknown_type_still_parses_so_the_app_can_ignore_it_deliberately()
    {
        var envelope = SignalingEnvelope.Serializer.TryParse("""{"type":"something.new","payload":{}}""");

        Assert.NotNull(envelope);
        Assert.Equal("something.new", envelope!.Type);
    }
}

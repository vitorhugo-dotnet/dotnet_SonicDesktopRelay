using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonicDesktopRelay.Signaling;

/// <summary>
/// One signaling frame. Inbound, every metadata field is the server's word. Outbound, only
/// <c>type</c>, <c>to</c> and <c>payload</c> are ours to set — the server overwrites
/// <c>from</c> with the authenticated participant and assigns its own timestamp, so sending
/// them would be noise at best.
/// </summary>
public sealed record SignalingEnvelope(
    string Type,
    Guid? MessageId,
    Guid? SessionId,
    Guid? From,
    Guid? To,
    DateTimeOffset? Timestamp,
    JsonElement? Payload)
{
    public static class Serializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string ToJson(string type, Guid? to, object? payload) =>
            JsonSerializer.Serialize(new OutboundFrame(type, to, payload), Options);

        public static SignalingEnvelope? TryParse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeElement)) return null;
                var type = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(type)) return null;

                return new SignalingEnvelope(
                    type,
                    ReadGuid(root, "messageId"),
                    ReadGuid(root, "sessionId"),
                    ReadGuid(root, "from"),
                    ReadGuid(root, "to"),
                    ReadTimestamp(root),
                    root.TryGetProperty("payload", out var payload) ? payload.Clone() : null);
            }
            catch (JsonException)
            {
                // A frame we cannot parse is a frame we ignore. Throwing here would tear down
                // a healthy socket over one malformed message.
                return null;
            }
        }

        private static Guid? ReadGuid(JsonElement root, string name) =>
            root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out var value)
                ? value
                : null;

        private static DateTimeOffset? ReadTimestamp(JsonElement root) =>
            root.TryGetProperty("timestamp", out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(element.GetString(), out var value)
                ? value
                : null;

        private sealed record OutboundFrame(
            [property: JsonPropertyName("type")] string Type,
            [property: JsonPropertyName("to")] Guid? To,
            [property: JsonPropertyName("payload")] object? Payload);
    }
}

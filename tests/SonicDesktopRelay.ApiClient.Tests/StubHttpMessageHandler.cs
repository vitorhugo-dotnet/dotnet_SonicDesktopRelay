using System.Net;
using System.Text;

namespace SonicDesktopRelay.ApiClient.Tests;

/// <summary>
/// Answers each request from a queued script and records what was asked. Real HTTP would make
/// these tests slow and flaky; what is under test is the client's own behavior, not the wire.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public StubHttpMessageHandler Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, body) = _responses.Count > 0
            ? _responses.Dequeue()
            : (HttpStatusCode.InternalServerError, "{}");
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

using System.Net;
using Xunit;

namespace SonicDesktopRelay.ApiClient.Tests;

public sealed class DeviceApiClientTests
{
    [Fact]
    public async Task Bootstrap_posts_the_windows_desktop_type_and_the_windows_platform()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.Created,
            """{"deviceId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","credentialSecret":"abc","credentialVersion":1}""");
        var client = new DeviceApiClient(HttpClientFor(handler));

        var response = await client.BootstrapAsync("Desk PC", CancellationToken.None);

        Assert.Equal("/api/devices/bootstrap", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("\"deviceType\":\"windows_desktop\"", handler.RequestBodies[0]);
        Assert.Contains("\"platform\":\"windows\"", handler.RequestBodies[0]);
        Assert.Contains("\"name\":\"Desk PC\"", handler.RequestBodies[0]);
        Assert.Equal("abc", response.CredentialSecret);
        Assert.Equal(1, response.CredentialVersion);
    }

    [Fact]
    public async Task Token_returns_the_rotated_secret_when_the_backend_sends_one()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK,
            """
            {"accessToken":"jwt","expiresAt":"2026-08-23T14:05:00Z","scopes":["session:create"],
             "deviceId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","credentialVersion":2,
             "rotatedCredentialSecret":"new-secret"}
            """);
        var client = new DeviceApiClient(HttpClientFor(handler));

        var response = await client.TokenAsync(Guid.NewGuid(), "old-secret", CancellationToken.None);

        Assert.Equal("jwt", response.AccessToken);
        Assert.Equal("new-secret", response.RotatedCredentialSecret);
        Assert.Equal(2, response.CredentialVersion);
    }

    [Fact]
    public async Task Token_leaves_the_rotated_secret_null_when_the_backend_omits_it()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK,
            """
            {"accessToken":"jwt","expiresAt":"2026-08-23T14:05:00Z","scopes":[],
             "deviceId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","credentialVersion":1,
             "rotatedCredentialSecret":null}
            """);
        var client = new DeviceApiClient(HttpClientFor(handler));

        var response = await client.TokenAsync(Guid.NewGuid(), "secret", CancellationToken.None);

        Assert.Null(response.RotatedCredentialSecret);
    }

    [Fact]
    public async Task A_failure_becomes_an_ApiException_carrying_status_and_error_code()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.BadRequest,
            """{"error":"Unsupported device type.","code":"invalid_device_type"}""");
        var client = new DeviceApiClient(HttpClientFor(handler));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.BootstrapAsync("Desk PC", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("invalid_device_type", exception.ErrorCode);
    }

    [Fact]
    public async Task A_failure_without_a_code_still_produces_an_ApiException()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.ServiceUnavailable, "not json at all");
        var client = new DeviceApiClient(HttpClientFor(handler));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.BootstrapAsync("Desk PC", CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
    }

    private static HttpClient HttpClientFor(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://relay.example.com") };
}

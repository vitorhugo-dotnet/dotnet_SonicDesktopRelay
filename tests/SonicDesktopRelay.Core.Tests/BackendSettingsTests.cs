using Xunit;

namespace SonicDesktopRelay.Core.Tests;

public sealed class BackendSettingsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/api")]
    [InlineData("ftp://example.com")]
    public void Rejects_anything_that_is_not_an_absolute_http_url(string value)
    {
        Assert.Null(BackendSettings.TryParse(value));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.Null(BackendSettings.TryParse(null));
    }

    [Fact]
    public void Accepts_an_https_url_and_keeps_a_trailing_slash()
    {
        var settings = BackendSettings.TryParse("https://relay.example.com");

        Assert.NotNull(settings);
        Assert.Equal("https://relay.example.com/", settings!.BaseAddress.ToString());
    }

    [Fact]
    public void Signaling_over_https_uses_wss()
    {
        var settings = BackendSettings.TryParse("https://relay.example.com")!;
        var sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var uri = settings.SignalingUri(sessionId);

        Assert.Equal("wss://relay.example.com/ws/signaling?sessionId=11111111-2222-3333-4444-555555555555",
            uri.ToString());
    }

    [Fact]
    public void Signaling_over_http_uses_ws()
    {
        var settings = BackendSettings.TryParse("http://localhost:5080")!;

        var uri = settings.SignalingUri(Guid.Empty);

        Assert.StartsWith("ws://localhost:5080/ws/signaling?sessionId=", uri.ToString());
    }
}

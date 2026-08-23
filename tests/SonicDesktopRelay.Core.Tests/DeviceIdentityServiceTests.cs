using Microsoft.Extensions.Time.Testing;
using SonicDesktopRelay.Core.Identity;
using Xunit;

namespace SonicDesktopRelay.Core.Tests;

public sealed class DeviceIdentityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_call_bootstraps_and_persists_the_credential()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var service = new DeviceIdentityService(store, api, TimeFrom(Now));

        var token = await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal("token-1", token);
        Assert.Equal(1, api.BootstrapCalls);
        Assert.NotNull(await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_second_call_reuses_the_stored_credential_and_the_cached_token()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var service = new DeviceIdentityService(store, api, TimeFrom(Now));

        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal(1, api.BootstrapCalls);
        Assert.Equal(1, api.TokenCalls);
    }

    [Fact]
    public async Task The_token_is_refreshed_once_less_than_a_fifth_of_its_life_remains()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var time = TimeFrom(Now);
        var service = new DeviceIdentityService(store, api, time);
        // FakeDeviceApi issues tokens valid for 60 minutes; the refresh threshold is 12 left.
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(49));
        var token = await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal(2, api.TokenCalls);
        Assert.Equal("token-2", token);
    }

    [Fact]
    public async Task A_token_with_plenty_of_life_left_is_not_refreshed()
    {
        var store = new InMemoryCredentialStore();
        var api = new FakeDeviceApi();
        var time = TimeFrom(Now);
        var service = new DeviceIdentityService(store, api, time);
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(30));
        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        Assert.Equal(1, api.TokenCalls);
    }

    [Fact]
    public async Task A_rotated_secret_replaces_both_the_device_id_and_the_secret_on_disk()
    {
        var store = new InMemoryCredentialStore();
        var rotatedDeviceId = Guid.NewGuid();
        var api = new FakeDeviceApi
        {
            RotateOnCall = 1,
            RotatedDeviceId = rotatedDeviceId,
            RotatedSecret = "rotated-secret"
        };
        var service = new DeviceIdentityService(store, api, TimeFrom(Now));

        await service.GetAccessTokenAsync("Desk PC", CancellationToken.None);

        var stored = await store.ReadAsync(CancellationToken.None);
        Assert.Equal(rotatedDeviceId, stored!.DeviceId);
        Assert.Equal("rotated-secret", stored.CredentialSecret);
    }

    private static FakeTimeProvider TimeFrom(DateTimeOffset start) => new(start);

    private sealed class InMemoryCredentialStore : IDeviceCredentialStore
    {
        private DeviceCredential? _credential;

        public Task<DeviceCredential?> ReadAsync(CancellationToken ct) => Task.FromResult(_credential);

        public Task WriteAsync(DeviceCredential credential, CancellationToken ct)
        {
            _credential = credential;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct)
        {
            _credential = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceApi : IDeviceApi
    {
        public int BootstrapCalls { get; private set; }

        public int TokenCalls { get; private set; }

        public int RotateOnCall { get; init; }

        public Guid RotatedDeviceId { get; init; }

        public string? RotatedSecret { get; init; }

        public Task<DeviceCredential> BootstrapAsync(string name, CancellationToken ct)
        {
            BootstrapCalls++;
            return Task.FromResult(new DeviceCredential(Guid.NewGuid(), "secret", 1));
        }

        public Task<AccessToken> TokenAsync(Guid deviceId, string secret, CancellationToken ct)
        {
            TokenCalls++;
            var rotate = TokenCalls == RotateOnCall;
            return Task.FromResult(new AccessToken(
                $"token-{TokenCalls}",
                new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero).AddMinutes(60),
                rotate ? RotatedDeviceId : deviceId,
                1,
                rotate ? RotatedSecret : null));
        }
    }
}

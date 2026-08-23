namespace SonicDesktopRelay.Core.Identity;

/// <summary>
/// Owns the device's identity: registers it once, keeps a valid access token, and survives
/// the backend rotating the identity underneath it. There is no login — the machine's own
/// registration is the account.
/// </summary>
public sealed class DeviceIdentityService(
    IDeviceCredentialStore store,
    IDeviceApi api,
    TimeProvider time)
{
    // Refresh once less than a fifth of the token's lifetime remains. Waiting for a 401
    // would mean discovering the expiry mid-session, and the cheapest moment to renew is
    // any moment that is not "while a screen session is negotiating".
    private const double RefreshWhenRemainingFraction = 0.2;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccessToken? _token;
    private DateTimeOffset _tokenIssuedAt;

    public async Task<string> GetAccessTokenAsync(string deviceName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (_token is not null && !NeedsRefresh(_token, now)) return _token.Value;

            var credential = await store.ReadAsync(ct);
            if (credential is null)
            {
                credential = await api.BootstrapAsync(deviceName, ct);
                await store.WriteAsync(credential, ct);
            }

            var token = await api.TokenAsync(credential.DeviceId, credential.CredentialSecret, ct);
            if (token.RotatedCredentialSecret is not null)
            {
                // The identity we held no longer exists: the next call using the old id would
                // get a 401. Persist the replacement before anything else can read the store.
                await store.WriteAsync(
                    new DeviceCredential(token.DeviceId, token.RotatedCredentialSecret, token.CredentialVersion), ct);
            }

            _token = token;
            _tokenIssuedAt = now;
            return token.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the next call to exchange a fresh token, after a 401 for instance.</summary>
    public void Invalidate() => _token = null;

    private bool NeedsRefresh(AccessToken token, DateTimeOffset now)
    {
        var lifetime = token.ExpiresAt - _tokenIssuedAt;
        if (lifetime <= TimeSpan.Zero) return true;
        var remaining = token.ExpiresAt - now;
        return remaining <= lifetime * RefreshWhenRemainingFraction;
    }
}
